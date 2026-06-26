using System.Collections.Generic;
using ArcheCore.WorldServer.Managers;
using LiteNetLib;
using Shared;
using Shared.Packets;

namespace ArcheCore.WorldServer.Networking.W2C
{
    public static class W2CAnnouncementPacketSender
    {
        public static void Send(
            ReplicationManager replication,
            IEnumerable<NetPeer> peers,
            string message)
        {
            replication.Broadcast(
                Opcode.Announcement,
                new W2CAnnouncementPacket { Message = message },
                peers);
        }
    }
}