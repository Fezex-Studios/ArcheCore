using MessagePack;

namespace ArcheCore.Net.Shared.Packets.PersistenceServer.W2P
{
    [MessagePackObject(true)]
    public class W2PHelloWorldPacket
    {
        public string Message;
    }
}