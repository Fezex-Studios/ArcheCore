using System.Collections.Generic;
using ArcheCore.WorldServer.Managers;
using LiteNetLib;
using Shared;
using Shared.Components;

namespace ArcheCore.WorldServer.Networking.W2C
{
    public static class W2CPlayerLeavePacketSender
    {
        public static void Send(
            ReplicationManager replication,
            IEnumerable<NetPeer> peers,
            int networkId)
        {
            replication.Broadcast(
                Opcode.PlayerLeave,
                new W2CPlayerLeavePacket { NetworkId = networkId },
                peers);
        }
    }
}