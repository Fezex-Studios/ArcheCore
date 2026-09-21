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
        /// Environment.TickCount64 milliseconds (monotonic, so a system
        /// clock change can't unlock a potion). Keyed by group, never by
        /// slot, so moving an item can't reset its timer.
        ///
        /// Not persisted - a relog clears every cooldown. Fine for 30s
        /// potions; add it to the save when something has an hour-long one.
        /// </summary>
        public readonly Dictionary<int, long> ItemCooldowns = new();

        /// <summary>A select/create for this peer is already in flight or done.</summary>
        public bool     SpawnRequested;

        // --- Save tracking (see AutosaveScheduler / CharacterPersistence) ---

        /// <summary>False until the current state is known to match the database.</summary>
        public bool     HasBeenSaved;
        public Vector3  SavedPosition;
        public int      SavedLevel;
        public int      SavedGold;

        /// <summary>
        /// What the database is CONFIRMED to hold, slot for slot. This is
        /// deliberately NOT updated the moment a save is fired off - only
        /// once CharacterPersistence hears back that the inventory diff
        /// was actually written. Between "fired" and "confirmed",
        /// InventoryDirty stays true precisely so a failed or in-flight
        /// save still gets retried with a fresh, correct diff next time,
        /// instead of silently treating an unconfirmed write as done.
        /// </summary>
        public InventorySlot[] SavedInventory = new InventorySlot[InventoryConstants.SlotCount];

        /// <summary>
        /// True whenever Inventory may differ from SavedInventory.
        /// A bool flip, not a 20-slot array compare, because this is
        /// checked once per player per autosave pass (AutosaveScheduler)
        /// and a linear array compare there is the "cheap at 2 players,
        /// not at 500" cost this field exists to avoid. Set by
        /// TryAddItem/TryMoveItem; cleared only once a save of the
        /// current diff is CONFIRMED (see CharacterPersistence).
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

        /// <summary>Moved more than 10cm, changed level, gold changed, or
        /// inventory changed since the last confirmed save.</summary>
        public bool IsDirty =>
            !HasBeenSaved ||
            Level != SavedLevel ||
            Gold  != SavedGold ||
            InventoryDirty ||
            Vector3.DistanceSquared(Position, SavedPosition) > 0.01f;

        /// <summary>
        /// Declares the CURRENT in-memory state to be exactly what the
        /// database holds. Correct to call right after a fresh load
        /// (memory was just built FROM the database, so by definition
        /// they match) or at initial spawn.
        ///
        /// Do NOT call this mid-save to "optimistically" mark things
        /// saved before a send is confirmed - CharacterPersistence
        /// handles that distinction deliberately, because gold/level/
        /// position are idempotent full-value sends (safe to assume-then-
        /// retry) but inventory is a diff (assuming success early means a
        /// failed write is never resent). See CharacterPersistence for
        /// why the two are NOT treated the same way there.
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