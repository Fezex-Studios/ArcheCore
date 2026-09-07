using System.Collections.Generic;
using System.Linq;
using ArcheCore.Network.Shared;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using MessagePack;
using NLog;

namespace ArcheCore.Server.World.Networking.C2W
{
    [PacketOpcode(Opcodes.ChatMessage)]
public class C2WChatHandler : IPacketHandler
    {
        private static readonly Logger Logger =
            LogManager.GetCurrentClassLogger();

        private const int ShoutRadiusCells = 4;

        private readonly PlayerManager      _playerManager;
        private readonly InterestManager    _interest;
        private readonly ReplicationManager _replication;

        public C2WChatHandler(
            PlayerManager      playerManager,
            InterestManager    interest,
            ReplicationManager replication)
        {
            _playerManager = playerManager;
            _interest      = interest;
            _replication   = replication;
        }

        public void Handle(NetPeer peer, NetPacketReader reader)
        {
            if (!_playerManager.TryGetNetworkId(peer, out int networkId))
                return;

            if (!_playerManager.TryGetSession(peer, out PlayerSession session))
                return;

            var request = MessagePackSerializer
                .Deserialize<C2WChatMessagePacket>(
                    reader.GetRemainingBytes());

            string message = request.Message?.Trim() ?? string.Empty;

            if (message.Length == 0 || message.Length > 200)
            {
                Logger.Warn(
                    $"[Chat] Rejected message from NetworkId={networkId} " +
                    $"(length {message.Length})");
                return;
            }

            switch (request.Channel)
            {
                case ChatChannel.Local:
                    SendToRecipients(
                        _interest.GetKnownBy(networkId),
                        peer, networkId, session.Name, message, request.Channel);
                    break;

                case ChatChannel.Shout:
                    SendToRecipients(
                        _interest.GetNearbyAtRadius(networkId, ShoutRadiusCells),
                        peer, networkId, session.Name, message, request.Channel);
                    break;

                case ChatChannel.Whisper:
                    HandleWhisper(peer, networkId, session.Name, request.TargetName, message);
                    break;
            }
        }

        private void SendToRecipients(
            IEnumerable<int> nearbyNetworkIds,
            NetPeer senderPeer,
            int senderNetworkId,
            string senderName,
            string message,
            ChatChannel channel)
        {
            var peers = nearbyNetworkIds
                .Select(id => _playerManager.TryGetPeer(id, out var p) ? p : null)
                .Where(p => p != null)
                .Append(senderPeer);

            W2CChatMessagePacketSender.Send(
                _replication, peers, senderNetworkId, senderName, message, channel);
        }

        private void HandleWhisper(
            NetPeer senderPeer,
            int senderNetworkId,
            string senderName,
            string targetName,
            string message)
        {
            if (string.IsNullOrWhiteSpace(targetName) ||
                !_playerManager.TryGetPeerByName(targetName, out var targetPeer))
            {
                W2CChatMessagePacketSender.Send(
                    _replication,
                    new[] { senderPeer },
                    senderNetworkId,
                    "System",
                    $"Player '{targetName}' is not online.",
                    ChatChannel.Whisper);
                return;
            }

            W2CChatMessagePacketSender.Send(
                _replication,
                new[] { senderPeer, targetPeer },
                senderNetworkId,
                senderName,
                message,
                ChatChannel.Whisper);
        }
    }
}