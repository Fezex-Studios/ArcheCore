using MessagePack;

namespace ArcheCore.Net.Shared.Packets.W2C
{
    [MessagePackObject(true)]
    public class W2CSpawnCubePacket
    {
        public int   CubeId;

        public float x;
        public float y;
        public float z;
    }
}