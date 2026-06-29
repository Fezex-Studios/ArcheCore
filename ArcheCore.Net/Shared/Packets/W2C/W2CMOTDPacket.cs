using MessagePack;

namespace ArcheCore.Net.Shared.Packets.W2C
{
    [MessagePackObject(true)]
    public class W2CMOTDPacket
    {
        public string Message;
    }
}