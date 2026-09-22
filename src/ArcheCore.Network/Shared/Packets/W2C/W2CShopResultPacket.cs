using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// Outcome of a buy or sell, for the message line. The gold and item
    /// changes themselves arrive through W2CGoldUpdate and
    /// W2CInventorySlotChanged, same as everywhere else.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CShopResultPacket
    {
        public bool   Success;
        public string Message;
    }
}
