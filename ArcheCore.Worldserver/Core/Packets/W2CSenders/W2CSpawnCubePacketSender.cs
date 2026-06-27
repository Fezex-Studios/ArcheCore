using System.Collections.Generic;
using System.Numerics;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Net.Shared.Packets.W2C;
using ArcheCore.WorldServer.Managers;
using LiteNetLib;
using Shared;

namespace ArcheCore.WorldServer.Networking.W2C
{
    public static class W2CSpawnCubePacketSender
    {
        public static void Send(
            ReplicationManager replication,
            NetPeer peer,
            int cubeId,
            Vector3 position)
        {
            replication.Send(
                Opcodes.SpawnCube,
                new W2CSpawnCubePacket
                {
                    CubeId = cubeId,
                    x = position.X,
                    y = position.Y,
                    z = position.Z
                },
                peer);
        }

        public static void Broadcast(
            ReplicationManager replication,
            IEnumerable<NetPeer> peers,
            int cubeId,
            Vector3 position)
        {
            replication.Broadcast(
                Opcodes.SpawnCube,
                new W2CSpawnCubePacket
                {
                    CubeId = cubeId,
                    x = position.X,
                    y = position.Y,
                    z = position.Z
                },
                peers);
        }
    }
}