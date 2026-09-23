using MessagePack;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer
{
    /// <summary>
    /// One character's state for one quest, as stored. Sits next to
    /// InventorySlotDto because it plays the same role: the shape the world
    /// server and the persistence server agree on.
    ///
    /// Progress is a comma-separated count per objective ("3,1"), not one row
    /// per objective. A quest has a handful of objectives that are always
    /// read and written together, so splitting them across rows would buy
    /// nothing and cost a join - unlike inventory slots, which change one at
    /// a time.
    /// </summary>
    [MessagePackObject(true)]
    public class QuestStateDto
    {
        public int    QuestId;

        /// <summary>1 = active, 2 = objectives met (ready to hand in), 3 = handed in.</summary>
        public byte   Status;

        public string Progress;
    }
}
