using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// One slot changed. Sent instead of a full snapshot for every
    /// mutation - a 20-slot re-send for a 1-slot change is the same
    /// mistake the world snapshot's per-entity LOD exists to avoid,
    /// just at a much smaller scale here since inventory isn't spatial.
    /// ItemTemplateId 0 means the slot is now empty.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CInventorySlotChangedPacket
    {
        public int Index;
        public int ItemTemplateId;
        public int Quantity;
    }
}
