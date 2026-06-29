using MessagePack;

namespace ArcheCore.Net.Shared.Packets.W2C
{
    [MessagePackObject(true)]
    public class W2CPlayerLeavePacket
    {
        public int NetworkId;
    }
}