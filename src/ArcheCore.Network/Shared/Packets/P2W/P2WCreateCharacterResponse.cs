using MessagePack;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer.P2W
{
    [MessagePackObject(true)]
    public class P2WCreateCharacterResponse
    {
        public bool   Success;
        public int    AccountId;
        public long   CharacterId;
        public string Name;
    }
}