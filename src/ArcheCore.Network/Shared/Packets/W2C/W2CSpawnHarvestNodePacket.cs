using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// A harvest node entered the player's interest radius. Same role as
    /// W2CSpawnNpcPacket, for a static gatherable object. Sent reliably,
    /// once per "came into view"; W2CNpcDespawn removes it again when the
    /// player walks away (the client checks both registries for that id).
    ///
    /// IsDepleted is included so a player arriving at an already-harvested
    /// node sees it depleted straight away, instead of seeing it available
    /// until the next state change.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CSpawnHarvestNodePacket
    {
        public int    NetworkId;
        public int    TemplateId;
        public string Name;
        public string ModelType;     // WorldObjectPrefabRegistry key
        public float  X;
        public float  Y;
        public float  Z;
        public float  Yaw;           // degrees
        public float  InteractRange;
        public bool   IsDepleted;

        /// <summary>What F / G do on this object, in slot order (index 0 = F, 1 = G).
        /// From the server's InteractableActions table.</summary>
        public InteractionActionData[] Actions;
    }
}
