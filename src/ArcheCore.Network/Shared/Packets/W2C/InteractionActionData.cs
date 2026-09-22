using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// What pressing a key does to an interactable. Every interaction in
    /// the game goes through this: harvesting, looting, talking, trading.
    /// The type is fixed code (each needs server logic); everything a
    /// player SEES - label, icon, cursor, whether it's available - is data
    /// from the server's InteractableActions table.
    /// </summary>
    public enum InteractionActionType
    {
        None      = 0,
        Talk      = 1,   // NPC greeting + Lua OnInteract
        Trade     = 2,   // open the NPC's shop
        Harvest   = 3,   // harvest node (label says Mine / Chop / Gather...)
        LootAll   = 4,   // corpse: take everything that fits
        OpenLoot  = 5,   // corpse: open the loot window
        Climb     = 6,   // placeholder - listed, greyed out, not implemented yet
    }

    [MessagePackObject(true)]
    public class InteractionActionData
    {
        public int    ActionType;
        public string Label;       // "Chop", "Take all items"
        public string IconName;    // Resources/Icons/Actions/<IconName>.png on the client
        public string CursorName;  // Resources/Cursors/<CursorName>.png while hovering
        public bool   IsEnabled;   // false = shown greyed out (e.g. Climb)
    }
}
