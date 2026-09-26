using System.Collections.Generic;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Gold, the bag and item cooldowns. Saved.
    ///
    /// Server-authoritative: the only writers are PlayerManager's
    /// TryAddGold / TryAddItem / TryMoveItem / TryDropItem / TryUseItem
    /// (and MailManager putting a claimed mail's contents in). Every writer
    /// sets Dirty.
    /// </summary>
    public sealed class InventoryComponent : IPersistentComponent
    {
        public int Gold;

        /// <summary>ItemTemplateId 0 = empty (see InventorySlot).</summary>
        public readonly InventorySlot[] Slots = new InventorySlot[InventoryConstants.SlotCount];

        /// <summary>
        /// ItemManager.CooldownKey -> ServerClock.NowMs when it's ready again.
        /// Keyed by group, never by slot, so moving an item can't reset its
        /// timer. Not saved - a relog clears them (fine for 30s potions).
        /// </summary>
        public readonly Dictionary<int, long> Cooldowns = new();

        /// <summary>
        /// Slots may differ from the last snapshot. A bool flip rather than a
        /// 20-slot compare because the autosave asks every player every pass.
        /// </summary>
        public bool Dirty;

        private int _savedGold;

        public bool IsDirty => Dirty || Gold != _savedGold;

        public void WriteTo(W2PCharacterSaveFullRequest snapshot)
        {
            var slots = new List<InventorySlotDto>(Slots.Length);

            for (int i = 0; i < Slots.Length; i++)
            {
                var slot = Slots[i];
                if (slot.ItemTemplateId > 0 && slot.Quantity > 0)
                    slots.Add(new InventorySlotDto { Slot = i, ItemTemplateId = slot.ItemTemplateId, Quantity = slot.Quantity });
            }

            snapshot.Gold      = Gold;
            snapshot.Inventory = slots.ToArray();
        }

        public void MarkSaved()
        {
            _savedGold = Gold;
            Dirty = false;
        }
    }
}
