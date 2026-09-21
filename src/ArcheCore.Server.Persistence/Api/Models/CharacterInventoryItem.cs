namespace ArcheCore.PersistenceServer.Api.Models
{
    // One row per occupied slot. There is no row for an empty slot -
    // "empty" is the absence of a row, not a row with ItemTemplateId 0,
    // so the table never needs to store 20 rows per character, only the
    // ones actually in use. Composite key (CharacterId, Slot) is the
    // thing that makes the upsert in /characters/inventory/save an
    // ON DUPLICATE KEY UPDATE instead of a manual exists-check.
    public class CharacterInventoryItem
    {
        public long CharacterId    { get; set; }
        public int  Slot           { get; set; }
        public int  ItemTemplateId { get; set; }
        public int  Quantity       { get; set; }
    }
}