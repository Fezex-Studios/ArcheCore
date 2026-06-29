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
    }
}