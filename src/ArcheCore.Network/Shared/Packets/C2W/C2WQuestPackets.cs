using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    /// <summary>"Give me this quest." The server re-checks the giver, range, level and prerequisites.</summary>
    [MessagePackObject(true)]
    public class C2WQuestAcceptPacket
    {
        public int NpcNetworkId;
        public int QuestId;
    }

    /// <summary>"I'm done." The server re-checks the objectives and takes any Collect items.</summary>
    [MessagePackObject(true)]
    public class C2WQuestCompletePacket
    {
        public int NpcNetworkId;
        public int QuestId;
    }

    /// <summary>"Drop it." No NPC needed - you can abandon from the log anywhere.</summary>
    [MessagePackObject(true)]
    public class C2WQuestAbandonPacket
    {
        public int QuestId;
    }
}
