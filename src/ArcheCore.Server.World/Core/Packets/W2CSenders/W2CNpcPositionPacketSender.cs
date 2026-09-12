using System.Collections.Generic;
using System.Numerics;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using Shared;

namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CNpcPositionPacketSender
    {
        public static void SendUnreliable(
            ReplicationManager replication,
            IEnumerable<NetPeer> peers,
            int networkId,
            Vector3 position)
        {
            replication.SendUnreliable(
                Opcodes.NpcPosition,
                new W2CNpcPositionPacket
                {
                    NetworkId = networkId,
                    x         = position.X,
                    y         = position.Y,
                    z         = position.Z
                },
                peers,
                null);
        }
    }
}
