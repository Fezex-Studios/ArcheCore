using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>One slot: empty when ItemTemplateId == 0.</summary>
    [MessagePackObject(true)]
    public class InventorySlotData
    {
        public int Index;
        public int ItemTemplateId;
        public int Quantity;
    }

    /// <summary>
    /// The player's whole inventory, sent once on spawn. Everything after
    /// this is a single-slot delta (W2CInventorySlotChangedPacket) - the
    /// snapshot exists so the client never has to guess what the other 19
    /// slots were before the first delta arrives.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CInventorySnapshotPacket
    {
        public InventorySlotData[] Slots;
    }
}
