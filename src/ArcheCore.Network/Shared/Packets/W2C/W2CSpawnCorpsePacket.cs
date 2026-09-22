using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// A lootable corpse came into view. Removed again with W2CNpcDespawn,
    /// like every other non-player entity. OwnerId is the player who may
    /// loot it; others see it but can't take anything.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CSpawnCorpsePacket
    {
        public int    NetworkId;
        public string Name;
        public string ModelType;     // WorldObjectPrefabRegistry key, "Corpse" by default
        public float  X;
        public float  Y;
        public float  Z;
        public float  InteractRange;
        public int    OwnerId;

        /// <summary>For the hover tooltip ("Owner: Testchar1").</summary>
        public string OwnerName;

        /// <summary>What F / G do on this object, in slot order (index 0 = F, 1 = G).
        /// From the server's InteractableActions table.</summary>
        public InteractionActionData[] Actions;
    }
}
