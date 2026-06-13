using MessagePack;

namespace MMO.Shared.Packets
{
    [MessagePackObject(true)]
    public class C2WPlayerMovePacket
    {
        public float x;
        public float y;
        public float z;
    }
}