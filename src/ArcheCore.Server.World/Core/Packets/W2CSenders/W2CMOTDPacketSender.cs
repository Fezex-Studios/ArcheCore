using System.Collections.Generic;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Managers;
using LiteNetLib;


namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CMOTDPacketSender
    {
        public static void Send(
            ReplicationManager replication,
            NetPeer peer,
            string message)
        {
            replication.Send(
                Opcodes.MOTD,
                new W2CMOTDPacket { Message = message },
                peer);
        }
    }
}