using System.ComponentModel.DataAnnotations.Schema;

namespace ArcheCore.Server.World.GameData.Quests;

/// <summary>
/// A quest. Who gives it, who takes it back, what it pays, and what has to
/// be true before it can be taken. What must be DONE is QuestObjectiveTable.
///
/// A new quest is rows in these two tables - no code, same as items, nodes
/// and shops.
/// </summary>
[Table("Quests")]
public class QuestTable
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Said when the quest is accepted. Empty = nothing is said.</summary>
    public string AcceptText { get; set; } = "";

    /// <summary>Said when it's handed in.</summary>
    public string CompleteText { get; set; } = "";

    /// <summary>NpcTemplates.Id that offers it.</summary>
    public int GiverNpcTemplateId { get; set; }

    /// <summary>NpcTemplates.Id it's handed in to. 0 = the same NPC that gave it.</summary>
    public int TurnInNpcTemplateId { get; set; }

    public int MinLevel { get; set; }

    /// <summary>Must already be handed in before this one is offered. 0 = no prerequisite.</summary>
    public int RequiredQuestId { get; set; }

    public int RewardGold { get; set; }
    public int RewardItemId { get; set; }
    public int RewardItemQuantity { get; set; }
}

/// <summary>
/// One thing a quest asks for. Several rows make a quest with several
/// objectives, ordered by SortOrder.
///
///   ObjectiveType 1 Kill    TargetId = NpcTemplates.Id
///                 2 Collect TargetId = Items.item_id  (counted in the bag,
///                                                      taken on hand-in)
///                 3 Talk    TargetId = NpcTemplates.Id
/// </summary>
[Table("QuestObjectives")]
public class QuestObjectiveTable
{
    public int Id { get; set; }
    public int QuestId { get; set; }
    public int SortOrder { get; set; }
    public int ObjectiveType { get; set; }
    public int TargetId { get; set; }
    public int RequiredCount { get; set; } = 1;

    /// <summary>Line shown in the log. Empty = one is generated ("Slay Orc Grunt: 2/3").</summary>
    public string Text { get; set; } = "";
}
