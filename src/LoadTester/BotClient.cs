using System.Numerics;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using LiteNetLib.Utils;
using MessagePack;

namespace ArcheCore.LoadTester
{
    /// <summary>
    /// One simulated player. Same opcode sequence and wire format as a
    /// real client — Authenticate -> character list -> select/create ->
    /// spawn -> movement.
    ///
    /// REWRITTEN from the original version, which gave every bot its own
    /// long-lived polling Task (Task.Delay(15ms) in a loop) and its own
    /// movement Task (Task.Delay(500ms) in a loop). At a few thousand
    /// bots that's tens of thousands of concurrent async loops fighting
    /// the .NET thread pool for the same cores your actual server
    /// processes need — and on a single dev box, that thread-pool
    /// contention hits its own wall well before the server does. A test
    /// run that plateaus under those conditions is measuring the tester,
    /// not the server.
    ///
    /// This version has NO long-lived Task of its own. It's pure state +
    /// an INetEventListener. A single shared driver loop (BotSwarm) calls
    /// Poll(), CheckTimeout(), and MaybeMove() on every bot once per
    /// shared tick — O(bots) work on ONE thread instead of N threads
    /// each doing O(1) work. Socket count is unavoidable (LiteNetLib
    /// dedupes peers by remote endpoint, so each simulated client still
    /// needs its own NetManager/local port to connect to the same
    /// server address) — what's eliminated is the scheduling overhead,
    /// which is what a busy dev box runs out of first.
    /// </summary>
    public sealed class BotClient : INetEventListener
    {
        public enum State { NotStarted, Connecting, Authenticating, Spawned, Failed }

        private readonly int _index;
        private readonly string _host;
        private readonly int _port;
        private readonly Metrics _metrics;
        private readonly float _moveIntervalSeconds;
        private readonly Random _rng;

        private NetManager? _net;
        private NetPeer? _peer;
        private string? _token;
        private int _networkId = -1;
        private Vector3 _position;

        private DateTime _connectStartUtc;
        private DateTime _lastMoveUtc;

        public State CurrentState { get; private set; } = State.NotStarted;

        public BotClient(int index, string host, int port, Metrics metrics, float moveIntervalSeconds = 0.5f)
        {
            _index = index;
            _host = host;
            _port = port;
            _metrics = metrics;
            _moveIntervalSeconds = moveIntervalSeconds;
            _rng = new Random(index * 7919 + 13);
        }

        /// <summary>
        /// Fetches a token (may be a real HTTP round trip for
        /// HttpLoginTokenSource) then opens the socket and starts the
        /// LiteNetLib handshake. Returns as soon as the connect attempt
        /// is IN FLIGHT — does not block waiting for it to complete. The
        /// shared driver loop's CheckTimeout picks up from here.
        /// </summary>
        public async Task StartAsync(ITokenSource tokens, CancellationToken ct)
        {
            try
            {
                _token = await tokens.GetTokenAsync(_index, ct);
            }
            catch
            {
                _metrics.AuthFailed();
                CurrentState = State.Failed;
                return;
            }

            _net = new NetManager(this) { UnconnectedMessagesEnabled = false };
            _net.Start();

            _connectStartUtc = DateTime.UtcNow;
            CurrentState = State.Connecting;

            // Must match WorldServer.cs's private ConnectionKey exactly —
            // a mismatch here looks identical to the server not
            // responding at all.
            _net.Connect(_host, _port, "MMO");
        }

        /// <summary>Call once per shared tick for every bot. Cheap no-op for bots not yet started.</summary>
        public void Poll() => _net?.PollEvents();

        /// <summary>
        /// Call once per shared tick. Central timeout check replaces the
        /// old per-bot wait-loop — one comparison instead of an entire
        /// await chain per bot.
        /// </summary>
        public void CheckTimeout(DateTime now)
        {
            if (CurrentState != State.Connecting) return;
            if ((now - _connectStartUtc).TotalSeconds < 5) return;

            _metrics.ConnectTimedOut();
            CurrentState = State.Failed;

            // Release the socket immediately rather than leaving a
            // doomed connection attempt bound to a local port — at a
            // few thousand bots, abandoned sockets are exactly the kind
            // of thing that turns into real resource pressure.
            _net?.Stop();
        }

        /// <summary>Call once per shared tick. Internally rate-limits to _moveIntervalSeconds per bot.</summary>
        public void MaybeMove(DateTime now)
        {
            if (CurrentState != State.Spawned) return;
            if ((now - _lastMoveUtc).TotalSeconds < _moveIntervalSeconds) return;

            _lastMoveUtc = now;

            var angle = _rng.NextDouble() * Math.PI * 2;
            _position += new Vector3(
                (float)Math.Cos(angle) * 2f,
                0f,
                (float)Math.Sin(angle) * 2f);

            Send(Opcodes.PlayerMove,
                new C2WPlayerMovePacket { x = _position.X, y = _position.Y, z = _position.Z },
                DeliveryMethod.Unreliable);
        }

        private void Send<T>(Opcodes opcode, T payload, DeliveryMethod delivery = DeliveryMethod.ReliableOrdered)
        {
            if (_peer is null) return;

            var writer = new NetDataWriter();
            writer.Put((ushort)opcode);
            writer.Put(MessagePackSerializer.Serialize(payload));

            _peer.Send(writer, delivery);
            _metrics.AddSent(writer.Length);
        }

        // ---- INetEventListener ----

        public void OnPeerConnected(NetPeer peer)
        {
            _peer = peer;
            CurrentState = State.Authenticating;
            _metrics.BotConnected();

            Send(Opcodes.Authenticate, new C2WAuthenticateRequest { Token = _token! });
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            _metrics.BotDisconnected();
            CurrentState = State.Failed;
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            _metrics.AddReceived(reader.AvailableBytes + 2);

            var opcode = (Opcodes)reader.GetUShort();
            var payload = reader.GetRemainingBytes();

            switch (opcode)
            {
                case Opcodes.W2CCharacterList:
                {
                    var list = MessagePackSerializer.Deserialize<W2CCharacterListPacket>(payload);
                    if (list.Characters is { Length: > 0 })
                    {
                        Send(Opcodes.C2WSelectCharacter,
                            new C2WSelectCharacterRequest { CharacterId = list.Characters[0].CharacterId });
                    }
                    else
                    {
                        Send(Opcodes.C2WCreateCharacterRequest,
                            new C2WCreateCharacterRequest { Name = $"LoadBot{_index:0000}" });
                    }
                    break;
                }

                case Opcodes.SpawnPlayer:
                {
                    var spawn = MessagePackSerializer.Deserialize<W2CSpawnPlayerPacket>(payload);
                    if (spawn.IsLocalPlayer)
                    {
                        _networkId = spawn.NetworkId;
                        _position = new Vector3(spawn.x, spawn.y, spawn.z);
                        _lastMoveUtc = DateTime.UtcNow;
                        CurrentState = State.Spawned;
                        _metrics.BotSpawned();
                    }
                    break;
                }
            }

            reader.Recycle();
        }

        public void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError socketError) { }
        public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnConnectionRequest(ConnectionRequest request) { }
    }
}