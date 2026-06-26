using System.Collections.Generic;
using ArcheCore.WorldServer.Managers;
using LiteNetLib;
using Shared;
using Shared.Components;

namespace ArcheCore.WorldServer.Networking.W2C
{
    public static class W2CSpawnCubePacketSender
    {
        public static void Send(
            ReplicationManager replication,
            NetPeer peer,
            int cubeId,
            float x, float y, float z)
        {
            replication.Send(
                Opcode.SpawnCube,
                new W2CSpawnCubePacket { CubeId = cubeId, x = x, y = y, z = z },
                peer);
        }

        public static void Broadcast(
            ReplicationManager replication,
            IEnumerable<NetPeer> peers,
            int cubeId,
            float x, float y, float z)
        {
            replication.Broadcast(
                Opcode.SpawnCube,
                new W2CSpawnCubePacket { CubeId = cubeId, x = x, y = y, z = z },
                peers);
        }
    }
}