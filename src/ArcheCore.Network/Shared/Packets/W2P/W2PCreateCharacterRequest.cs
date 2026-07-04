using MessagePack;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer.W2P
{
    [MessagePackObject(true)]
    public class W2PCreateCharacterRequest
    {
        public int    AccountId;
        public string Name;
    }
}