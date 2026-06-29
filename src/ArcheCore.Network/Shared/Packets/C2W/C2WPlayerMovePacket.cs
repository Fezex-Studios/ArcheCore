using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    [MessagePackObject(true)]
    public class C2WPlayerMovePacket
    {
        public float x;
        public float y;
        public float z;
    }
}