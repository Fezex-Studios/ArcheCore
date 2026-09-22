using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    /// <summary>
    /// "Buy Quantity of ItemTemplateId from this merchant." No price is
    /// sent - the server looks it up. See ShopManager.TryBuy.
    /// </summary>
    [MessagePackObject(true)]
    public class C2WShopBuyPacket
    {
        public int NpcNetworkId;
        public int ItemTemplateId;
        public int Quantity;
    }
}
