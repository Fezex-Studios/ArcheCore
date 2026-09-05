using MessagePack;



namespace ArcheCore.Network.Shared.Packets.PersistenceServer.W2P
{
    [MessagePackObject(true)]
    public class W2PCharacterLoadRequest
    {
        public long AccountId;
        public long CharacterId;
    }
}
