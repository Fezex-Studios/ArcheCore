using MessagePack;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer.P2W
{
    [MessagePackObject(true)]
    public class P2WConnectResponse
    {
        public string Message;
    }
}
