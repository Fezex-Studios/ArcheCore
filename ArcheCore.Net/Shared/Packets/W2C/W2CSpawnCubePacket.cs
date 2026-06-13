using MessagePack;

namespace Shared.Components
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