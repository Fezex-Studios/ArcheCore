using MessagePack;

namespace Shared.Packets
{
    [MessagePackObject(true)]
    public class W2CAnnouncement
    {
        public string Message;
    }
}