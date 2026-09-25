using System;
using System.Collections.Generic;
using System.Numerics;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// One inventory slot. ItemTemplateId 0 means empty - there is no
    /// separate "IsEmpty" flag, because a template id of 0 can never be a
    /// real item (GameData ids start at 1) and adding a second way to say
    /// "empty" is a second thing that can disagree with the first.
    /// </summary>
    public struct InventorySlot
    {
        public int ItemTemplateId;
        public int Quantity;
    }

    /// <summary>Single source of truth for slot count - referenced by the
    /// session's array size, the snapshot packet, and the client's grid.</summary>
    public static class InventoryConstants
    {
        public const int SlotCount = 20;
    }

    /// <summary>
    /// Everything the server knows about one connected player, attached
    /// directly to the owning NetPeer via peer.Tag.
    ///
    /// Lifecycle:
    ///   - Created by TrackPendingSelection right after auth succeeds.
    ///     NetworkId is null - authenticated, no character in world yet.
    ///   - SpawnRequested flips to true the moment a select/create request
    ///     is accepted, so a second (double-clicked) request is ignored
    ///     instead of spawning the same peer twice.
    ///   - NetworkId is assigned by PlayerSpawnManager.SpawnPlayer. From then
    ///     on, NetworkId != null means "actually in the world."
    ///   - peer.Tag is cleared in CleanupPeer on disconnect.
    ///
    /// Only touched on the tick thread.
    /// </summary>
    public class PlayerSession
    {
        public int      AccountId;
        public int?     NetworkId;
        public long     CharacterId;
        public string   Name;
        public int      Level;
        public Vector3  Position;

        /// <summary>
        /// Gold balance. Server-authoritative - the ONLY place this
        /// should ever be written is PlayerManager.TryAddGold.
        /// </summary>
        public int       Gold;

        /// <summary>
        /// Inventory slots. Server-authoritative the same way Gold is -
        /// the ONLY place this array should ever be written is
        /// PlayerManager.TryAddItem, TryMoveItem, TryDropItem and TryUseItem.
        /// </summary>
        public InventorySlot[] Inventory = new InventorySlot[InventoryConstants.SlotCount];

        /// <summary>
        /// Item cooldowns: ItemManager.CooldownKey -> expiry, in
        /// ServerClock.NowMs milliseconds (monotonic, so a system
        /// clock change can't unlock a potion). Keyed by group, never by
        /// slot, so moving an item can't reset its timer.
        ///
        /// Not persisted - a relog clears every cooldown. Fine for 30s
        /// potions; add it to the save when something has an hour-long one.
        /// </summary>
        public readonly Dictionary<int, long> ItemCooldowns = new();

        // ── Combat (roadmap G/H) ──

        /// <summary>
        /// Current and max health. Not persisted: set to full from
        /// HealthRules.PlayerMaxHealth(Level) at spawn. Changed only through
        /// PlayerManager (heals, level-ups) and CombatManager (damage, J).
        /// </summary>
        public int Health;
        public int MaxHealth;
        public bool IsDead => MaxHealth > 0 && Health <= 0;

        /// <summary>Skill id -> ServerClock.NowMs when it's ready again.</summary>
        public readonly Dictionary<int, long> SkillCooldowns = new();

        // ── Mount (roadmap N) ──

        /// <summary>Mounts.Id being ridden, or 0. Not persisted: you start on foot.</summary>
        public int MountId;

        /// <summary>
        /// What the movement check allows, relative to normal. 1 on foot, the
        /// mount's multiplier while riding. Set only by MountManager.
        /// </summary>
        public float SpeedMultiplier = 1f;

        /// <summary>Network id of this player's summoned pet, or 0 (roadmap O).</summary>
        public int PetNetworkId;

        /// <summary>
        /// Zone the player is standing in (ZoneMap id, 0 = none). Kept current
        /// by PlayerManager on every accepted move and teleport; changing it
        /// fires PlayerEvent.OnEnterZone. Not persisted - recomputed on spawn.
        /// </summary>
        public ushort ZoneId;

        /// <summary>
        /// Network id of the NPC the player last opened the auction house or
        /// mailbox at, or 0. Every auction/mail packet is checked against it
        /// (MarketAccess) - audit H4.
        /// </summary>
        public int MarketTargetId;

        /// <summary>Where this player died - picks the nearest respawn point.</summary>
        public Vector3 DiedAt;

        // ── Quests (roadmap K) ──

        /// <summary>
        /// Every quest this character has touched, by quest id. Loaded on
        /// spawn, saved like the inventory: only when QuestsDirty says
        /// something changed.
        /// </summary>
        public readonly Dictionary<int, QuestProgress> Quests = new();

        /// <summary>Set by QuestManager on any change; cleared by the save.</summary>
        public bool QuestsDirty;

        /// <summary>
        /// Quest rows loaded for quests that no longer exist in the game data.
        /// Not shown or used - just written back with every save, so taking a
        /// quest out of the data (temporarily, by mistake) doesn't erase
        /// everyone's progress in it.
        /// </summary>
        public ArcheCore.Network.Shared.Packets.PersistenceServer.QuestStateDto[] UnknownQuestRows;

        /// <summary>
        /// Mail ids whose claim-save is in flight. A second claim of the same
        /// mail is ignored until the first has an answer, so one mail can't be
        /// paid out twice in memory.
        /// </summary>
        public readonly HashSet<long> ClaimingMail = new();

        /// <summary>A select/create for this peer is already in flight or done.</summary>
        public bool     SpawnRequested;

        // --- Save tracking (see AutosaveScheduler / CharacterPersistence) ---

        /// <summary>False until the current state is known to match the database.</summary>
        public bool     HasBeenSaved;
        public Vector3  SavedPosition;
        public int      SavedLevel;
        public int      SavedGold;

        /// <summary>
        /// The inventory as of the last save snapshot. Every save sends the
        /// whole inventory now (see CharacterPersistence), so this is only
        /// kept for comparisons and debugging.
        /// </summary>
        public InventorySlot[] SavedInventory = new InventorySlot[InventoryConstants.SlotCount];

        /// <summary>
        /// True whenever Inventory may differ from the last save snapshot.
        /// A bool flip, not a 20-slot array compare, because this is
        /// checked once per player per autosave pass. Set by every inventory
        /// change; cleared when a snapshot is taken.
        /// </summary>
        public bool InventoryDirty;

        // --- Movement validation (see MovementValidator) ---

        public bool     MoveBaselineSet;
        public Vector3  LastValidPosition;
        public double   LastMoveTime;
        public float    HorizontalBudget;
        public float    UpBudget;
        public float    DownBudget;
        public int      MovementViolations;
        public double   LastCorrectionTime;

        /// <summary>Moved more than 10cm, changed level, gold, inventory or
        /// quests since the last save snapshot - or the last save failed.</summary>
        public bool IsDirty =>
            !HasBeenSaved ||
            Level != SavedLevel ||
            Gold  != SavedGold ||
            InventoryDirty ||
            QuestsDirty ||
            Vector3.DistanceSquared(Position, SavedPosition) > 0.01f;

        /// <summary>
        /// Declares the CURRENT in-memory state saved: right after a fresh
        /// load (memory was just built from the database), or when
        /// CharacterPersistence takes a save snapshot - which its save chain
        /// then delivers or definitely fails (and a failure sets
        /// HasBeenSaved back to false).
        /// </summary>
        public void MarkSaved()
        {
            HasBeenSaved  = true;
            SavedPosition = Position;
            SavedLevel    = Level;
            SavedGold     = Gold;
            Array.Copy(Inventory, SavedInventory, InventoryConstants.SlotCount);
            InventoryDirty = false;
        }
    }
}