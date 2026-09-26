using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// The single call-site for "save this character".
    ///
    /// Every save is a FULL SNAPSHOT - level, position, gold, every
    /// inventory slot, the whole quest log - taken on the tick thread and
    /// written by the persistence server in one transaction
    /// (/characters/save-full). No diffs: a diff is only correct relative to
    /// what the database holds, and "what the database holds" is exactly
    /// what a lost or reordered request makes unknowable.
    ///
    /// Snapshots go through the character's CharacterSaveChain: one request
    /// in flight at a time, strictly ordered by SaveSeq, retried with the
    /// same SaveSeq until the database gives a definite answer. So a
    /// snapshot, once taken, WILL land (or be definitely refused) - which is
    /// why the session is marked saved at the moment of the snapshot. If a
    /// save is definitely refused, the session is marked dirty again.
    ///
    /// Three ways in, all tick-thread only:
    ///   SaveInBackground   autosave, level-up, disconnect. Fire and forget.
    ///   SaveNowAsync       write-through: the caller waits for the database
    ///                      before doing something irreversible elsewhere
    ///                      (listing on the auction house, paying for one).
    ///   SaveClaimingMailAsync  the snapshot includes a mail's contents and
    ///                      deletes that mail in the same transaction.
    /// </summary>
    public class CharacterPersistence
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Func<W2PCharacterSaveFullRequest, Task<P2WCharacterSaveFullResponse>> _send;
        private readonly Action<Action> _enqueueOnTickThread;
        private readonly ConcurrentDictionary<long, CharacterSaveChain> _chains = new();

        public CharacterPersistence(PersistenceClient persistence, Action<Action> enqueueOnTickThread)
            : this(request => persistence.W2PCharacterSaveFull.Send(request), enqueueOnTickThread)
        {
        }

        /// <summary>For tests: any send function.</summary>
        public CharacterPersistence(
            Func<W2PCharacterSaveFullRequest, Task<P2WCharacterSaveFullResponse>> send,
            Action<Action> enqueueOnTickThread)
        {
            _send = send;
            _enqueueOnTickThread = enqueueOnTickThread;
        }

        /// <summary>The chain for a character, created on first use. Never removed.</summary>
        public CharacterSaveChain ChainFor(long characterId) =>
            _chains.GetOrAdd(characterId, id => new CharacterSaveChain(id, _send));

        // ── Taking a snapshot ────────────────────────────────────────

        /// <summary>
        /// Everything the database should hold for this character, right
        /// now. Tick thread only. Marks the session saved (see class doc).
        /// </summary>
        public static W2PCharacterSaveFullRequest Capture(PlayerSession session, long claimMailId = 0)
        {
            var request = new W2PCharacterSaveFullRequest
            {
                AccountId   = session.AccountId,
                CharacterId = session.CharacterId,
                Level       = session.Level,
                X           = session.Position.X,
                Y           = session.Position.Y,
                Z           = session.Position.Z,
                ClaimMailId = claimMailId
            };

            // Gold + bag, quest log, and whatever gets added later: each
            // saved component writes its own part (IPersistentComponent).
            foreach (var component in session.PersistentComponents)
                component.WriteTo(request);

            session.MarkSaved();
            return request;
        }

        // ── Saving ───────────────────────────────────────────────────

        /// <summary>Tick thread only. Fire and forget.</summary>
        public void SaveInBackground(PlayerSession session)
        {
            if (session.CharacterId <= 0)
                return;

            _ = SaveAndReportAsync(session, Capture(session), coalescable: true);
        }

        /// <summary>
        /// Tick thread only. Snapshot now and complete once the database has
        /// it: true = saved. Waits as long as it takes (the chain never
        /// guesses) - callers that can't wait put their own timeout on it,
        /// and must treat a timeout as "not known to be saved".
        /// </summary>
        public Task<SaveOutcome> SaveNowAsync(PlayerSession session)
        {
            if (session.CharacterId <= 0)
                return Task.FromResult(SaveOutcome.Failed);

            return SaveAndReportAsync(session, Capture(session), coalescable: true);
        }

        /// <summary>
        /// Tick thread only. The session must ALREADY hold the mail's
        /// contents. Saves it and deletes the mail in one transaction.
        /// MailGone = nothing was written; the caller takes the contents
        /// back out of the session.
        /// </summary>
        public Task<SaveOutcome> SaveClaimingMailAsync(PlayerSession session, long mailId)
        {
            if (session.CharacterId <= 0 || mailId <= 0)
                return Task.FromResult(SaveOutcome.Failed);

            return SaveAndReportAsync(session, Capture(session, mailId), coalescable: false);
        }

        private async Task<SaveOutcome> SaveAndReportAsync(
            PlayerSession session, W2PCharacterSaveFullRequest request, bool coalescable)
        {
            var outcome = await ChainFor(request.CharacterId).Enqueue(request, coalescable);

            if (outcome == SaveOutcome.Saved)
            {
                Logger.Debug($"[Save] CharacterId={request.CharacterId} saved (seq {request.SaveSeq}).");
            }
            else
            {
                // Not written. Make sure the next autosave sends everything
                // again, with whatever the state is by then.
                _enqueueOnTickThread(() => session.HasBeenSaved = false);
            }

            return outcome;
        }

        // ── Login ────────────────────────────────────────────────────

        /// <summary>
        /// Any thread. Completes true once every save queued for this
        /// character has a definite answer - the point after which loading
        /// it returns the latest data. False on timeout.
        /// </summary>
        public Task<bool> WhenSettledAsync(long characterId, TimeSpan timeout) =>
            _chains.TryGetValue(characterId, out var chain)
                ? chain.WhenSettledAsync(timeout)
                : Task.FromResult(true);

        /// <summary>See CharacterSaveChain.IsLoadCurrent.</summary>
        public bool IsLoadCurrent(long characterId, long loadedSeq, out string why)
        {
            if (_chains.TryGetValue(characterId, out var chain))
                return chain.IsLoadCurrent(loadedSeq, out why);

            why = null;
            return true;
        }

        /// <summary>A character was loaded into the world; its saves continue from this seq.</summary>
        public void OnLoaded(long characterId, long loadedSeq) =>
            ChainFor(characterId).SeedFromLoad(loadedSeq);

        // ── Shutdown ─────────────────────────────────────────────────

        /// <summary>
        /// Shutdown only, after the tick loop has stopped (so reading
        /// sessions here is safe). Queues a final save for every dirty
        /// session and waits for EVERY chain - including disconnect saves
        /// still running for players who already left.
        /// </summary>
        public async Task<bool> SaveAllAndWaitAsync(IEnumerable<PlayerSession> sessions, TimeSpan timeout)
        {
            int queued = 0;

            foreach (var session in sessions)
            {
                if (!session.IsDirty || session.CharacterId <= 0)
                    continue;

                _ = ChainFor(session.CharacterId).Enqueue(Capture(session), coalescable: true);
                queued++;
            }

            var busy = _chains.Values.Where(c => c.IsBusy).ToList();

            if (busy.Count == 0)
                return true;

            Logger.Info($"[Shutdown] Saving {queued} character(s); waiting on {busy.Count} save queue(s)...");

            var results = await Task.WhenAll(busy.Select(c => c.WhenSettledAsync(timeout)));
            int unfinished = results.Count(r => !r);

            if (unfinished == 0)
                Logger.Info("[Shutdown] All characters saved.");
            else
                Logger.Error($"[Shutdown] {unfinished} character save queue(s) did not finish within " +
                             $"{timeout.TotalSeconds:F0}s. Those characters lose what happened since their last confirmed save.");

            return unfinished == 0;
        }
    }
}
