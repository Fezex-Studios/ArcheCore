using MessagePack;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer
{
    /// <summary>
    /// One inventory slot, as it travels between WorldServer and
    /// Persistence. Used two ways:
    ///   - On P2WCharacterLoadResponse.Inventory: the full set of
    ///     occupied slots for a freshly-loaded character (sparse - only
    ///     slots that actually have an item are included).
    ///   - On W2PInventorySaveRequest.Changes: only the slots that
    ///     changed since the last confirmed save (also sparse, but for a
    ///     different reason - it's a diff, not a snapshot).
    /// In both cases, this type carries the payload; only the field
    /// meanings around it differ. ItemTemplateId/Quantity <= 0 in a
    /// Changes entry means "this slot is now empty - delete its row."
    /// </summary>
    [MessagePackObject(true)]
    public class InventorySlotDto
    {
        public int Slot;
        public int ItemTemplateId;
        public int Quantity;
    }
}
