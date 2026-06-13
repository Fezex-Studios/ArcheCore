using MessagePack;

namespace ArcheCore.WorldServer.PersistenceServer.Packets
{   
    
    [MessagePackObject(true)]
    public class W2PConnectionRequest
    {
        public string Message;
    }
}