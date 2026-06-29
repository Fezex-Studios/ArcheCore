using System.Collections.Generic;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Managers;
using LiteNetLib;


namespace ArcheCore.Server.World.Networking.W2C
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