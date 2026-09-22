namespace ArcheCore.Server.World.GameData.Interaction;

/// <summary>
/// One key-bound action an interactable offers. The whole F/G interaction
/// system is driven by these rows:
///
///   TargetKind  InteractableKind: 1 = NPC, 2 = corpse, 4 = harvest node
///   TemplateId  that kind's template id - or 0 for EVERY object of that
///               kind that has no rows of its own (all corpses share one set)
///   Slot        0 = F, 1 = G
///   ActionType  InteractionActionType (code): 1 Talk, 2 Trade, 3 Harvest,
///               4 LootAll, 5 OpenLoot, 6 Climb
///   Label       what the player reads - "Chop", "Mine", "Take all items"
///   IconName    client Resources/Icons/Actions/&lt;IconName&gt;.png
///   CursorName  client Resources/Cursors/&lt;CursorName&gt;.png while hovering
///   IsEnabled   0 = shown greyed out, refused by the server (Climb today)
///
/// The same Harvest action is "Chop" on a tree and "Mine" on a vein - the
/// label is data, the behaviour is code.
/// </summary>
public class InteractableAction
{
    public int    Id         { get; set; }
    public int    TargetKind { get; set; }
    public int    TemplateId { get; set; }
    public int    Slot       { get; set; }
    public int    ActionType { get; set; }
    public string Label      { get; set; } = string.Empty;
    public string IconName   { get; set; } = string.Empty;
    public string CursorName { get; set; } = "interact";
    public bool   IsEnabled  { get; set; } = true;
}
