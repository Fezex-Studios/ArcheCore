using MessagePack;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer.W2P
{
    /// <summary>
    /// Save the quests that changed for one character. Same shape and same
    /// rules as W2PInventorySaveRequest: only what changed is sent, and the
    /// persistence server upserts on (CharacterId, QuestId).
    /// </summary>
    [MessagePackObject(true)]
    public class W2PQuestSaveRequest
    {
        public int  AccountId;
        public long CharacterId;
        public QuestStateDto[] Quests;
    }
}
