using System.Collections.Generic;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Managers;
using LiteNetLib;

namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CChatMessagePacketSender
    {
        public static void Send(
            ReplicationManager replication,
            IEnumerable<NetPeer> peers,
            int senderNetworkId,
            string senderName,
            string message,
            ChatChannel channel)
        {
            replication.Broadcast(
                Opcodes.ChatMessage,
                new W2CChatMessagePacket
                {
                    SenderNetworkId = senderNetworkId,
                    SenderName      = senderName,
                    Message         = message,
                    Channel         = channel
                },
                peers);
        }
    }
}