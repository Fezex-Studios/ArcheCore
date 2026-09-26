using System;
using System.Collections.Generic;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// The quest log. Saved. QuestManager is the only writer; every change
    /// sets Dirty.
    /// </summary>
    public sealed class QuestComponent : IPersistentComponent
    {
        /// <summary>Every quest this character has touched, by quest id.</summary>
        public readonly Dictionary<int, QuestProgress> Progress = new();

        /// <summary>Set by QuestManager on any change; cleared by the snapshot.</summary>
        public bool Dirty;

        /// <summary>
        /// Rows loaded for quests that no longer exist in the game data. Not
        /// shown or used - written back with every save, so taking a quest out
        /// of the data (temporarily, by mistake) doesn't erase everyone's
        /// progress in it.
        /// </summary>
        public QuestStateDto[] UnknownRows;

        /// <summary>
        /// The log was loaded from the database (QuestManager.LoadInto). Until
        /// it is - quests disabled, or a test with no QuestManager - the
        /// snapshot sends Quests = null, which the persistence server reads as
        /// "leave the quest rows alone". Sending an empty log instead would
        /// delete them.
        /// </summary>
        public bool Loaded;

        public bool IsDirty => Dirty;

        public void WriteTo(W2PCharacterSaveFullRequest snapshot)
        {
            if (!Loaded)
            {
                snapshot.Quests = null;
                return;
            }

            var unknown = UnknownRows ?? Array.Empty<QuestStateDto>();
            var rows = new QuestStateDto[Progress.Count + unknown.Length];
            int i = 0;

            foreach (var row in unknown)
                rows[i++] = row;

            foreach (var quest in Progress.Values)
            {
                rows[i++] = new QuestStateDto
                {
                    QuestId  = quest.QuestId,
                    Status   = (byte)quest.Status,
                    Progress = quest.ToProgressString()
                };
            }

            snapshot.Quests = rows;
        }

        public void MarkSaved() => Dirty = false;

        /// <summary>Back to "nothing loaded" (before a LoadInto).</summary>
        public void Clear()
        {
            Progress.Clear();
            UnknownRows = null;
            Dirty  = false;
            Loaded = false;
        }
    }
}
