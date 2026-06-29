using System.Collections.Generic;
using System.Numerics;
using ArcheCore.WorldServer.Networking.W2C;
using LiteNetLib;
using NLog;

namespace ArcheCore.WorldServer.Managers;

public class SpawnManager(ReplicationManager replication)
{
    private int _nextCubeId = 1;
    private readonly Dictionary<int, Vector3> _cubes = new();

    private static readonly Logger Logger =
        LogManager.GetCurrentClassLogger();

    public void SpawnInitialCubes()
    {
        SpawnCube(new Vector3(3f, 0.5f, 0f));
        SpawnCube(new Vector3(-3f, 0.5f, 0f));
        SpawnCube(new Vector3(0f, 0.5f, 4f));
        SpawnCube(new Vector3(0f, 0.5f, -4f));
        SpawnCube(new Vector3(5f, 0.5f, 5f));

        Logger.Info($"Spawned {_cubes.Count} cubes.");
    }

    private void SpawnCube(Vector3 position)
    {
        int id = _nextCubeId++;
        _cubes[id] = position;
    }

    public void SendCubesToPeer(NetPeer peer)
    {
        foreach (var kvp in _cubes)
        {
            W2CSpawnCubePacketSender.Send(
                replication,
                peer,
                kvp.Key,
                kvp.Value);
        }
    }

    public void SpawnCubeForAll(
        Vector3 position,
        IEnumerable<NetPeer> peers)
    {
        int id = _nextCubeId++;
        _cubes[id] = position;

        W2CSpawnCubePacketSender.Broadcast(
            replication,
            peers,
            id,
            position);
    }
}