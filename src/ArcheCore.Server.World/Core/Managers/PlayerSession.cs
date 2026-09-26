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
    /// The session itself holds identity, where the player is and save
    /// bookkeeping. Each system's state lives in its own component
    /// (roadmap fix-first #3), so a system only touches its own part:
    ///
    ///   Inventory  gold, bag, item cooldowns          saved
    ///   Quests     quest log                          saved
    ///   Combat     health, death, skill cooldowns
    ///   Mount      mount, speed multiplier, pet
    ///   Movement   MovementValidator's budgets
    ///   Market     auction/mail NPC, in-flight mail claims
    ///
    /// Saved components implement IPersistentComponent and are listed in
    /// PersistentComponents; the save snapshot and IsDirty go through them.
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
        /// Zone the player is standing in (ZoneMap id, 0 = none). Kept current
        /// by PlayerManager on every accepted move and teleport; changing it
        /// fires PlayerEvent.OnEnterZone. Not persisted - recomputed on spawn.
        /// </summary>
        public ushort ZoneId;

        /// <summary>A select/create for this peer is already in flight or done.</summary>
        public bool     SpawnRequested;

        // ── Components ──

        public readonly InventoryComponent Inventory = new();
        public readonly QuestComponent     Quests    = new();
        public readonly CombatComponent    Combat    = new();
        public readonly MountComponent     Mount     = new();
        public readonly MovementComponent  Movement  = new();
        public readonly MarketComponent    Market    = new();

        /// <summary>Every saved component, in snapshot order.</summary>
        public readonly IPersistentComponent[] PersistentComponents;

        public PlayerSession()
        {
            PersistentComponents = new IPersistentComponent[] { Inventory, Quests };
        }

        // ── Save tracking (see AutosaveScheduler / CharacterPersistence) ──

        /// <summary>False until the current state is known to match the database.</summary>
        public bool     HasBeenSaved;
        public Vector3  SavedPosition;
        public int      SavedLevel;

        /// <summary>
        /// Moved more than 10cm, changed level or any saved component since
        /// the last save snapshot - or the last save failed.
        /// </summary>
        public bool IsDirty
        {
            get
            {
                if (!HasBeenSaved || Level != SavedLevel ||
                    Vector3.DistanceSquared(Position, SavedPosition) > 0.01f)
                    return true;

                foreach (var component in PersistentComponents)
                    if (component.IsDirty)
                        return true;

                return false;
            }
        }

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

            foreach (var component in PersistentComponents)
                component.MarkSaved();
        }
    }
}
