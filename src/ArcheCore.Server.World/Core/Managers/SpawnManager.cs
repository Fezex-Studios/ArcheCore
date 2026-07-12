using System;
using System.Collections.Generic;
using System.Numerics;
using ArcheCore.Server.World.GameData.World.Spawners;
using ArcheCore.Server.World.Utils.Database.SQLite;
using LiteNetLib;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace ArcheCore.Server.World.Managers;

public class SpawnManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly Random _rng   = new();

    private readonly ReplicationManager _replication;
    private readonly IDbContextFactory<WorldDataDbContext> _dbFactory;
    private readonly NpcSpawner _npcSpawner;

    private int _nextId = 1;

    public SpawnManager(
        ReplicationManager replication,
        IDbContextFactory<WorldDataDbContext> dbFactory,
        InteractionRegistry interactions)
    {
        _replication = replication;
        _dbFactory   = dbFactory;
        _npcSpawner  = new NpcSpawner(replication, interactions);
    }

    public void SpawnInitialObjects()
    {
        using var db = _dbFactory.CreateDbContext();

        var spawners  = db.NpcSpawners.ToList();
        var templates = db.NpcTemplates.ToDictionary(t => t.Id);

        foreach (var spawner in spawners)
        {
            if (!templates.TryGetValue(spawner.TemplateId, out var template))
            {
                Logger.Warn($"NpcSpawner {spawner.Id} references unknown TemplateId {spawner.TemplateId}");
                continue;
            }

            for (int i = 0; i < spawner.Count; i++)
            {
                int networkId = _nextId++;

                double angle  = _rng.NextDouble() * Math.PI * 2;
                double dist   = _rng.NextDouble() * spawner.Radius;
                float  spawnX = spawner.X + (float)(Math.Cos(angle) * dist);
                float  spawnZ = spawner.Z + (float)(Math.Sin(angle) * dist);

                _npcSpawner.SpawnFromTemplate(
                    networkId,
                    template,
                    new Vector3(spawnX, spawner.Y, spawnZ));

                Logger.Info($"Spawned '{template.Name}' (NetworkId={networkId}) at ({spawnX:F1}, {spawner.Y}, {spawnZ:F1})");
            }
        }
    }

    public void SendWorldToPeer(NetPeer peer)
    {
        _npcSpawner.SendToPeer(peer);
    }
}