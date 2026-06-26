using MessagePack;

namespace Shared.Components
{
    [MessagePackObject(true)]
    public class W2CPlayerPositionPacket
    {
        public int NetworkId;

        public float x;
        public float y;
        public float z;
    }
}