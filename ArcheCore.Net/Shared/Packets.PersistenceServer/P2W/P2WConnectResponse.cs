using MessagePack;

namespace ArcheCore.Net.Shared.Packets.PersistenceServer.P2W
{
    [MessagePackObject(true)]
    public class P2WConnectResponse
    {
        public string Message;
    }
}
