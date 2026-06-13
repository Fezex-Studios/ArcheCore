using MessagePack;

namespace Shared.Components
{
    [MessagePackObject(true)]
    public class W2CPlayerLeavePacket
    {
        public int NetworkId;
    }
}