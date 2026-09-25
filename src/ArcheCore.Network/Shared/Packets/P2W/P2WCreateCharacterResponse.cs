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

        /// <summary>Why it failed, for the player ("That name is taken."). Empty on success.</summary>
        public string Reason;
    }
}