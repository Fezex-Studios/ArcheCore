using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>What an objective asks for. The count is always "how many".</summary>
    public enum QuestObjectiveType
    {
        None    = 0,
        Kill    = 1,   // TargetId = NpcTemplates.Id
        Collect = 2,   // TargetId = Items.item_id   (counted from the bag, taken on hand-in)
        Talk    = 3,   // TargetId = NpcTemplates.Id
    }

    /// <summary>Where a quest stands for one character.</summary>
    public enum QuestStatus
    {
        None      = 0,
        Active    = 1,   // in the log, objectives not met
        Complete  = 2,   // objectives met, waiting to be handed in
        TurnedIn  = 3,   // done
    }

    [MessagePackObject(true)]
    public class QuestObjectiveData
    {
        public int    ObjectiveType;
        public int    TargetId;
        public int    RequiredCount;

        /// <summary>Shown in the log, e.g. "Slay Orc Grunts". Falls back to a generated line if empty.</summary>
        public string Text;
    }

    /// <summary>
    /// A quest as the CLIENT needs it: enough to draw the log, the giver's
    /// dialogue and the tracker without asking the server anything else.
    /// Sent once, on entering the world.
    /// </summary>
    [MessagePackObject(true)]
    public class QuestDefinitionData
    {
        public int    Id;
        public string Name;
        public string Description;

        /// <summary>Told to the player when they accept it ("Bring me five iron ore...").</summary>
        public string AcceptText;

        /// <summary>Told to them when they hand it in.</summary>
        public string CompleteText;

        public int    MinLevel;
        public int    GiverNpcTemplateId;
        public int    TurnInNpcTemplateId;

        public int    RewardGold;
        public int    RewardItemId;
        public int    RewardItemQuantity;
        public string RewardItemName;

        public QuestObjectiveData[] Objectives;
    }

    /// <summary>One quest's state for this character: status plus a count per objective.</summary>
    [MessagePackObject(true)]
    public class QuestProgressData
    {
        public int   QuestId;
        public int   Status;
        public int[] Counts;
    }
}
