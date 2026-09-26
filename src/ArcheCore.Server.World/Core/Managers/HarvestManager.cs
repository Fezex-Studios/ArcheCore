using System;
using ArcheCore.Server.World.Core.Interaction;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ArcheCore.Server.World.Core.Entities;
using ArcheCore.Server.World.GameData.Harvest;
using ArcheCore.Server.World.Networking.W2C;
using ArcheCore.Server.World.Utils.Database.SQLite;
using LiteNetLib;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Harvest nodes: loading them, keeping them visible, and the gather
    /// loop itself. Roadmap E.
    ///
    /// NODES LIVE IN THE SHARED INTEREST GRID, LIKE NPCS
    ///
    /// Every node gets an id from SpawnManager's NPC range and is placed
    /// in the same InterestManager as players and NPCs. That's what makes
    /// visibility free: a player walking near a node gets it in their
    /// "entered" list from their own movement update, and
    /// SpawnManager.TrySendSpawnTo asks this class (an IWorldEntitySource)
    /// to send the spawn packet. Walking away sends W2CNpcDespawn, which
    /// the client resolves against its node registry too.
    ///
    /// Nodes are static and cheap, so unlike NPC spawners there is no
    /// proximity activation: every node exists from boot. A depleted node
    /// stays in the world, flagged, until it respawns.
    ///
    /// THE HARVEST LOOP (all on the tick thread)
    ///
    ///   1. C2WInteract on a node -> TryBeginHarvest
    ///        - node available, not claimed by someone else
    ///        - room in the inventory for the MAXIMUM yield (checked up
    ///          front, so a full bag is refused before anything is claimed)
    ///        - node claimed for this player, timer starts, client shows bar
    ///   2. Tick
    ///        - player moved away from where they started   -> cancel
    ///        - player disconnected / node gone            -> cancel silently
    ///        - timer elapsed                              -> Complete
    ///   3. Complete
    ///        - roll quantity, TryAddItem (the ONE add-item path, roadmap C)
    ///        - if that somehow fails, cancel and leave the node available
    ///        - otherwise deplete, tell everyone who can see it, fire
    ///          OnHarvest to Lua, schedule respawn
    ///   4. Tick again: respawn any node whose timer is up
    ///
    /// Claiming at the START (not at completion) is what makes two players
    /// on one node well-defined: the second one is told "someone else is
    /// gathering that" instead of both finishing and one getting nothing.
    ///
    /// Depleted state is not persisted. A restart brings every node back,
    /// which is fine for respawn timers measured in seconds or minutes.
    /// </summary>
    public class HarvestManager : IWorldEntitySource, ArcheCore.Server.World.Core.Services.IInitializable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly Random Rng = new();

        /// <summary>
        /// How far a player may drift from where they started harvesting
        /// before it cancels. Not zero - movement packets jitter slightly
        /// even when standing still, and a harvest that cancels because the
        /// server heard a 2cm correction would feel broken.
        /// </summary>
        private const float MaxDriftWhileHarvesting = 0.75f;

        /// <summary>Slack on the range check, so standing right at the edge doesn't flicker between allowed and not.</summary>
        private const float RangeTolerance = 0.5f;

        private readonly IDbContextFactory<WorldDataDbContext> _dbFactory;
        private readonly InteractionRegistry _interactions;
        private readonly InterestManager _interest;
        private readonly SpawnManager _spawnManager;
        private readonly ItemManager _items;
        private readonly PlayerManager _players;
        private readonly ReplicationManager _replication;

        private readonly Dictionary<int, HarvestNodeEntity> _nodes = new();

        /// <summary>Player network id -> their in-progress harvest. At most one each.</summary>
        private readonly Dictionary<int, ActiveHarvest> _active = new();

        /// <summary>Nodes currently waiting to respawn - scanned instead of every node.</summary>
        private Scheduler _scheduler;

        // Scratch buffers, reused every tick (tick thread only).
        private readonly List<int> _finishedScratch = new();
        private readonly List<NetPeer> _peerScratch = new();

        private sealed class ActiveHarvest
        {
            public int PlayerId;
            public NetPeer Peer;
            public HarvestNodeEntity Node;
            public Vector3 StartPosition;
            public long CompleteAtMs;
            public Scheduler.Handle Completion;
        }

        /// <summary>Two-phase start-up: harvest completion and node respawns run on the shared Scheduler.</summary>
        public void Initialize(ArcheCore.Server.World.Core.Services.ServiceContainer services)
        {
            _scheduler = services.Get<Scheduler>();
            _actions = services.Get<InteractionActionCatalog>();
        }

        private InteractionActionCatalog _actions;

        public HarvestManager(
            IDbContextFactory<WorldDataDbContext> dbFactory,
            InteractionRegistry interactions,
            InterestManager interest,
            SpawnManager spawnManager,
            ItemManager items,
            PlayerManager players,
            ReplicationManager replication)
        {
            _dbFactory = dbFactory;
            _interactions = interactions;
            _interest = interest;
            _spawnManager = spawnManager;
            _items = items;
            _players = players;
            _replication = replication;
        }

        // ── Boot ─────────────────────────────────────────────────────

        /// <summary>
        /// Loads templates and spawns, validates them, and places every node
        /// in the world. Call once at boot, AFTER ItemManager has loaded and
        /// BEFORE the tick loop starts (the interest grid is tick-thread
        /// only; before the loop starts, the boot thread is the only one).
        /// </summary>
        public void LoadAndSpawnAll()
        {
            using var db = _dbFactory.CreateDbContext();

            var templates = new Dictionary<int, HarvestNodeTemplate>();
            foreach (var t in db.HarvestNodeTemplates.ToList())
            {
                if (!IsValid(t))
                    continue;

                templates[t.Id] = t;
            }

            int spawned = 0;
            foreach (var spawn in db.HarvestNodeSpawns.ToList())
            {
                if (!templates.TryGetValue(spawn.TemplateId, out var template))
                {
                    Logger.Warn("[Harvest] Spawn {Id} uses missing or invalid template {TemplateId} - skipped", spawn.Id, spawn.TemplateId);
                    continue;
                }

                var node = new HarvestNodeEntity
                {
                    NetworkId = _spawnManager.AllocateNetworkId(),
                    SpawnId   = spawn.Id,
                    Template  = template,
                    Position  = new Vector3(spawn.X, spawn.Y, spawn.Z),
                    Yaw       = spawn.Yaw,
                };

                _nodes[node.NetworkId] = node;
                _interactions.Register(node.NetworkId, node);

                // Into the shared grid. Nothing to tell anyone yet - no
                // players exist at boot. Players discover it from their
                // own movement from here on.
                _interest.UpdatePosition(node.NetworkId, node.Position);
                spawned++;
            }

            _spawnManager.RegisterEntitySource(this);

            Logger.Info("[Harvest] Loaded {Templates} node template(s), spawned {Spawned} node(s).", templates.Count, spawned);
        }

        /// <summary>A bad row is a data bug, not a crash: warn loudly at boot and skip it.</summary>
        private bool IsValid(HarvestNodeTemplate t)
        {
            string problem =
                !_items.Exists(t.ItemId)                 ? $"ItemId {t.ItemId} is not in Items" :
                t.MinQuantity < 1                        ? "MinQuantity must be at least 1" :
                t.MaxQuantity < t.MinQuantity            ? "MaxQuantity is below MinQuantity" :
                t.HarvestTimeMs < 0                      ? "HarvestTimeMs is negative" :
                t.RespawnSeconds < 0                     ? "RespawnSeconds is negative" :
                t.InteractRange <= 0                     ? "InteractRange must be positive" :
                string.IsNullOrWhiteSpace(t.ModelType)   ? "ModelType is empty" :
                null;

            if (problem == null)
                return true;

            Logger.Warn("[Harvest] Template {Id} '{Name}' skipped: {Problem}", t.Id, t.Name, problem);
            return false;
        }

        // ── IWorldEntitySource ───────────────────────────────────────

        public bool TrySendSpawn(NetPeer peer, int networkId)
        {
            if (!_nodes.TryGetValue(networkId, out var node))
                return false;

            W2CSpawnHarvestNodePacketSender.Send(_replication, peer, node, _actions.For(InteractableKind.HarvestNode, node.TemplateId));
            return true;
        }

        public bool TryGetNode(int networkId, out HarvestNodeEntity node) =>
            _nodes.TryGetValue(networkId, out node);

        // ── Starting ─────────────────────────────────────────────────

        /// <summary>
        /// Called by C2WInteractHandler once it has already confirmed the
        /// target exists and the player is in range.
        /// </summary>
        public void TryBeginHarvest(NetPeer peer, int playerId, HarvestNodeEntity node)
        {
            // Pressing interact on the node you're already harvesting is a
            // no-op, not a restart - otherwise mashing F resets the bar.
            if (_active.TryGetValue(playerId, out var existing))
            {
                if (existing.Node == node)
                    return;

                Cancel(existing, "You stop gathering.");
            }

            if (!_players.TryGetSession(peer, out var session) || session.NetworkId == null)
                return;

            if (session.Combat.IsDead)
            {
                W2CInteractDeniedPacketSender.Send(peer, "You can't do that while dead.");
                return;
            }

            if (node.IsDepleted)
            {
                W2CInteractDeniedPacketSender.Send(peer, $"The {node.Template.Name} has nothing left to gather.");
                return;
            }

            if (node.ClaimedBy is int other && other != playerId)
            {
                W2CInteractDeniedPacketSender.Send(peer, "Someone else is gathering that.");
                return;
            }

            // Room for the BEST roll, checked before claiming anything. If
            // it only fits the minimum, refusing now is kinder than making
            // the player wait three seconds to be told their bag is full.
            if (!_players.CanAddItem(peer, node.Template.ItemId, node.Template.MaxQuantity))
            {
                W2CInteractDeniedPacketSender.Send(peer, "Your inventory is full.");
                return;
            }

            // Gathering happens on foot.
            _players.Mounts?.Dismount(peer, session, "You dismount to gather.");

            node.ClaimedBy = playerId;

            var harvest = new ActiveHarvest
            {
                PlayerId      = playerId,
                Peer          = peer,
                Node          = node,
                StartPosition = session.Position,
                CompleteAtMs  = ArcheCore.Server.World.ServerClock.NowMs + node.Template.HarvestTimeMs,
            };

            _active[playerId] = harvest;

            // Completion is a Scheduler callback. The per-tick check below
            // only watches for walking away or disconnecting.
            harvest.Completion = _scheduler.In(node.Template.HarvestTimeMs, () => Finish(harvest), "harvest");

            W2CHarvestStartedPacketSender.Send(peer, node.NetworkId, node.Template.Name, node.Template.HarvestTimeMs);
        }

        /// <summary>Stop a player's harvest, if they have one (e.g. they started interacting with something else).</summary>
        public void CancelFor(int playerId, string reason)
        {
            if (_active.TryGetValue(playerId, out var harvest))
                Cancel(harvest, reason);
        }

        public bool IsInRange(Vector3 playerPosition, HarvestNodeEntity node) =>
            Vector3.Distance(playerPosition, node.Position) <= node.InteractRange + RangeTolerance;

        // ── Tick ─────────────────────────────────────────────────────

        /// <summary>Once per tick, from WorldServer's tick loop only.</summary>
        public void Tick()
        {
            long now = ArcheCore.Server.World.ServerClock.NowMs;

            if (_active.Count > 0)
                TickHarvests(now);
        }

        private void TickHarvests(long now)
        {
            _finishedScratch.Clear();

            foreach (var harvest in _active.Values)
            {
                // Gone: disconnected, or logged out to character select.
                if (!_players.TryGetSession(harvest.Peer, out var session) ||
                    session.NetworkId != harvest.PlayerId)
                {
                    _finishedScratch.Add(harvest.PlayerId);
                    continue;
                }

                // Moved: walking away cancels, like every MMO gather.
                if (Vector3.Distance(session.Position, harvest.StartPosition) > MaxDriftWhileHarvesting)
                {
                    _finishedScratch.Add(harvest.PlayerId);
                    continue;
                }

            }

            // Resolve outside the loop - Complete/Cancel remove from _active.
            foreach (int playerId in _finishedScratch)
            {
                if (!_active.TryGetValue(playerId, out var harvest))
                    continue;

                bool connected = _players.TryGetSession(harvest.Peer, out var session) &&
                                 session.NetworkId == harvest.PlayerId;

                if (!connected)
                    Release(harvest);                       // nobody to tell
                else if (Vector3.Distance(session.Position, harvest.StartPosition) > MaxDriftWhileHarvesting)
                    Cancel(harvest, "Interrupted.");
                else
                    Complete(harvest);
            }
        }

        /// <summary>Scheduler callback: the harvest's time is up.</summary>
        private void Finish(ActiveHarvest harvest)
        {
            // Superseded (cancelled, or a new harvest replaced it).
            if (!_active.TryGetValue(harvest.PlayerId, out var current) || !ReferenceEquals(current, harvest))
                return;

            bool connected = _players.TryGetSession(harvest.Peer, out var session) &&
                             session.NetworkId == harvest.PlayerId;

            if (!connected)
                Release(harvest);
            else if (Vector3.Distance(session.Position, harvest.StartPosition) > MaxDriftWhileHarvesting)
                Cancel(harvest, "Interrupted.");
            else
                Complete(harvest);
        }

        private void Complete(ActiveHarvest harvest)
        {
            var node = harvest.Node;
            var template = node.Template;

            int quantity = Rng.Next(template.MinQuantity, template.MaxQuantity + 1);

            // THE add-item path. If the bag filled up during the harvest
            // (another source added items), the node is NOT consumed.
            if (!_players.TryAddItem(harvest.Peer, template.ItemId, quantity))
            {
                Cancel(harvest, "Your inventory is full.");
                return;
            }

            Release(harvest);

            string itemName = _items.GetById(template.ItemId)?.name ?? $"#{template.ItemId}";
            W2CHarvestCompletedPacketSender.Send(harvest.Peer, node.NetworkId, template.ItemId, quantity, itemName);

            Deplete(node);

            _players.FireHarvestEvent(harvest.Peer, template.Id, template.ItemId, quantity);

            Logger.Info("[Harvest] Player {Player} gathered {Qty}x item {Item} from node {Node} ({Name})",
                harvest.PlayerId, quantity, template.ItemId, node.NetworkId, template.Name);
        }

        private void Cancel(ActiveHarvest harvest, string reason)
        {
            Release(harvest);
            W2CHarvestCancelledPacketSender.Send(harvest.Peer, harvest.Node.NetworkId, reason);
        }

        /// <summary>Forget the harvest and free the node's claim. Sends nothing.</summary>
        private void Release(ActiveHarvest harvest)
        {
            _scheduler.Cancel(harvest.Completion);

            if (_active.TryGetValue(harvest.PlayerId, out var current) && ReferenceEquals(current, harvest))
                _active.Remove(harvest.PlayerId);

            if (harvest.Node.ClaimedBy == harvest.PlayerId)
                harvest.Node.ClaimedBy = null;
        }

        // ── Depletion / respawn ──────────────────────────────────────

        private void Deplete(HarvestNodeEntity node)
        {
            // RespawnSeconds = 0 means "infinite node" - handy for testing.
            if (node.Template.RespawnSeconds <= 0)
                return;

            node.IsDepleted = true;
            node.RespawnAtMs = ArcheCore.Server.World.ServerClock.NowMs + node.Template.RespawnSeconds * 1000L;

            _scheduler.In(node.Template.RespawnSeconds * 1000L, () =>
            {
                node.IsDepleted = false;
                BroadcastState(node);
            }, "node respawn");

            BroadcastState(node);
        }

        /// <summary>Tell every player who can currently see this node its new state.</summary>
        private void BroadcastState(HarvestNodeEntity node)
        {
            _peerScratch.Clear();

            foreach (int id in _interest.GetKnownBy(node.NetworkId))
            {
                if (SpawnManager.IsNpcId(id))
                    continue; // other nodes / NPCs share the grid - they aren't listening

                if (_players.TryGetPeer(id, out var peer))
                    _peerScratch.Add(peer);
            }

            if (_peerScratch.Count > 0)
                W2CHarvestNodeStatePacketSender.Send(_replication, _peerScratch, node.NetworkId, node.IsDepleted);
        }
    }
}
