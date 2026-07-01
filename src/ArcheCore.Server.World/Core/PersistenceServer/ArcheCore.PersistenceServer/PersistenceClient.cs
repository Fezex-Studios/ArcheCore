using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Tasks;
using ArcheCore.Network.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using ArcheCore.Server.World.PersistenceServer.Networking;
using ArcheCore.Server.World.PersistenceServer.Networking.P2W;

using ArcheCore.Server.World.PersistenceServer.Senders;
using ArcheCore.Server.World.Utils.Config;
using MessagePack;
using NLog;


namespace Worldserver.ArcheCore.PersistenceServer.Scripts
{
    public class PersistenceClient 
    {
        private TcpClient client;
        private NetworkStream stream;
        private PersistenceDispatcher dispatcher;
        private readonly WorldServerConfig _worldConfig;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        internal readonly ConcurrentDictionary<long, TaskCompletionSource<P2WCharacterLoadResponse>>
            pendingLoads = new();

        
        public W2PCharacterSender W2PCharacter { get; private set; }
        public W2PHelloWorldSender W2PHelloWorld { get; private set; }

        
        public PersistenceClient(WorldServerConfig worldConfig)
        {
            _worldConfig = worldConfig;
        }

        public async Task Start()
        {

            dispatcher = new PersistenceDispatcher();
            W2PCharacter  = new W2PCharacterSender(this);
            W2PHelloWorld = new W2PHelloWorldSender(this);

            RegisterHandlers();

            client = new TcpClient();

            await client.ConnectAsync(
                "127.0.0.1",
                _worldConfig.PersistencePort);

            stream = client.GetStream();

            _ = ReceiveLoop();

            await Send(
                PServerOpcodes.W2PConnectRequest,
                new W2PConnectionRequest
                {
                    Message = "WorldServer 1 has connected"
                });
            await W2PHelloWorld.Send("Hello THIS IS A MESSAGE SENT FROM THE WORLDSERVER");
        }

        private void RegisterHandlers()
        {
            dispatcher.Register(
                PServerOpcodes.P2WConnectResponse,
                new P2WConnectResponseHandler());

            dispatcher.Register(
                PServerOpcodes.CharacterLoad,
                new P2WCharacterLoadHandler(this));
        }

        public void ResolveLoad(P2WCharacterLoadResponse response)
        {
            if (pendingLoads.TryRemove(response.CharacterId, out var tcs))
                tcs.SetResult(response);
            else
                Logger.Warn(
                    $"[PersistenceClient] No pending load for CharacterId={response.CharacterId}");
        }

        internal async Task Send<T>(PServerOpcodes opcode, T payload)
        {
            PersistencePacket persistencePacket = new PersistencePacket
            {
                Opcode  = (ushort)opcode,
                Payload = MessagePackSerializer.Serialize(payload)
            };

            byte[] packetBytes = MessagePackSerializer.Serialize(persistencePacket);
            byte[] lengthBytes = BitConverter.GetBytes(packetBytes.Length);

            await stream.WriteAsync(lengthBytes);
            await stream.WriteAsync(packetBytes);

            Logger.Info($"[World] Sent {opcode}");
        }

        private async Task ReceiveLoop()
        {
            while (client.Connected)
            {
                try
                {
                    byte[] lengthBuffer = new byte[4];
                    int read = await ReadExact(lengthBuffer, 4);

                    if (read == 0)
                        break;

                    int packetLength = BitConverter.ToInt32(lengthBuffer, 0);
                    byte[] packetBuffer = new byte[packetLength];

                    await ReadExact(packetBuffer, packetLength);

                    PersistencePacket persistencePacket = MessagePackSerializer.Deserialize<PersistencePacket>(packetBuffer);

                    Logger.Info($"[World] Received {(PServerOpcodes)persistencePacket.Opcode}");

                    dispatcher.Handle(persistencePacket);
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                    break;
                }
            }
        }

        private async Task<int> ReadExact(byte[] buffer, int size)
        {
            int totalRead = 0;

            while (totalRead < size)
            {
                int read = await stream.ReadAsync(buffer, totalRead, size - totalRead);

                if (read == 0)
                    return 0;

                totalRead += read;
            }

            return totalRead;
        }

        private void OnDestroy()
        {
            stream?.Close();
            client?.Close();
        }
    }
}