using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// Every quest definition, sent once on entering the world. It's small
    /// (a few hundred rows at most) and sending it whole means the log, the
    /// tracker, the giver dialogue and the "!" markers need no further round
    /// trips - the client can answer "does this NPC have anything for me?"
    /// on its own.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CQuestCatalogPacket
    {
        public QuestDefinitionData[] Quests;
    }

    /// <summary>This character's whole quest state, sent right after the catalog.</summary>
    [MessagePackObject(true)]
    public class W2CQuestLogPacket
    {
        public QuestProgressData[] Quests;
    }

    /// <summary>
    /// One quest changed: accepted, an objective ticked up, ready to hand in,
    /// handed in, or abandoned. Message is what the player is told.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CQuestUpdatePacket
    {
        public QuestProgressData Quest;
        public string Message;

        /// <summary>True when the quest left the log (abandoned), so the client drops it.</summary>
        public bool Removed;
    }

    /// <summary>
    /// What a quest giver has for you right now, in answer to their Quests
    /// action. The client already has the text from the catalog, so this is
    /// only which ids fall in which bucket.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CQuestOffersPacket
    {
        public int    NpcNetworkId;
        public string NpcName;
        public int[]  Available;     // can be accepted now
        public int[]  Completable;   // objectives met, hand in here
        public int[]  InProgress;    // accepted, not finished
    }
}
