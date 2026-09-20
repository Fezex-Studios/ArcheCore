using System;
using System.Threading.Tasks;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// The single call-site for "save this character".
    ///
    /// SaveInBackground is what gameplay code uses: it copies the session's
    /// values ON THE TICK THREAD, marks the session saved, and sends the save
    /// without blocking the tick. If the save fails, the session is marked
    /// unsaved again (back on the tick thread, via the enqueue callback) so
    /// the next autosave retries it.
    ///
    /// SaveAsync is for shutdown, where the caller needs to wait.
    /// </summary>
    public class CharacterPersistence
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly PersistenceClient _persistence;
        private readonly Action<Action> _enqueueOnTickThread;

        public CharacterPersistence(PersistenceClient persistence, Action<Action> enqueueOnTickThread)
        {
            _persistence = persistence;
            _enqueueOnTickThread = enqueueOnTickThread;
        }

        /// <summary>Tick thread only.</summary>
        public void SaveInBackground(PlayerSession session)
        {
            long characterId = session.CharacterId;
            int accountId    = session.AccountId;
            string name      = session.Name;
            int level        = session.Level;
            var pos          = session.Position;
            int gold         = session.Gold;

            session.MarkSaved();

            _ = SaveAndReportAsync(session, characterId, accountId, name, level, pos, gold);
        }

        private async Task SaveAndReportAsync(
            PlayerSession session, long characterId, int accountId, string name,
            int level, System.Numerics.Vector3 pos, int gold)
        {
            bool ok = await SaveAsync(characterId, accountId, name, level, pos, gold);
            if (!ok)
            {
                // Back on the tick thread: force the next autosave to retry.
                _enqueueOnTickThread(() => session.HasBeenSaved = false);
            }
        }

        /// <summary>Safe from any thread. Never throws. Returns true on a confirmed save.</summary>
        public async Task<bool> SaveAsync(
            long characterId, int accountId, string name, int level,
            System.Numerics.Vector3 pos, int gold)
        {
            try
            {
                bool ok = await _persistence.W2PCharacterSave.Send(
                    characterId, accountId, name, level, pos.X, pos.Y, pos.Z, gold);

                if (ok)
                    Logger.Debug($"[Save] CharacterId={characterId} saved.");
                else
                    Logger.Error($"[Save] FAILED CharacterId={characterId} - persistence server returned an error.");

                return ok;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[Save] FAILED CharacterId={characterId} - persistence server unreachable.");
                return false;
            }
        }
    }
}