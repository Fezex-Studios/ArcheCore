using System.Collections.Generic;
using System.Numerics;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using Shared;


namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CPlayerPositionPacketSender
    {
        public static void SendUnreliable(
            ReplicationManager replication,
            IEnumerable<NetPeer> peers,
            NetPeer except,
            int networkId,
            Vector3 position)
        {
            replication.SendUnreliable(
                Opcodes.PlayerPosition,
                new W2CPlayerPositionPacket
                {
                    NetworkId = networkId,
                    x         = position.X,
                    y         = position.Y,
                    z         = position.Z
                },
                peers,
                except);
        }
    }
}