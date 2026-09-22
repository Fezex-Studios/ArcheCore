using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    /// <summary>Take one entry from a corpse. ItemTemplateId 0 = the gold. "Take all" is C2WInteract with LootAll.</summary>
    [MessagePackObject(true)]
    public class C2WLootTakePacket
    {
        public int CorpseNetworkId;
        public int ItemTemplateId;
    }
}
