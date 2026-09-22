using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    [MessagePackObject(true)]
    public class W2CSpawnNpcPacket
    {
        public int    NetworkId;
        public int    TemplateId;
        public string Name;
        public int    Level;
        public string ModelType;  // tells client which prefab to use
        public float  X;
        public float  Y;
        public float  Z;
        public float InteractRange;
        /// <summary>For the target frame. MaxHealth 0 = this NPC can't be attacked (merchants).</summary>
        public int    Health;
        public int    MaxHealth;

        /// <summary>Shown above the name, e.g. "Merchant". Empty = none.</summary>
        public string Title;

        /// <summary>What F / G do on this object, in slot order (index 0 = F, 1 = G).
        /// From the server's InteractableActions table.</summary>
        public InteractionActionData[] Actions;
    }
}