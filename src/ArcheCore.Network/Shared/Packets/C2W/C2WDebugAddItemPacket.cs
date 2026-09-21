using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    /// <summary>
    /// DEV ONLY - same role as C2WDebugAddGoldPacket. Proves items can be
    /// granted and appear in a slot before any real source (loot, a shop
    /// purchase) exists. Delete alongside its handler once one does.
    /// </summary>
    [MessagePackObject(true)]
    public class C2WDebugAddItemPacket
    {
        public int ItemTemplateId;
        public int Quantity;
    }
}
