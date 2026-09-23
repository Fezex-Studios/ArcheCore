using System;
using ArcheCore.Network.Shared.Packets.W2C;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// One character's state for one quest, in memory: where it stands and
    /// how far each objective has got. Counts is as long as the quest has
    /// objectives, and is stored as the comma-separated Progress string
    /// (see QuestStateDto).
    /// </summary>
    public class QuestProgress
    {
        public int QuestId;
        public QuestStatus Status;
        public int[] Counts = Array.Empty<int>();

        public string ToProgressString() => string.Join(',', Counts);

        /// <summary>
        /// Rebuilds the counts from storage. A quest whose objectives changed
        /// since the character last played gets its counts resized rather
        /// than dropped, so editing a live quest can't wipe anyone's progress
        /// or crash on a short array.
        /// </summary>
        public static int[] ParseCounts(string progress, int objectiveCount)
        {
            var counts = new int[objectiveCount];
            if (string.IsNullOrWhiteSpace(progress))
                return counts;

            var parts = progress.Split(',');
            for (int i = 0; i < objectiveCount && i < parts.Length; i++)
                int.TryParse(parts[i], out counts[i]);

            return counts;
        }
    }
}
