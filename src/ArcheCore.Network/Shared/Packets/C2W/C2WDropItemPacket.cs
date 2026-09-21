using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    /// <summary>
    /// "Destroy Quantity items from Slot." Quantity &lt;= 0 means the whole
    /// stack. The client only sends this AFTER the player confirms the
    /// destroy dialog - the server doesn't know or care about the dialog,
    /// it just validates and applies. Client never clears its own slot;
    /// it waits for the W2CInventorySlotChangedPacket the server sends
    /// back. See PlayerManager.TryDropItem.
    /// </summary>
    [MessagePackObject(true)]
    public class C2WDropItemPacket
    {
        public int Slot;
        public int Quantity;
    }
}