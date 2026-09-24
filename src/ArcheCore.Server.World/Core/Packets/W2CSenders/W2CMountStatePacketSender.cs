using System.Collections.Generic;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using Shared;

namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CMountStatePacketSender
    {
        public static void Send(ReplicationManager replication, IEnumerable<NetPeer> peers,
                                int playerNetworkId, int mountId, string modelType, float speedMultiplier)
        {
            replication.Broadcast(
                Opcodes.W2CMountState,
                new W2CMountStatePacket
                {
                    PlayerNetworkId = playerNetworkId,
                    MountId         = mountId,
                    ModelType       = modelType ?? string.Empty,
                    SpeedMultiplier = speedMultiplier
                },
                peers);
        }
    }
}
