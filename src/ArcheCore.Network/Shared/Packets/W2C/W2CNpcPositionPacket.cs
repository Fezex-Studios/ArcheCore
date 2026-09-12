using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    [MessagePackObject(true)]
    public class W2CNpcPositionPacket
    {
        public int   NetworkId;
        public float x;
        public float y;
        public float z;
    }
}