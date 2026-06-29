using System.Collections.Generic;
using System.Numerics;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.WorldServer.Managers;
using LiteNetLib;
using Shared;
using Shared.Components;


namespace ArcheCore.WorldServer.Networking.W2C
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