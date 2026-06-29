using MessagePack;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer.W2P
{   
    
    [MessagePackObject(true)]
    public class W2PConnectionRequest
    {
        public string Message;
    }
}