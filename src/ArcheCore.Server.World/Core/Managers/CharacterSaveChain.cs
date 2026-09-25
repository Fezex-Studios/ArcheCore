using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using NLog;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>What finally happened to one save.</summary>
    public enum SaveOutcome
    {
        /// <summary>In the database.</summary>
        Saved,

        /// <summary>
        /// A save carrying a mail claim was refused because that mail no
        /// longer exists. Nothing was written - not the claim, not the rest.
        /// </summary>
        MailGone,

        /// <summary>
        /// Definitely NOT written (the persistence server refused it or kept
        /// failing). The character is marked dirty again so the next
        /// autosave tries with fresh data.
        /// </summary>
        Failed
    }

    /// <summary>
    /// Every save of ONE character, one at a time, in order.
    ///
    /// Why this exists: saves used to be fire-and-forget, so two of them for
    /// the same character (an autosave and a disconnect save a moment later)
    /// were two HTTP requests racing each other, and whichever the database
    /// saw LAST won - sometimes the older one. Worse, a relog could load the
    /// character before its disconnect save had landed, then play on from
    /// stale data while the late save quietly overwrote... something.
    /// Every item dupe in the audit (C1-C3) came from one of those races.
    ///
    /// The rules this class enforces:
    ///
    ///   1. ONE request in flight per character. The next one starts only
    ///      when the previous one has a definite answer.
    ///
    ///   2. Each request carries a SaveSeq, strictly increasing, and the
    ///      persistence server only writes a higher one than it holds. So a
    ///      request whose answer was lost (a timeout) can simply be SENT
    ///      AGAIN with the same number: if it had landed, the database says
    ///      "Stale, CurrentSeq = yours" and that IS the success answer.
    ///
    ///   3. An unknown outcome is never guessed at. A timeout or a dropped
    ///      connection is retried with the same SaveSeq until the database
    ///      says what happened - however long that takes. While it does,
    ///      later saves queue behind it (coalesced into one), and a login
    ///      for this character waits (see WhenSettledAsync).
    ///
    ///   4. Queued ordinary saves coalesce: if a save is already waiting
    ///      behind the one in flight, a newer one replaces its data instead
    ///      of queueing a second request. The newest state is what counts.
    ///      Mail-claim saves never coalesce - each is its own transaction.
    ///
    /// Chains outlive sessions on purpose: a disconnected character's final
    /// save keeps running after the session is gone, and the next login of
    /// that character finds the same chain and waits on it.
    ///
    /// Thread-safe. Nothing here touches PlayerSession.
    /// </summary>
    public sealed class CharacterSaveChain
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>Waits between attempts when the outcome is unknown. The last one repeats.</summary>
        private static readonly TimeSpan[] RetryDelays =
        {
            TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)
        };

        /// <summary>
        /// A 5xx means the transaction rolled back, so retrying is safe - but
        /// an ordinary save that keeps failing that way is given up after
        /// this many tries rather than blocking the character for ever. A
        /// mail claim never gives up (see RunJobAsync).
        /// </summary>
        private const int MaxServerErrorsForOrdinarySave = 20;

        public long CharacterId { get; }

        private readonly Func<W2PCharacterSaveFullRequest, Task<P2WCharacterSaveFullResponse>> _send;
        private readonly Func<TimeSpan, Task> _delay;

        private readonly object _gate = new();
        private readonly LinkedList<Job> _queue = new();
        private bool _running;
        private long _lastIssuedSeq;
        private long _lastConfirmedSeq;
        private TaskCompletionSource<bool> _idle;

        private sealed class Job
        {
            public W2PCharacterSaveFullRequest Request;
            public bool Coalescable;
            public readonly TaskCompletionSource<SaveOutcome> Done =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public CharacterSaveChain(
            long characterId,
            Func<W2PCharacterSaveFullRequest, Task<P2WCharacterSaveFullResponse>> send,
            Func<TimeSpan, Task> delay = null)
        {
            CharacterId = characterId;
            _send = send;
            _delay = delay ?? (d => Task.Delay(d));
        }

        /// <summary>Highest SaveSeq known to be in the database.</summary>
        public long LastConfirmedSeq { get { lock (_gate) return _lastConfirmedSeq; } }

        /// <summary>True while a save is in flight or queued.</summary>
        public bool IsBusy { get { lock (_gate) return _running; } }

        /// <summary>
        /// A character was just loaded with this save_seq. Later saves
        /// continue from it. Never moves anything backwards.
        /// </summary>
        public void SeedFromLoad(long loadedSeq)
        {
            lock (_gate)
            {
                if (loadedSeq > _lastIssuedSeq)    _lastIssuedSeq = loadedSeq;
                if (loadedSeq > _lastConfirmedSeq) _lastConfirmedSeq = loadedSeq;
            }
        }

        /// <summary>
        /// Is a load that saw <paramref name="loadedSeq"/> the latest data?
        /// Not if a save is still running, and not if a save newer than it
        /// has already been confirmed.
        /// </summary>
        public bool IsLoadCurrent(long loadedSeq, out string why)
        {
            lock (_gate)
            {
                if (_running)
                {
                    why = "a save for this character is still in progress";
                    return false;
                }

                if (loadedSeq < _lastConfirmedSeq)
                {
                    why = $"the load (save_seq {loadedSeq}) is older than a confirmed save ({_lastConfirmedSeq})";
                    return false;
                }

                why = null;
                return true;
            }
        }

        /// <summary>
        /// Queue a save. <paramref name="request"/>.SaveSeq is assigned here,
        /// when it is actually sent. A coalescable save may replace the data
        /// of one already waiting; the returned task then completes with
        /// that shared save's outcome.
        /// </summary>
        public Task<SaveOutcome> Enqueue(W2PCharacterSaveFullRequest request, bool coalescable)
        {
            bool start = false;
            Task<SaveOutcome> result;

            lock (_gate)
            {
                var last = _queue.Last?.Value;

                if (coalescable && last is { Coalescable: true })
                {
                    last.Request = request;
                    result = last.Done.Task;
                }
                else
                {
                    var job = new Job { Request = request, Coalescable = coalescable };
                    _queue.AddLast(job);
                    result = job.Done.Task;
                }

                if (!_running)
                {
                    _running = true;
                    start = true;
                }
            }

            if (start)
                _ = Task.Run(PumpAsync);

            return result;
        }

        /// <summary>
        /// Completes true once nothing is queued or in flight, false if that
        /// didn't happen within <paramref name="timeout"/>.
        /// </summary>
        public async Task<bool> WhenSettledAsync(TimeSpan timeout)
        {
            Task<bool> idle;

            lock (_gate)
            {
                if (!_running)
                    return true;

                _idle ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                idle = _idle.Task;
            }

            if (timeout == Timeout.InfiniteTimeSpan)
                return await idle;

            var finished = await Task.WhenAny(idle, Task.Delay(timeout));
            return finished == idle;
        }

        private async Task PumpAsync()
        {
            while (true)
            {
                Job job;

                lock (_gate)
                {
                    if (_queue.Count == 0)
                    {
                        _running = false;
                        _idle?.TrySetResult(true);
                        _idle = null;
                        return;
                    }

                    job = _queue.First.Value;
                    _queue.RemoveFirst();
                }

                SaveOutcome outcome;

                try
                {
                    outcome = await RunJobAsync(job.Request, claim: job.Request.ClaimMailId > 0);
                }
                catch (Exception ex)
                {
                    // RunJobAsync doesn't throw; this is a last line so the
                    // pump can never die and leave the chain "running" for ever.
                    Logger.Error(ex, $"[Save] CharacterId={CharacterId}: save pump error.");
                    outcome = SaveOutcome.Failed;
                }

                job.Done.TrySetResult(outcome);
            }
        }

        private async Task<SaveOutcome> RunJobAsync(W2PCharacterSaveFullRequest request, bool claim)
        {
            long seq;
            lock (_gate) seq = ++_lastIssuedSeq;
            request.SaveSeq = seq;

            int attempt = 0;
            int serverErrors = 0;

            while (true)
            {
                if (attempt > 0)
                    await _delay(RetryDelays[Math.Min(attempt - 1, RetryDelays.Length - 1)]);

                attempt++;

                P2WCharacterSaveFullResponse response;

                try
                {
                    response = await _send(request);
                }
                catch (HttpRequestException ex) when (ex.StatusCode is not null)
                {
                    // The server ANSWERED, with an error. It commits last, so
                    // an error answer means nothing was written: a retry is
                    // safe, and giving up is safe.
                    int status = (int)ex.StatusCode.Value;

                    if (status >= 500 && (claim || ++serverErrors < MaxServerErrorsForOrdinarySave))
                    {
                        if (attempt == 1 || attempt % 10 == 0)
                            Logger.Warn($"[Save] CharacterId={CharacterId} seq {seq}: persistence answered {status}; retrying.");
                        continue;
                    }

                    Logger.Error($"[Save] FAILED CharacterId={CharacterId} seq {seq}: persistence answered {status}. " +
                                 "Nothing was written; the character is marked dirty for the next autosave.");
                    return SaveOutcome.Failed;
                }
                catch (Exception ex)
                {
                    // Timeout, refused or dropped connection: it may or may
                    // not have been written. Ask again with the SAME seq -
                    // the answer will tell us.
                    if (attempt == 1 || attempt % 10 == 0)
                        Logger.Warn($"[Save] CharacterId={CharacterId} seq {seq}: no answer ({ex.GetType().Name}: {ex.Message}); " +
                                    $"asking again (attempt {attempt}).");
                    continue;
                }

                if (response is null)
                {
                    Logger.Warn($"[Save] CharacterId={CharacterId} seq {seq}: empty answer; asking again.");
                    continue;
                }

                switch (response.Result)
                {
                    case CharacterSaveResult.Saved:
                        Confirm(seq);
                        if (attempt > 1)
                            Logger.Info($"[Save] CharacterId={CharacterId} seq {seq} saved after {attempt} attempts.");
                        return SaveOutcome.Saved;

                    case CharacterSaveResult.Stale when response.CurrentSeq == seq:
                        // This exact request landed on an earlier attempt
                        // whose answer we never got.
                        Confirm(seq);
                        Logger.Info($"[Save] CharacterId={CharacterId} seq {seq} had already been saved (answer was lost).");
                        return SaveOutcome.Saved;

                    case CharacterSaveResult.Stale:
                        // The database holds a NEWER save than any we sent -
                        // something else is writing this character (two world
                        // servers pointed at one database?). Continue after
                        // it so this live session's state isn't lost, but say
                        // so loudly: that's a deployment problem.
                        Logger.Error($"[Save] CharacterId={CharacterId}: database is at save_seq {response.CurrentSeq}, " +
                                     $"ahead of this server's {seq}. Is another world server writing this character?");
                        lock (_gate)
                        {
                            if (response.CurrentSeq > _lastIssuedSeq) _lastIssuedSeq = response.CurrentSeq;
                            if (response.CurrentSeq > _lastConfirmedSeq) _lastConfirmedSeq = response.CurrentSeq;
                            seq = ++_lastIssuedSeq;
                        }
                        request.SaveSeq = seq;
                        attempt = 0;
                        continue;

                    case CharacterSaveResult.MailGone:
                        return SaveOutcome.MailGone;

                    case CharacterSaveResult.NotFound:
                        Logger.Error($"[Save] FAILED CharacterId={CharacterId}: persistence has no such character " +
                                     $"for AccountId={request.AccountId}.");
                        return SaveOutcome.Failed;

                    default:
                        Logger.Error($"[Save] FAILED CharacterId={CharacterId} seq {seq}: persistence rejected the save " +
                                     $"as {response.Result} (see the persistence log for why).");
                        return SaveOutcome.Failed;
                }
            }
        }

        private void Confirm(long seq)
        {
            lock (_gate)
            {
                if (seq > _lastConfirmedSeq)
                    _lastConfirmedSeq = seq;
            }
        }
    }
}
