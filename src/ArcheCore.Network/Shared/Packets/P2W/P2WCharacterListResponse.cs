// Shared/Packets/P2W/P2WCharacterListResponse.cs

using MessagePack;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer.P2W
{
    [MessagePackObject(true)]
    public class CharacterSummary
    {
        public long   CharacterId;
        public string Name;
        public int    Level;
    }

    [MessagePackObject(true)]
    public class P2WCharacterListResponse
    {
        public int AccountId;
        public CharacterSummary[] Characters;
    }
}