using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    /// <summary>
    /// "Move whatever is in FromSlot to ToSlot." Server decides swap vs
    /// merge vs no-op - see PlayerManager.TryMoveItem. The client never
    /// mutates its own displayed inventory on this request; it waits for
    /// the W2CInventorySlotChangedPacket(s) the server sends back, same
    /// authority rule as movement and gold.
    /// </summary>
    [MessagePackObject(true)]
    public class C2WMoveItemPacket
    {
        public int FromSlot;
        public int ToSlot;
    }
}
