using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    /// <summary>
    /// "Sell Quantity from inventory Slot to this merchant." Quantity &lt;= 0
    /// sells the whole stack. See ShopManager.TrySell.
    /// </summary>
    [MessagePackObject(true)]
    public class C2WShopSellPacket
    {
        public int NpcNetworkId;
        public int Slot;
        public int Quantity;
    }
}
