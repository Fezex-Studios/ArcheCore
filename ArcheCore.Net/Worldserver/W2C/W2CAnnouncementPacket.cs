using MessagePack;

namespace Shared.Packets
{
    [MessagePackObject(true)]
    public class W2CAnnouncementPacket
    {
        public string Message;
    }
}