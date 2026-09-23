using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Server.World.Core.Interaction;
using ArcheCore.Server.World.GameData.Items;
using ArcheCore.Server.World.Lua.Scripting;
using ArcheCore.Server.World.Lua.Scripting.Bindings;
using ArcheCore.Server.World.Networking.W2C;
using ArcheCore.Server.World.Replication;
using ArcheCore.Server.World.Utils.Config;
using LiteNetLib;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.Managers
{
    public class PlayerManager
    {
        private readonly ConcurrentQueue<Action> _pendingActions = new();
        private readonly LuaEngine _luaEngine = new();
        private readonly ReplicationManager _replication;

        private readonly SessionManager _sessions;
        private readonly InterestManager _interest;
        private readonly PlayerMovementBroadcaster _movement;
        private readonly PlayerSpawnManager _spawn;
        private readonly CharacterPersistence _persistence;
        private readonly AutosaveScheduler _autosave;
        private readonly SnapshotDispatcher _snapshots;
        private readonly TickClock _clock;
        private readonly MovementValidator _validator;
        private readonly JumpEventBroadcaster _jumps;
        private readonly ItemManager _items;

        public InterestManager Interest => _interest;
        public JumpEventBroadcaster Jumps => _jumps;
        public SnapshotDispatcher Snapshots => _snapshots;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static string Lua =>
            System.IO.Path.Combine(AppContext.BaseDirectory, "Lua", "Server");

        public PlayerManager(
            SpawnManager spawnManager,
            ReplicationManager replication,
            WorldServerConfig worldConfig,
            PersistenceClient persistence,
            DemoManager demoManager,
            InterestManager interest,
            ItemManager items)
        {
            _replication = replication;
            _items = items;
            _interest = interest;

            _sessions = new SessionManager();
            _persistence = new CharacterPersistence(persistence, EnqueueAction);
            _autosave = new AutosaveScheduler(
                _sessions, _persistence, worldConfig.AutosaveIntervalSeconds, worldConfig.TickRate);
            _clock = new TickClock();

            _snapshots = new SnapshotDispatcher(
                _sessions, _interest, (ushort)ArcheCore.Library.Net.Worldserver.Opcodes.W2CWorldSnapshot);

            _validator = new MovementValidator(_replication);
            _jumps = new JumpEventBroadcaster(_sessions, _interest, _replication, _clock);

            _movement = new PlayerMovementBroadcaster(
                _sessions, _interest, _replication, spawnManager, _snapshots, _clock);

            _spawn = new PlayerSpawnManager(
                _sessions, _persistence, _replication, _interest,
                _luaEngine, worldConfig, demoManager, spawnManager,
                _snapshots, _clock, _jumps);
        }

        public void InitializeScripts() => _luaEngine.LoadAllScripts(Lua);
        public void EnqueueAction(Action action) => _pendingActions.Enqueue(action);

        public void DrainActions()
        {
            while (_pendingActions.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { Logger.Error(ex); }
            }
        }

        public void AdvanceTick(uint tick) => _clock.Advance(tick);
        public void FlushSnapshots(uint tick) => _snapshots.Flush(tick);
        public void RunAutosave(uint tick) => _autosave.Tick(tick);

        /// <summary>
        /// Shutdown only - call AFTER the tick loop has stopped. Saves
        /// every in-world dirty character and waits for all of them.
        ///
        /// Unlike the normal background save path, this assumes success
        /// once the send completes without throwing (checked below via
        /// baseOk/inventoryOk, logged but not retried) - there IS no next
        /// autosave to retry on at shutdown, so a failed save here can
        /// only be reported, not recovered. That's why the ordinary path
        /// in CharacterPersistence is careful about NOT marking inventory
        /// saved early and this one doesn't need to be: there's nothing
        /// left to protect once the process is exiting anyway.
        /// </summary>
        public async Task SaveAllAsync()
        {
            var saves = new List<Task<(bool baseOk, bool inventoryOk)>>();
            var descriptions = new List<string>();

            foreach (var peer in _sessions.GetAllConnectedPeers())
            {
                if (peer.Tag is not PlayerSession { NetworkId: not null } s || !s.IsDirty)
                    continue;

                InventorySlotDto[] diff = s.InventoryDirty
                    ? CharacterPersistence.ComputeInventoryDiff(s)
                    : Array.Empty<InventorySlotDto>();

                saves.Add(_persistence.SaveAsync(s.CharacterId, s.AccountId, s.Name, s.Level, s.Position, s.Gold, diff));
                descriptions.Add($"CharacterId={s.CharacterId}");

                s.MarkSaved();
            }

            if (saves.Count == 0) return;

            Logger.Info($"[Shutdown] Saving {saves.Count} character(s)...");
            var results = await Task.WhenAll(saves);

            int failed = 0;
            for (int i = 0; i < results.Length; i++)
            {
                var (baseOk, inventoryOk) = results[i];
                if (!baseOk || !inventoryOk)
                {
                    failed++;
                    Logger.Error($"[Shutdown] {descriptions[i]} FAILED (base={baseOk}, inventory={inventoryOk}).");
                }
            }

            if (failed == 0) Logger.Info("[Shutdown] All characters saved.");
            else             Logger.Error($"[Shutdown] {failed} of {results.Length} character save(s) FAILED.");
        }

        public bool TryGetSession(NetPeer peer, out PlayerSession session) =>
            _sessions.TryGetSession(peer, out session);
        public bool TryGetNetworkId(NetPeer peer, out int networkId) =>
            _sessions.TryGetNetworkId(peer, out networkId);
        public bool TryGetPeer(int networkId, out NetPeer peer) =>
            _sessions.TryGetPeer(networkId, out peer);
        public bool TryGetPeerByName(string name, out NetPeer peer) =>
            _sessions.TryGetPeerByName(name, out peer);
        public IEnumerable<NetPeer> GetAllConnectedPeers() =>
            _sessions.GetAllConnectedPeers();
        public void TrackPendingSelection(NetPeer peer, int accountId) =>
            _sessions.TrackPendingSelection(peer, accountId);
        public int? GetPendingAccountId(NetPeer peer) =>
            _sessions.GetPendingAccountId(peer);
        public bool TryBeginSpawn(NetPeer peer) =>
            _sessions.TryBeginSpawn(peer);
        public int GetLevel(NetPeer peer) => _sessions.GetLevel(peer);
        public long GetCharacterId(NetPeer peer) => _sessions.GetCharacterId(peer);
        public string GetName(NetPeer peer) => _sessions.GetName(peer);

        public void HandlePlayerConnected(
            NetPeer peer, int accountId, P2WCharacterLoadResponse character,
            bool isNewCharacter = false) =>
            _spawn.HandlePlayerConnected(peer, accountId, character, isNewCharacter);

        public void HandlePlayerDisconnected(NetPeer peer) =>
            _spawn.HandlePlayerDisconnected(peer);

        public bool TryGetPosition(int networkId, out Vector3 position) =>
            _movement.TryGetPosition(networkId, out position);

        public void BroadcastPosition(
            NetPeer sender, int networkId, Vector3 position,
            Vector3 velocity = default, float yaw = 0f,
            float pitch = 0f, float roll = 0f, byte state = 0) =>
            _movement.BroadcastPosition(sender, networkId, position, velocity, yaw, pitch, roll, state);

        /// <summary>Dead players don't move. The client locks input too; this is the authority.</summary>
        public bool TryAcceptMovementChecked(NetPeer peer, PlayerSession session, Vector3 position, Vector3 velocity) =>
            !session.IsDead && TryAcceptMovement(peer, session, position, velocity);

        public bool TryAcceptMovement(NetPeer peer, PlayerSession session, Vector3 position, Vector3 velocity) =>
            _validator.Validate(peer, session, position, velocity) == MovementValidator.Result.Accepted;

        public void NotifyAuthoritativeMove(PlayerSession session, Vector3 position) =>
            _validator.NotifyAuthoritativeMove(session, position);

        /// <summary>
        /// Puts a PLAYER somewhere authoritatively (respawn). The snapshot
        /// store is what other clients read, so this is what makes everyone
        /// else see the move.
        /// </summary>
        public void SetPlayerTransform(int networkId, Vector3 position) =>
            _snapshots.SetTransform(
                networkId, position, velocity: Vector3.Zero, yaw: 0f, pitch: 0f, roll: 0f, state: 0,
                isNpc: false, _clock.Current);

        /// <summary>Is this player connected, spawned and not dead? Used by NPC aggro.</summary>
        public bool IsAlive(int networkId) =>
            TryGetPeer(networkId, out var peer) &&
            TryGetSession(peer, out var session) &&
            session.NetworkId == networkId &&
            !session.IsDead;

        /// <summary>PlayerEvent.OnDeath(player, killerNpcTemplateId).</summary>
        public void FireDeathEvent(NetPeer peer, int killerTemplateId)
        {
            var player = CreateLuaPlayer(peer);
            if (player == null)
                return;

            _luaEngine.FireEvent(PlayerEvent.OnDeath, player, killerTemplateId);
        }

        public void SetNpcTransform(
            int networkId, Vector3 position, Vector3 velocity, float yaw, byte state) =>
            _snapshots.SetTransform(
                networkId, position, velocity, yaw, pitch: 0f, roll: 0f, state,
                isNpc: true, _clock.Current);

        public void RemoveReplicatedEntity(int networkId)
        {
            _snapshots.Remove(networkId);
            _jumps.Remove(networkId);
        }

        /// <summary>
        /// The one script engine. Exposed so server systems can subscribe to
        /// LuaEngine.EventFired (QuestManager does) instead of a second event
        /// system being invented beside it.
        /// </summary>
        public LuaEngine LuaEngine => _luaEngine;

        public LuaPlayer CreateLuaPlayer(NetPeer peer)
        {
            if (!_sessions.TryGetSession(peer, out var session) || session.NetworkId is not int networkId)
                return null;
            return new LuaPlayer(peer, networkId, session.AccountId, _replication);
        }

        public void FireInteractEvent(LuaPlayer player, IInteractable target)
        {
            _luaEngine.FireEvent(PlayerEvent.OnInteract, player, target.TemplateId, (int)target.Kind);
        }

        /// <summary>
        /// PlayerEvent.OnKill(player, npcTemplateId), after the NPC has died.
        /// The "kill" hook roadmap L's quest objectives will listen to.
        /// </summary>
        public void FireKillEvent(NetPeer peer, int npcTemplateId)
        {
            var player = CreateLuaPlayer(peer);
            if (player == null)
                return;

            _luaEngine.FireEvent(PlayerEvent.OnKill, player, npcTemplateId);
        }

        /// <summary>
        /// PlayerEvent.OnHarvest(player, nodeTemplateId, itemTemplateId, quantity).
        /// Fired after the item is already in the inventory. This is the
        /// "collect" hook roadmap L's quest wiring will listen to.
        /// </summary>
        public void FireHarvestEvent(NetPeer peer, int nodeTemplateId, int itemTemplateId, int quantity)
        {
            var player = CreateLuaPlayer(peer);
            if (player == null)
                return;

            _luaEngine.FireEvent(PlayerEvent.OnHarvest, player, nodeTemplateId, itemTemplateId, quantity);
        }

        public int LevelUp(NetPeer peer)
        {
            if (!TryGetSession(peer, out var session) || session.NetworkId == null) return -1;
            session.Level += 1;

            // A level-up raises max health and refills it, MMO-style.
            session.MaxHealth = ArcheCore.Server.World.Core.Combat.HealthRules.PlayerMaxHealth(session.Level);
            session.Health = session.MaxHealth;
            W2CHealthUpdatePacketSender.Send(peer, session.Health, session.MaxHealth);
            _persistence.SaveInBackground(session);
            return session.Level;
        }

        // --- Currency ---

        /// <summary>The ONLY place gold changes. Unchanged from the
        /// currency pass - see PlayerSession.Gold's doc comment.</summary>
        public bool TryAddGold(NetPeer peer, int delta)
        {
            if (!TryGetSession(peer, out var session) || session.NetworkId == null)
                return false;

            // long, so neither direction can wrap: a big sale can't overflow
            // to negative, and a big purchase can't underflow to positive.
            long result = (long)session.Gold + delta;
            if (result < 0 || result > int.MaxValue)
                return false;

            session.Gold = (int)result;
            W2CGoldUpdatePacketSender.Send(peer, session.Gold);
            return true;
        }

        // --- Inventory ---
        //
        // Same authority rule as gold: TryAddItem and TryMoveItem are the
        // ONLY place session.Inventory is ever written. Both set
        // InventoryDirty = true on any real change - that's the one line
        // that's new compared to the in-memory pass; everything about the
        // move/merge/swap logic itself is unchanged, because persistence
        // is a concern of WHEN and WHAT gets saved, not of how a swap is
        // decided.

        /// <summary>
        /// THE one function that hands a player an item. Harvesting, loot,
        /// shop purchases and quest rewards all end up here.
        ///
        /// Adds to an existing stack of the same item if one exists,
        /// otherwise uses the first empty slot. Returns false if the item
        /// id isn't real, or the inventory is full - nothing changes.
        ///
        /// The Exists check matters most for callers passing ids from DATA
        /// (harvest node templates, loot tables): a typo there would
        /// otherwise put a ghost item into someone's saved inventory that
        /// no lookup can ever resolve.
        /// </summary>
        public bool TryAddItem(NetPeer peer, int itemTemplateId, int quantity)
        {
            if (!TryGetSession(peer, out var session) || session.NetworkId == null)
                return false;

            if (itemTemplateId <= 0 || quantity <= 0)
                return false;

            if (!_items.Exists(itemTemplateId))
            {
                Logger.Warn($"[AddItem] Unknown item id {itemTemplateId} for account {session.AccountId} - rejected");
                return false;
            }

            var inventory = session.Inventory;

            for (int i = 0; i < inventory.Length; i++)
            {
                if (inventory[i].ItemTemplateId != itemTemplateId)
                    continue;

                // No MaxStack yet, but never let a stack wrap to negative.
                // An overflowing stack falls through to the empty-slot pass.
                if ((long)inventory[i].Quantity + quantity > int.MaxValue)
                    continue;

                inventory[i].Quantity += quantity;
                session.InventoryDirty = true;
                W2CInventorySlotChangedPacketSender.Send(peer, i, inventory[i]);
                return true;
            }

            for (int i = 0; i < inventory.Length; i++)
            {
                if (inventory[i].ItemTemplateId == 0)
                {
                    inventory[i] = new InventorySlot { ItemTemplateId = itemTemplateId, Quantity = quantity };
                    session.InventoryDirty = true;
                    W2CInventorySlotChangedPacketSender.Send(peer, i, inventory[i]);
                    return true;
                }
            }

            return false; // full
        }

        /// <summary>
        /// Would TryAddItem(itemTemplateId, quantity) succeed right now?
        /// Same stacking rules, changes nothing. Shops and harvesting check
        /// this BEFORE taking gold or claiming a node, so a full inventory
        /// is refused up front instead of half-completing a transaction.
        /// </summary>
        public bool CanAddItem(NetPeer peer, int itemTemplateId, int quantity)
        {
            if (!TryGetSession(peer, out var session) || session.NetworkId == null)
                return false;

            if (itemTemplateId <= 0 || quantity <= 0 || !_items.Exists(itemTemplateId))
                return false;

            foreach (var slot in session.Inventory)
            {
                if (slot.ItemTemplateId == itemTemplateId && (long)slot.Quantity + quantity <= int.MaxValue)
                    return true;
            }

            foreach (var slot in session.Inventory)
            {
                if (slot.ItemTemplateId == 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Takes up to quantity items out of one slot (quantity &lt;= 0 means
        /// the whole stack) and reports what was taken. The public face of
        /// the same removal code drop and consume use, for callers that pay
        /// for what they remove - selling to a shop.
        /// </summary>
        public bool TryTakeFromSlot(NetPeer peer, int slot, int quantity, out int itemTemplateId, out int removed)
        {
            itemTemplateId = 0;
            removed = 0;

            if (!TryGetSession(peer, out var session) || session.NetworkId == null)
                return false;

            return TryRemoveFromSlot(session, peer, slot, quantity, out itemTemplateId, out removed);
        }

        /// <summary>What's in a slot, without changing it. False for a bad index.</summary>
        public bool TryPeekSlot(NetPeer peer, int slot, out InventorySlot contents)
        {
            contents = default;

            if (!TryGetSession(peer, out var session) || session.NetworkId == null)
                return false;

            if (slot < 0 || slot >= session.Inventory.Length)
                return false;

            contents = session.Inventory[slot];
            return true;
        }

        /// <summary>
        /// Move, merge or swap two slots, depending on what's in each.
        /// Same slot twice is a harmless no-op (returns true). Out-of-
        /// range indices return false.
        /// </summary>
        public bool TryMoveItem(NetPeer peer, int fromSlot, int toSlot)
        {
            if (!TryGetSession(peer, out var session) || session.NetworkId == null)
                return false;

            var inventory = session.Inventory;

            if (fromSlot < 0 || fromSlot >= inventory.Length ||
                toSlot   < 0 || toSlot   >= inventory.Length)
                return false;

            if (fromSlot == toSlot)
                return true; // no-op, not an error, not dirty

            var from = inventory[fromSlot];
            var to   = inventory[toSlot];

            if (from.ItemTemplateId == 0)
                return false; // nothing to move

            if (to.ItemTemplateId == 0)
            {
                inventory[toSlot]   = from;
                inventory[fromSlot] = default;
            }
            else if (to.ItemTemplateId == from.ItemTemplateId)
            {
                // Merge stacks. No max-stack-size cap in this pass - add
                // one when ItemStats gains a StackLimit field, and split
                // the excess back into fromSlot instead of dropping it.
                inventory[toSlot].Quantity += from.Quantity;
                inventory[fromSlot] = default;
            }
            else
            {
                inventory[fromSlot] = to;
                inventory[toSlot]   = from;
            }

            session.InventoryDirty = true;
            W2CInventorySlotChangedPacketSender.Send(peer, fromSlot, inventory[fromSlot]);
            W2CInventorySlotChangedPacketSender.Send(peer, toSlot,   inventory[toSlot]);
            return true;
        }

        /// <summary>
        /// Destroys all or part of one slot. quantity &lt;= 0, or &gt;= the
        /// stack size, clears the whole slot; anything in between removes
        /// that many and leaves the rest.
        ///
        /// Destroys, not drops-to-ground: there is no world item object
        /// yet. When loot (roadmap I) adds a ground/corpse IInteractable,
        /// this is the one place that changes - spawn it here with the
        /// removed stack instead of discarding it.
        ///
        /// The "are you sure?" dialog is client-side only. By the time this
        /// runs the player has confirmed; the server just validates.
        ///
        /// Logged at Info because it's irreversible, and "my item vanished"
        /// is a support question you'll want to answer from logs.
        /// </summary>
        public bool TryDropItem(NetPeer peer, int slot, int quantity)
        {
            if (!TryGetSession(peer, out var session) || session.NetworkId == null)
                return false;

            if (!TryRemoveFromSlot(session, peer, slot, quantity, out int itemTemplateId, out int removed))
                return false;

            Logger.Info(
                $"[DropItem] Account {session.AccountId} destroyed {removed}x item {itemTemplateId} from slot {slot}");
            return true;
        }

        /// <summary>
        /// Uses the item in a slot. What "use" means comes from its ItemUse
        /// row - never from the client. Order matters, and is deliberate:
        ///
        ///   1. Slot is valid and not empty
        ///   2. Item has an ItemUse row          (no row  -> not usable)
        ///   3. Its cooldown group has expired   (running -> reject)
        ///   4. Apply the effect                 (fails   -> reject, NOT consumed)
        ///   5. Consume one, if ConsumeOnUse
        ///   6. Start the cooldown, tell the client
        ///   7. Fire PlayerEvent.OnItemUse to Lua for anything custom
        ///
        /// Consuming only AFTER the effect succeeds is what stops "drank a
        /// potion at full HP and lost it", and cooldown-before-effect is
        /// what stops spam-clicking a potion to drink five in one tick.
        /// </summary>
        public bool TryUseItem(NetPeer peer, int slot)
        {
            if (!TryGetSession(peer, out var session) || session.NetworkId == null)
                return false;

            if (session.IsDead)
            {
                W2CInteractDeniedPacketSender.Send(peer, "You can't do that while dead.");
                return false;
            }

            var inventory = session.Inventory;

            // 1
            if (slot < 0 || slot >= inventory.Length)
                return false;

            int itemTemplateId = inventory[slot].ItemTemplateId;
            if (itemTemplateId == 0)
                return false;

            // 2
            var use = _items.GetUse(itemTemplateId);
            if (use == null)
                return false;

            // 3
            long now = Environment.TickCount64;
            int cooldownKey = ItemManager.CooldownKey(use);

            if (session.ItemCooldowns.TryGetValue(cooldownKey, out long readyAt) && now < readyAt)
                return false;

            // 4
            var player = CreateLuaPlayer(peer);
            if (player == null)
                return false;

            if (!TryApplyItemEffect(peer, session, use))
                return false;

            // 5
            if (use.ConsumeOnUse)
                TryRemoveFromSlot(session, peer, slot, 1, out _, out _);

            // 6
            if (use.CooldownMs > 0)
            {
                session.ItemCooldowns[cooldownKey] = now + use.CooldownMs;
                W2CItemCooldownPacketSender.Send(
                    peer, cooldownKey, _items.ItemsSharingCooldown(use), use.CooldownMs);
            }

            // 7
            _luaEngine.FireEvent(PlayerEvent.OnItemUse, player, itemTemplateId, slot);
            return true;
        }

        /// <summary>
        /// Applies an ItemUse's built-in effect. Returns false to reject the
        /// use without consuming the item or starting its cooldown.
        ///
        /// Heal / ApplyBuff / CastSkill are STUBS that log and succeed, so
        /// the full consume + cooldown pipeline can be tested today. Each
        /// becomes real when its system exists:
        ///   Heal      - roadmap G. Return false at full HP if you want
        ///               potions to be un-drinkable when they'd be wasted.
        ///   ApplyBuff - with the buff system.
        ///   CastSkill - roadmap H.
        /// </summary>
        private bool TryApplyItemEffect(NetPeer peer, PlayerSession session, ItemUse use)
        {
            switch (use.EffectType)
            {
                case ItemEffectType.ScriptOnly:
                    return true; // Lua does the work in OnItemUse

                case ItemEffectType.Heal:
                    // Refused at full health, so a potion is never wasted -
                    // returning false here means it isn't consumed and its
                    // cooldown doesn't start.
                    if (session.IsDead)
                        return false;

                    if (session.Health >= session.MaxHealth)
                    {
                        W2CInteractDeniedPacketSender.Send(peer, "You are already at full health.");
                        return false;
                    }

                    session.Health = System.Math.Min(session.MaxHealth, session.Health + System.Math.Max(0, use.EffectValue));
                    W2CHealthUpdatePacketSender.Send(peer, session.Health, session.MaxHealth);
                    return true;

                case ItemEffectType.ApplyBuff:
                    Logger.Info($"[UseItem] STUB ApplyBuff {use.EffectValue} for account {session.AccountId} - no buff system yet");
                    return true;

                case ItemEffectType.CastSkill:
                    Logger.Info($"[UseItem] STUB CastSkill {use.EffectValue} for account {session.AccountId} - no skill system yet (roadmap H)");
                    return true;

                default:
                    Logger.Warn($"[UseItem] Item {use.ItemId} has unknown EffectType {(int)use.EffectType} - rejected");
                    return false;
            }
        }

        /// <summary>
        /// Shared by drop and consume, so there is exactly one piece of
        /// code that takes items out of a slot.
        /// </summary>
        private static bool TryRemoveFromSlot(
            PlayerSession session, NetPeer peer, int slot, int quantity,
            out int itemTemplateId, out int removed)
        {
            itemTemplateId = 0;
            removed = 0;

            var inventory = session.Inventory;

            if (slot < 0 || slot >= inventory.Length)
                return false;

            var current = inventory[slot];
            if (current.ItemTemplateId == 0)
                return false;

            itemTemplateId = current.ItemTemplateId;

            if (quantity <= 0 || quantity >= current.Quantity)
            {
                removed = current.Quantity;
                inventory[slot] = default;
            }
            else
            {
                removed = quantity;
                inventory[slot].Quantity -= quantity;
            }

            session.InventoryDirty = true;
            W2CInventorySlotChangedPacketSender.Send(peer, slot, inventory[slot]);
            return true;
        }
    }
}