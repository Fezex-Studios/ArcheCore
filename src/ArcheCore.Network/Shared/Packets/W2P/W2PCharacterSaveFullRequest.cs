using MessagePack;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer.W2P
{
    /// <summary>
    /// The ONE character save. Everything about a character that the world
    /// server owns - level, position, gold, the whole inventory and the
    /// whole quest log - in one request, written in one transaction.
    ///
    /// Replaces the old three-request save (base, inventory diff, quests),
    /// which could land half-applied and in any order.
    ///
    /// SaveSeq makes it safe to send twice and impossible to apply out of
    /// order: the persistence server only writes if SaveSeq is higher than
    /// the one it already holds, so a late or repeated request is refused
    /// instead of overwriting newer data.
    /// </summary>
    [MessagePackObject(true)]
    public class W2PCharacterSaveFullRequest
    {
        public int   AccountId;
        public long  CharacterId;

        /// <summary>Strictly increasing per character. Assigned by the world server's save chain.</summary>
        public long  SaveSeq;

        public int   Level;
        public float X;
        public float Y;
        public float Z;
        public int   Gold;

        /// <summary>
        /// The COMPLETE inventory: occupied slots only. Any slot not listed
        /// is empty afterwards. Never null.
        /// </summary>
        public InventorySlotDto[] Inventory;

        /// <summary>
        /// The COMPLETE quest log, or null to leave quests untouched. Rows
        /// with status 0 are ignored (not started = no row).
        /// </summary>
        public QuestStateDto[] Quests;

        /// <summary>
        /// Non-zero: delete this mail in the SAME transaction. Used when a
        /// player takes something out of their mailbox - the mail
        /// disappears exactly when the character that received its
        /// contents is saved, never one without the other.
        /// </summary>
        public long ClaimMailId;
    }
}
