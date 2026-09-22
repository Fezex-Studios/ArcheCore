using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// Your harvest finished. The item itself already arrived through the
    /// normal W2CInventorySlotChanged path - this is just the "+3 Iron Ore"
    /// feedback. ItemName comes from the server's item table so the message
    /// is right even if the client's gamedata is out of date.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CHarvestCompletedPacket
    {
        public int    NodeNetworkId;
        public int    ItemTemplateId;
        public int    Quantity;
        public string ItemName;
    }
}
