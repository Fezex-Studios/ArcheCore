using MessagePack;

namespace Shared.Packets
{
    [MessagePackObject(true)]
    public class W2CMOTDPacket
    {
        public string Message;
    }
}