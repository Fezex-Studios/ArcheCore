using System.Collections.Generic;
using ArcheCore.WorldServer.Managers;
using LiteNetLib;
using Shared;
using Shared.Packets;

namespace ArcheCore.WorldServer.Networking.W2C
{
    public static class W2CMOTDPacketSender
    {
        public static void Send(
            ReplicationManager replication,
            NetPeer peer,
            string message)
        {
            replication.Send(
                Opcode.MOTD,
                new W2CMOTDPacket { Message = message },
                peer);
        }
    }
}