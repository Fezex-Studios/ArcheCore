using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    [MessagePackObject(true)]
    public class LootWindowItem
    {
        public int    ItemTemplateId;
        public int    Quantity;
        public string ItemName;   // from the server's Items table, so it's never "#id"
    }

    /// <summary>
    /// A corpse's current contents, for the loot window. Sent when the window
    /// opens and again after every take while it's open, so the window always
    /// shows exactly what's left. An emptied corpse despawns instead (the
    /// client closes the window on W2CNpcDespawn).
    /// </summary>
    [MessagePackObject(true)]
    public class W2CLootWindowPacket
    {
        public int              CorpseNetworkId;
        public string           CorpseName;
        public int              Gold;
        public LootWindowItem[] Items;
    }
}
