using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ArcheCore.Server.World.Core.Entities;
using ArcheCore.Server.World.GameData.Npcs;
using ArcheCore.Server.World.GameData.World.Spawners;
using ArcheCore.Server.World.Networking.W2C;
using ArcheCore.Server.World.Utils.Database.SQLite;
using LiteNetLib;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace ArcheCore.Server.World.Managers;

/// <summary>
/// Owns spawner *definitions* (loaded from DB) and decides, on a timer,
/// which spawners should currently have their NPCs alive - mirrors
/// AAEmu's ActiveRegionTick -> IsSpawnerActive -> NpcSpawner.Update()
/// pattern: a spawner only spawns its group once a player is within
/// its radius, and despawns the group again once nobody's been nearby
/// for a grace period.
///
/// This used to spawn every NPC from every spawner unconditionally at
/// boot (SpawnInitialObjects). That's gone - see LoadSpawnerDefinitions,
/// which only loads the *definitions*, and ScanSpawners, which is what
/// actually spawns/despawns groups based on player proximity.
///
/// ScanSpawners is MAIN-THREAD ONLY. InterestManager/SpatialGrid dropped
/// their internal locking (see those classes) on the assumption that
/// exactly one thread ever touches them - that's now the main tick
/// thread, via NpcAiManager.Tick(). It reads InterestManager/SpatialGrid
/// and returns the resulting spawn/despawn work as data; it does NOT
/// mutate NpcSpawner's live dictionary or InteractionRegistry itself, or
/// send any packets - callers still run ApplySpawn/ApplyDespawn through
/// the existing _pendingActions queue pattern in PlayerManager, which is
/// now a same-thread deferral (applied next tick) rather than a
/// cross-thread handoff.
/// </summary>
public class SpawnManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly Random _rng   = new();

    /// <summary>
    /// How long a spawner group stays alive with nobody nearby before it's
    /// torn down again. Without this, a player clipping the edge of a
    /// spawner's radius for one tick would cause a spawn/despawn every
    /// second - this smooths that out.
    /// </summary>
    private static readonly TimeSpan DespawnGrace = TimeSpan.FromSeconds(30);

    /// <summary>InactiveSince when the spawner has players nearby (ServerClock time otherwise).</summary>
    private static readonly TimeSpan NotInactive = TimeSpan.MinValue;

    /// <summary>
    /// NPC network ids start well above where player ids will ever reach
    /// (SessionManager's ids start at 1 and MaxPlayers is in the hundreds),
    /// so NPCs and players can share one InterestManager/SpatialGrid with
    /// no id collisions. That's what lets a player's ordinary movement
    /// update discover nearby NPCs for free, instead of needing a second,
    /// parallel "am I near an NPC" system.
    /// </summary>
    public const int NpcIdBase = 1_000_000;

    private readonly IDbContextFactory<WorldDataDbContext> _dbFactory;
    private readonly NpcSpawner _npcSpawner;
    private readonly InterestManager _interest;

    private int _nextId = NpcIdBase;

    private readonly Dictionary<int, SpawnerRuntime> _spawners = new();

    // Other kinds of non-player entity sharing the grid (harvest nodes...).
    // See IWorldEntitySource.
    private readonly List<IWorldEntitySource> _entitySources = new();

    /// <summary>Every NPC template by id, so pets can be summoned by template.</summary>
    private Dictionary<int, NpcTemplate> _templates = new();

    // Reused across ScanSpawners' per-spawner proximity checks - safe as a
    // single field because ScanSpawners runs synchronously on one thread
    // (see class remarks) and nothing holds onto this list across calls.
    private readonly List<int> _nearbyScratch = new(capacity: 64);

    private class SpawnerRuntime
    {
        public NpcSpawnerTable Record;
        public NpcTemplate Template;
        public readonly List<int> LiveNpcIds = new();
        public bool IsActive;
        public TimeSpan InactiveSince = NotInactive;
    }

    public SpawnManager(
        IDbContextFactory<WorldDataDbContext> dbFactory,
        InteractionRegistry interactions,
        InterestManager interest)
    {
        _dbFactory  = dbFactory;
        _npcSpawner = new NpcSpawner(interactions);
        _interest   = interest;
    }

    /// <summary>Loads spawner rows/templates from the DB. Spawns nothing - spawners start dormant.</summary>
    public void LoadSpawnerDefinitions()
    {
        using var db = _dbFactory.CreateDbContext();

        var spawners  = db.NpcSpawners.ToList();
        var templates = db.NpcTemplates.ToDictionary(t => t.Id);
        _templates = templates;

        foreach (var spawner in spawners)
        {
            if (!templates.TryGetValue(spawner.TemplateId, out var template))
            {
                Logger.Warn($"NpcSpawner {spawner.Id} references unknown TemplateId {spawner.TemplateId}");
                continue;
            }

            _spawners[spawner.Id] = new SpawnerRuntime { Record = spawner, Template = template };
        }

        Logger.Info($"Loaded {_spawners.Count} NPC spawner definitions (dormant until a player is nearby).");
    }

    /// <summary>
    /// Read-only pass over every spawner: which should activate, which
    /// should deactivate. Call from any thread - only touches the
    /// thread-safe InterestManager. Returns instructions; apply them on
    /// the main thread with ApplySpawn/ApplyDespawn.
    /// </summary>
    public (List<PendingSpawn> toActivate, List<int> spawnerIdsToDeactivate) ScanSpawners()
    {
        var toActivate = new List<PendingSpawn>();
        var toDeactivate = new List<int>();

        foreach (var runtime in _spawners.Values)
        {
            var position = new Vector3(runtime.Record.X, runtime.Record.Y, runtime.Record.Z);
            int radiusCells = CellsForRadius(runtime.Record.Radius);
            bool playerNearby = HasPlayerNearby(position, radiusCells);

            if (!runtime.IsActive)
            {
                if (playerNearby)
                    toActivate.Add(new PendingSpawn(runtime.Record.Id));
            }
            else if (playerNearby)
            {
                runtime.InactiveSince = NotInactive;
            }
            else
            {
                if (runtime.InactiveSince == NotInactive)
                    runtime.InactiveSince = ServerClock.Elapsed;
                else if (ServerClock.Elapsed - runtime.InactiveSince > DespawnGrace)
                    toDeactivate.Add(runtime.Record.Id);
            }
        }

        return (toActivate, toDeactivate);
    }

    public readonly record struct PendingSpawn(int SpawnerId);

    /// <summary>Main-thread only. Actually spawns a spawner's NPC group and registers each in the shared InterestManager grid.</summary>
    public List<NpcEntity> ApplyActivate(int spawnerId)
    {
        var spawned = new List<NpcEntity>();

        if (!_spawners.TryGetValue(spawnerId, out var runtime) || runtime.IsActive)
            return spawned;

        runtime.IsActive = true;
        runtime.InactiveSince = NotInactive;

        for (int i = 0; i < runtime.Record.Count; i++)
        {
            int networkId = _nextId++;

            double angle = _rng.NextDouble() * Math.PI * 2;
            double dist  = _rng.NextDouble() * runtime.Record.Radius;
            float spawnX = runtime.Record.X + (float)(Math.Cos(angle) * dist);
            float spawnZ = runtime.Record.Z + (float)(Math.Sin(angle) * dist);
            var pos = new Vector3(spawnX, runtime.Record.Y, spawnZ);

            var npc = _npcSpawner.SpawnFromTemplate(networkId, runtime.Record.Id, runtime.Template, pos);
            runtime.LiveNpcIds.Add(networkId);
            spawned.Add(npc);
        }

        Logger.Info($"Spawner {runtime.Record.Id} activated: spawned {spawned.Count}x '{runtime.Template.Name}'.");
        return spawned;
    }

    /// <summary>Main-thread only. Despawns a spawner's whole NPC group. Returns the ids that were removed.</summary>
    public List<int> ApplyDeactivate(int spawnerId)
    {
        if (!_spawners.TryGetValue(spawnerId, out var runtime) || !runtime.IsActive)
            return new List<int>();

        runtime.IsActive = false;
        var removed = new List<int>(runtime.LiveNpcIds);

        foreach (var id in removed)
            _npcSpawner.DespawnNpc(id);

        runtime.LiveNpcIds.Clear();

        Logger.Info($"Spawner {runtime.Record.Id} deactivated: despawned {removed.Count} NPCs (no players nearby for {DespawnGrace.TotalSeconds:F0}s).");
        return removed;
    }

    /// <summary>Main-thread only. Removes a single NPC (e.g. it died) without tearing down the whole spawner group.</summary>
    public void ApplyDespawnSingle(NpcEntity npc)
    {
        if (_spawners.TryGetValue(npc.SpawnerId, out var runtime))
            runtime.LiveNpcIds.Remove(npc.NetworkId);

        _npcSpawner.DespawnNpc(npc.NetworkId);
    }

    /// <summary>
    /// Main-thread only. Replaces ONE NPC in an active spawner that's below
    /// its Count (one died). Returns null if the spawner is inactive or
    /// already full - an inactive spawner refills its whole group on its
    /// next activation anyway.
    /// </summary>
    public NpcEntity ApplyRespawnOne(int spawnerId)
    {
        if (!_spawners.TryGetValue(spawnerId, out var runtime) || !runtime.IsActive)
            return null;

        if (runtime.LiveNpcIds.Count >= runtime.Record.Count)
            return null;

        int networkId = _nextId++;

        double angle = _rng.NextDouble() * Math.PI * 2;
        double dist  = _rng.NextDouble() * runtime.Record.Radius;
        var pos = new Vector3(
            runtime.Record.X + (float)(Math.Cos(angle) * dist),
            runtime.Record.Y,
            runtime.Record.Z + (float)(Math.Sin(angle) * dist));

        var npc = _npcSpawner.SpawnFromTemplate(networkId, runtime.Record.Id, runtime.Template, pos);
        runtime.LiveNpcIds.Add(networkId);

        Logger.Info($"Spawner {runtime.Record.Id} respawned a '{runtime.Template.Name}' ({runtime.LiveNpcIds.Count}/{runtime.Record.Count}).");
        return npc;
    }

    public bool TryGetNpc(int networkId, out NpcEntity npc) => _npcSpawner.TryGet(networkId, out npc);

    public bool TryGetTemplate(int templateId, out NpcTemplate template) =>
        _templates.TryGetValue(templateId, out template);

    /// <summary>
    /// Spawns an NPC that belongs to no spawner - a pet (roadmap O). It has
    /// a network id from the same range and lives in the same registries, so
    /// visibility, interaction and despawn all work exactly as for a spawned
    /// NPC; the only difference is that nothing will ever replace it.
    ///
    /// Main thread only, like every other spawn.
    /// </summary>
    public NpcEntity SpawnStandalone(NpcTemplate template, Vector3 position)
    {
        int networkId = _nextId++;
        return _npcSpawner.SpawnFromTemplate(networkId, spawnerId: 0, template, position);
    }

    /// <summary>Removes a standalone NPC. Spawner-owned NPCs use ApplyDespawnSingle.</summary>
    public void DespawnStandalone(int networkId) => _npcSpawner.DespawnNpc(networkId);

    /// <summary>
    /// Hands out an id from the NPC range for any non-player entity. One
    /// counter for NPCs AND everything else, so ids can never collide and
    /// IsNpcId ("not a player") stays true for all of them.
    /// </summary>
    public int AllocateNetworkId() => _nextId++;

    /// <summary>Register another kind of grid entity (see IWorldEntitySource).</summary>
    public void RegisterEntitySource(IWorldEntitySource source) => _entitySources.Add(source);

    /// <summary>
    /// A non-player entity just came into peer's view: send whatever spawn
    /// packet it needs. Replaces the "TryGetNpc then W2CSpawnNpc" pair that
    /// used to be written out at each call site, so new entity kinds don't
    /// need those call sites edited again. Returns false for an id nobody
    /// owns any more (despawned since the grid update) - nothing is sent.
    /// </summary>
    public bool TrySendSpawnTo(ReplicationManager replication, NetPeer peer, int networkId)
    {
        if (_npcSpawner.TryGet(networkId, out var npc))
        {
            W2CSpawnNpcPacketSender.Send(replication, peer, npc);
            return true;
        }

        for (int i = 0; i < _entitySources.Count; i++)
        {
            if (_entitySources[i].TrySendSpawn(peer, networkId))
                return true;
        }

        return false;
    }

    public IEnumerable<NpcEntity> AllLiveNpcs => _npcSpawner.AllLive;

    public static bool IsNpcId(int networkId) => networkId >= NpcIdBase;

    private bool HasPlayerNearby(Vector3 position, int radiusCells)
    {
        _interest.GetNearbyAtPosition(position, radiusCells, _nearbyScratch);

        for (int i = 0; i < _nearbyScratch.Count; i++)
        {
            if (!IsNpcId(_nearbyScratch[i]))
                return true;
        }

        return false;
    }

    private int CellsForRadius(float radius)
    {
        float cellSize = _interest.CellSize;
        return Math.Max(1, (int)Math.Ceiling(radius / cellSize));
    }
}