namespace ArcheCore.PersistenceServer.Api.Models
{
    /// <summary>
    /// One row per quest a character has touched. A quest they've never
    /// seen has no row, the same way an empty inventory slot has none, so
    /// the table only holds what actually happened.
    ///
    /// Progress is a comma-separated count per objective ("3,1"). Unlike
    /// inventory slots - which change one at a time and are read one at a
    /// time - a quest's objectives are always read and written together, so
    /// a row each would buy nothing and cost a join.
    ///
    /// Composite key (CharacterId, QuestId) is what makes the save an
    /// upsert instead of an exists-check, same as inventory's
    /// (CharacterId, Slot).
    /// </summary>
    public class CharacterQuest
    {
        public long   CharacterId { get; set; }
        public int    QuestId     { get; set; }

        /// <summary>1 = active, 2 = objectives met, 3 = handed in.</summary>
        public byte   Status      { get; set; }

        public string Progress    { get; set; } = string.Empty;
    }
}
