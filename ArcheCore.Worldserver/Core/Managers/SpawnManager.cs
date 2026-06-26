using System.Collections.Generic;
using ArcheCore.WorldServer.Networking.W2C;
using LiteNetLib;


namespace ArcheCore.WorldServer.Managers
{
    public class SpawnManager
    {
        private int nextCubeId = 1;
        private readonly Dictionary<int, Vector3> cubes = new();
        private readonly ReplicationManager _replication;

        public SpawnManager(ReplicationManager replication)
        {
            _replication = replication;
        }

        public void SpawnInitialCubes()
        {
            SpawnCube(new Vector3( 3f, 0.5f,  0f));
            SpawnCube(new Vector3(-3f, 0.5f,  0f));
            SpawnCube(new Vector3( 0f, 0.5f,  4f));
            SpawnCube(new Vector3( 0f, 0.5f, -4f));
            SpawnCube(new Vector3( 5f, 0.5f,  5f));

            WorldLogger.Info($"[SpawnManager] Spawned {cubes.Count} cubes.");
        }

        private void SpawnCube(Vector3 position)
        {
            int id = nextCubeId++;
            cubes[id] = position;
        }

        public void SendCubesToPeer(NetPeer peer)
        {
            foreach (var kvp in cubes)
            {
                W2CSpawnCubePacketSender.Send(
                    _replication,
                    peer,
                    kvp.Key,
                    kvp.Value.x,
                    kvp.Value.y,
                    kvp.Value.z);
            }
        }

        public void SpawnCubeForAll(Vector3 position, IEnumerable<NetPeer> peers)
        {
            int id = nextCubeId++;
            cubes[id] = position;

            W2CSpawnCubePacketSender.Broadcast(
                _replication,
                peers,
                id,
                position.x,
                position.y,
                position.z);
        }
    }
}