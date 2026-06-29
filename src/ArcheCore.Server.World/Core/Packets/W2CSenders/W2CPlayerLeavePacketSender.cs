using System.Collections.Generic;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using Shared;

namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CPlayerLeavePacketSender
    {
        public static void Send(
            ReplicationManager replication,
            IEnumerable<NetPeer> peers,
            int networkId)
        {
            replication.Broadcast(
                Opcodes.PlayerLeave,
                new W2CPlayerLeavePacket { NetworkId = networkId },
                peers);
        }
    }
}