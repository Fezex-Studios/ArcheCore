using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using ArcheCore.Server.World.Core.Entities;
using ArcheCore.Server.World.GameData.Loot;
using ArcheCore.Server.World.Networking.W2C;
using ArcheCore.Server.World.Utils.Database.SQLite;
using LiteNetLib;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Loot tables and corpses. Roadmap I.
    ///
    /// When an NPC dies, CombatManager asks for a corpse. Its loot is rolled
    /// ONCE, right then, from the NPC template's loot table - so what's in a
    /// corpse never changes, whoever opens it and however often. No drops
    /// (every roll missed, no gold range) means no corpse at all.
    ///
    /// Corpses live in the shared interest grid exactly like harvest nodes
    /// (this is an IWorldEntitySource): walk near one and it spawns on your
    /// client, walk away and W2CNpcDespawn removes it. Only the killer can
    /// loot; everyone else sees it.
    ///
    /// Two ways to loot, both the killer's only (F and G by default, from the
    /// InteractableActions table):
    ///
    ///   LootAll  - everything that FITS goes into the bag through
    ///              TryAddItem (the one add-item path) plus the gold;
    ///              whatever doesn't fit stays on the corpse.
    ///   OpenLoot - sends the loot window; each click there is a
    ///              C2WLootTake for one entry (or the gold).
    ///
    /// While a player has a corpse's window open, every change re-sends it,
    /// so the window never shows something that's already gone. An empty
    /// corpse, or one past its lifetime, disappears - which also closes the
    /// window on the client (it listens for the despawn).
    /// </summary>
    public class LootManager : IWorldEntitySource
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly Random Rng = new();

        /// <summary>How long an unlooted corpse stays in the world.</summary>
        private static readonly TimeSpan CorpseLifetime = TimeSpan.FromMinutes(2);

        private readonly IDbContextFactory<WorldDataDbContext> _dbFactory;
        private readonly ItemManager _items;
        private readonly PlayerManager _players;
        private readonly SpawnManager _spawnManager;
        private readonly InterestManager _interest;
        private readonly InteractionRegistry _interactions;
        private readonly ReplicationManager _replication;

        private sealed class Table
        {
            public LootTable Record;
            public LootTableEntry[] Entries;
        }

        private readonly Dictionary<int, Table> _tables = new();
        private readonly Dictionary<int, CorpseEntity> _corpses = new();
        private readonly List<int> _expiredScratch = new();
        private readonly List<NetPeer> _peerScratch = new();

        /// <summary>Player network id -> corpse whose loot window they have open.</summary>
        private readonly Dictionary<int, int> _openWindows = new();

        /// <summary>Same slack as shops/harvesting, re-checked on every single take.</summary>
        private const float RangeTolerance = 1.0f;

        public LootManager(
            IDbContextFactory<WorldDataDbContext> dbFactory,
            ItemManager items,
            PlayerManager players,
            SpawnManager spawnManager,
            InterestManager interest,
            InteractionRegistry interactions,
            ReplicationManager replication)
        {
            _dbFactory = dbFactory;
            _items = items;
            _players = players;
            _spawnManager = spawnManager;
            _interest = interest;
            _interactions = interactions;
            _replication = replication;
        }

        /// <summary>Call once at boot, after ItemManager has loaded.</summary>
        public void LoadFromDatabase()
        {
            using var db = _dbFactory.CreateDbContext();

            var entries = db.LootTableEntries.ToList().GroupBy(e => e.LootTableId).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var table in db.LootTables.ToList())
            {
                if (table.MinGold < 0 || table.MaxGold < table.MinGold)
                    Logger.Warn("[Loot] Table {Id} '{Name}': gold range {Min}-{Max} is invalid - gold disabled", table.Id, table.Name, table.MinGold, table.MaxGold);

                var valid = new List<LootTableEntry>();
                if (entries.TryGetValue(table.Id, out var rows))
                {
                    foreach (var e in rows)
                    {
                        string problem =
                            !_items.Exists(e.ItemId)         ? $"ItemId {e.ItemId} is not in Items" :
                            e.Chance <= 0 || e.Chance > 1     ? $"Chance {e.Chance} must be above 0 and at most 1" :
                            e.MinQuantity < 1                 ? "MinQuantity must be at least 1" :
                            e.MaxQuantity < e.MinQuantity     ? "MaxQuantity is below MinQuantity" :
                            null;

                        if (problem != null)
                        {
                            Logger.Warn("[Loot] Table {Table} entry {Id} skipped: {Problem}", table.Id, e.Id, problem);
                            continue;
                        }

                        valid.Add(e);
                    }
                }

                _tables[table.Id] = new Table { Record = table, Entries = valid.ToArray() };
            }

            _spawnManager.RegisterEntitySource(this);
            Logger.Info("[Loot] Loaded {Count} loot table(s).", _tables.Count);
        }

        // ── IWorldEntitySource ───────────────────────────────────────

        public bool TrySendSpawn(NetPeer peer, int networkId)
        {
            if (!_corpses.TryGetValue(networkId, out var corpse))
                return false;

            W2CSpawnCorpsePacketSender.Send(_replication, peer, corpse);
            return true;
        }

        public bool TryGetCorpse(int networkId, out CorpseEntity corpse) => _corpses.TryGetValue(networkId, out corpse);

        // ── Creating ─────────────────────────────────────────────────

        /// <summary>Roll the dead NPC's loot and, if anything dropped, leave a corpse for the killer.</summary>
        public void CreateCorpse(NpcEntity npc, int killerPlayerId)
        {
            if (npc.LootTableId == 0 || !_tables.TryGetValue(npc.LootTableId, out var table))
                return;

            string ownerName = string.Empty;
            if (_players.TryGetPeer(killerPlayerId, out var killerPeer) &&
                _players.TryGetSession(killerPeer, out var killer))
                ownerName = killer.Name ?? string.Empty;

            var corpse = new CorpseEntity
            {
                NetworkId = _spawnManager.AllocateNetworkId(),
                SourceTemplateId = npc.TemplateId,
                Name = npc.Name,
                Position = npc.Position,
                OwnerId = killerPlayerId,
                OwnerName = ownerName,
                ExpiresAtMs = Environment.TickCount64 + (long)CorpseLifetime.TotalMilliseconds,
            };

            var t = table.Record;
            if (t.MinGold >= 0 && t.MaxGold >= t.MinGold && t.MaxGold > 0)
                corpse.Gold = Rng.Next(t.MinGold, t.MaxGold + 1);

            foreach (var e in table.Entries)
            {
                if (Rng.NextDouble() < e.Chance)
                    corpse.Items.Add((e.ItemId, Rng.Next(e.MinQuantity, e.MaxQuantity + 1)));
            }

            if (corpse.IsEmpty)
                return; // unlucky - nothing to leave behind

            _corpses[corpse.NetworkId] = corpse;
            _interactions.Register(corpse.NetworkId, corpse);

            // Into the grid, and in front of everyone already nearby - a new
            // entry's "entered" list is exactly the players who can see it.
            var (entered, _) = _interest.UpdatePosition(corpse.NetworkId, corpse.Position);
            foreach (int id in entered)
            {
                if (SpawnManager.IsNpcId(id)) continue;
                if (_players.TryGetPeer(id, out var peer))
                    W2CSpawnCorpsePacketSender.Send(_replication, peer, corpse);
            }
        }

        // ── Looting ──────────────────────────────────────────────────

        /// <summary>OpenLoot: show this corpse's contents. Called by C2WInteractHandler after its range check.</summary>
        public void OpenWindow(NetPeer peer, int playerId, CorpseEntity corpse)
        {
            if (corpse.OwnerId != playerId)
            {
                W2CInteractDeniedPacketSender.Send(peer, "That isn't yours to loot.");
                return;
            }

            _openWindows[playerId] = corpse.NetworkId;
            W2CLootWindowPacketSender.Send(peer, corpse, ItemName);
        }

        /// <summary>
        /// C2WLootTake: one entry (itemTemplateId), or the gold (0). Arrives
        /// straight from the loot window, not through C2WInteractHandler, so
        /// it re-checks ownership and range itself.
        /// </summary>
        public void TryTakeOne(NetPeer peer, int corpseNetworkId, int itemTemplateId)
        {
            if (!_players.TryGetSession(peer, out var session) || session.NetworkId is not int playerId)
                return;

            if (!_corpses.TryGetValue(corpseNetworkId, out var corpse))
                return; // already gone - the despawn closed the window

            if (corpse.OwnerId != playerId)
            {
                W2CInteractDeniedPacketSender.Send(peer, "That isn't yours to loot.");
                return;
            }

            if (Vector3.Distance(session.Position, corpse.Position) > corpse.InteractRange + RangeTolerance)
            {
                W2CInteractDeniedPacketSender.Send(peer, "Too far away.");
                return;
            }

            string taken = null;

            if (itemTemplateId == 0)
            {
                if (corpse.Gold > 0 && _players.TryAddGold(peer, corpse.Gold))
                {
                    taken = $"{corpse.Gold}g";
                    corpse.Gold = 0;
                }
            }
            else
            {
                int index = corpse.Items.FindIndex(e => e.ItemId == itemTemplateId);
                if (index < 0)
                    return; // taken already (a double click)

                var (id, qty) = corpse.Items[index];
                if (_players.TryAddItem(peer, id, qty))
                {
                    taken = $"{qty}x {ItemName(id)}";
                    corpse.Items.RemoveAt(index);
                }
                else
                {
                    W2CInteractDeniedPacketSender.Send(peer, "Your inventory is full.");
                }
            }

            if (taken != null)
            {
                W2CInteractLootPacketSender.Send(peer, taken);
                Logger.Info("[Loot] Account {Account} took {What} from corpse {Corpse}", session.AccountId, taken, corpse.NetworkId);
            }

            AfterChange(peer, playerId, corpse);
        }

        /// <summary>LootAll: everything that fits. Called by C2WInteractHandler after its range check.</summary>
        public void TryLootAll(NetPeer peer, int playerId, CorpseEntity corpse)
        {
            if (corpse.OwnerId != playerId)
            {
                W2CInteractDeniedPacketSender.Send(peer, "That isn't yours to loot.");
                return;
            }

            if (!_players.TryGetSession(peer, out var session))
                return;

            var taken = new StringBuilder();

            if (corpse.Gold > 0 && _players.TryAddGold(peer, corpse.Gold))
            {
                Append(taken, $"{corpse.Gold}g");
                corpse.Gold = 0;
            }

            bool somethingDidntFit = false;
            for (int i = corpse.Items.Count - 1; i >= 0; i--)
            {
                var (itemId, qty) = corpse.Items[i];

                if (_players.TryAddItem(peer, itemId, qty))
                {
                    Append(taken, $"{qty}x {ItemName(itemId)}");
                    corpse.Items.RemoveAt(i);
                }
                else
                {
                    somethingDidntFit = true;
                }
            }

            if (taken.Length > 0)
            {
                W2CInteractLootPacketSender.Send(peer, taken.ToString());
                Logger.Info("[Loot] Account {Account} looted {What} from corpse {Corpse}", session.AccountId, taken, corpse.NetworkId);
            }

            if (somethingDidntFit)
                W2CInteractDeniedPacketSender.Send(peer, "Your inventory is full - the rest is still on the corpse.");

            AfterChange(peer, playerId, corpse);
        }

        /// <summary>Empty -> gone (closes the window). Otherwise refresh the window if it's open.</summary>
        private void AfterChange(NetPeer peer, int playerId, CorpseEntity corpse)
        {
            if (corpse.IsEmpty)
            {
                Despawn(corpse);
                return;
            }

            if (_openWindows.TryGetValue(playerId, out int open) && open == corpse.NetworkId)
                W2CLootWindowPacketSender.Send(peer, corpse, ItemName);
        }

        private string ItemName(int itemTemplateId) =>
            _items.GetById(itemTemplateId)?.name ?? $"#{itemTemplateId}";

        private static void Append(StringBuilder sb, string part)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(part);
        }

        // ── Lifetime ─────────────────────────────────────────────────

        /// <summary>Once per tick, from WorldServer's tick loop only.</summary>
        public void Tick()
        {
            if (_corpses.Count == 0)
                return;

            long now = Environment.TickCount64;
            _expiredScratch.Clear();

            foreach (var corpse in _corpses.Values)
                if (now >= corpse.ExpiresAtMs)
                    _expiredScratch.Add(corpse.NetworkId);

            foreach (int id in _expiredScratch)
                if (_corpses.TryGetValue(id, out var corpse))
                    Despawn(corpse);
        }

        private void Despawn(CorpseEntity corpse)
        {
            _peerScratch.Clear();
            foreach (int id in _interest.GetKnownBy(corpse.NetworkId))
            {
                if (SpawnManager.IsNpcId(id)) continue;
                if (_players.TryGetPeer(id, out var p)) _peerScratch.Add(p);
            }

            _interest.Remove(corpse.NetworkId);
            _interactions.Unregister(corpse.NetworkId);
            _corpses.Remove(corpse.NetworkId);

            // Forget any window pointing at it (the client closes on the despawn).
            foreach (var viewer in _openWindows.Where(kv => kv.Value == corpse.NetworkId).Select(kv => kv.Key).ToList())
                _openWindows.Remove(viewer);

            if (_peerScratch.Count > 0)
                W2CNpcDespawnPacketSender.Send(_replication, _peerScratch, corpse.NetworkId);
        }
    }
}
