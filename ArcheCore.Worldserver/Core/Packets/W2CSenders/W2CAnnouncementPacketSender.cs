using System.Collections.Generic;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.WorldServer.Managers;
using LiteNetLib;
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
                Opcodes.Announcement,
                new W2CAnnouncementPacket { Message = message },
                peers);
        }
    }
}