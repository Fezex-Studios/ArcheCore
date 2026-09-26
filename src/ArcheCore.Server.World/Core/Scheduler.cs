using System;
using System.Collections.Generic;
using NLog;

namespace ArcheCore.Server.World
{
    /// <summary>
    /// The one timer system (roadmap fix-first #2).
    ///
    ///     var h = scheduler.In(12_000, () => Expire(corpse));   // "expires in 12 seconds"
    ///     scheduler.Cancel(h);                                  // looted early
    ///     scheduler.Every(60_000, SweepAuctions);               // repeating
    ///
    /// Before this there were hand-rolled timers in five places (corpse
    /// expiry, NPC respawns, harvest completion, node respawns, the auction
    /// sweep), each with its own list, its own per-tick sweep and its own
    /// idea of "now". Phase 3 status effects would have added one more for
    /// every buff, debuff and DoT.
    ///
    /// Times are ServerClock milliseconds (monotonic). Callbacks run on the
    /// TICK THREAD, from RunDue, which WorldServer calls once per tick - so
    /// a callback may touch sessions, the grid and anything else tick-owned
    /// without locks. Resolution is one tick (50ms at 20Hz): "in 100ms"
    /// fires on the first tick at or after it's due, never early.
    ///
    /// Cooldowns deliberately do NOT use this. A cooldown is a "ready at"
    /// timestamp that's checked when you press the key - nothing has to
    /// happen when it runs out, so there's nothing to schedule.
    ///
    /// Not persisted across restarts. Things that must survive a bounce
    /// (a siege at 8pm Saturday - Phase 10) will store their due time and
    /// re-schedule on boot.
    ///
    /// Tick thread only. Not thread-safe by design; code on another thread
    /// hops over with PlayerManager.EnqueueAction first.
    /// </summary>
    public sealed class Scheduler
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>A scheduled callback. Default (Id 0) is "nothing scheduled".</summary>
        public readonly struct Handle : IEquatable<Handle>
        {
            internal readonly long Id;
            internal Handle(long id) => Id = id;

            public bool IsValid => Id != 0;
            public static readonly Handle None = default;

            public bool Equals(Handle other) => Id == other.Id;
            public override bool Equals(object obj) => obj is Handle h && h.Id == Id;
            public override int GetHashCode() => Id.GetHashCode();
            public override string ToString() => IsValid ? $"#{Id}" : "none";
        }

        private sealed class Entry
        {
            public long Id;
            public long DueMs;
            public long IntervalMs;   // > 0 = repeating
            public Action Action;
            public string Label;
        }

        private readonly PriorityQueue<Entry, (long Due, long Id)> _queue = new();
        private readonly Dictionary<long, Entry> _live = new();
        private readonly Func<long> _clock;
        private long _nextId = 1;

        public Scheduler() : this(() => ServerClock.NowMs) { }

        /// <summary>For tests: any millisecond clock.</summary>
        public Scheduler(Func<long> clockMs) => _clock = clockMs;

        public long NowMs => _clock();

        /// <summary>Callbacks waiting to run.</summary>
        public int Count => _live.Count;

        /// <summary>Run once at an absolute ServerClock time.</summary>
        public Handle At(long dueMs, Action action, string label = null) =>
            Add(dueMs, 0, action, label);

        /// <summary>Run once after a delay.</summary>
        public Handle In(long delayMs, Action action, string label = null) =>
            Add(_clock() + Math.Max(0, delayMs), 0, action, label);

        public Handle In(TimeSpan delay, Action action, string label = null) =>
            In((long)delay.TotalMilliseconds, action, label);

        /// <summary>Run every intervalMs (first time after one interval) until cancelled.</summary>
        public Handle Every(long intervalMs, Action action, string label = null)
        {
            if (intervalMs < 1)
                throw new ArgumentOutOfRangeException(nameof(intervalMs), "A repeating timer needs an interval of at least 1ms.");

            return Add(_clock() + intervalMs, intervalMs, action, label);
        }

        /// <summary>Stop a callback that hasn't run yet (or a repeating one). False if it already ran or was cancelled.</summary>
        public bool Cancel(Handle handle) =>
            handle.IsValid && _live.Remove(handle.Id);

        public bool IsScheduled(Handle handle) =>
            handle.IsValid && _live.ContainsKey(handle.Id);

        /// <summary>
        /// Run everything due. Once per tick, from the tick loop. A callback
        /// that throws is logged and does not stop the others (a repeating
        /// one keeps repeating).
        /// </summary>
        public int RunDue()
        {
            long now = _clock();
            int ran = 0;

            while (_queue.TryPeek(out var entry, out var key) && key.Due <= now)
            {
                _queue.Dequeue();

                // Cancelled: its id is no longer live. (Lazy removal - the
                // queue can't delete from the middle.)
                if (!_live.TryGetValue(entry.Id, out var liveEntry) || !ReferenceEquals(liveEntry, entry))
                    continue;

                if (entry.IntervalMs > 0)
                {
                    // Next run from when it was DUE, not from now, so a
                    // repeating timer doesn't drift by a tick every time.
                    entry.DueMs += entry.IntervalMs;
                    if (entry.DueMs <= now) entry.DueMs = now + entry.IntervalMs;   // way behind: don't burst
                    _queue.Enqueue(entry, (entry.DueMs, entry.Id));
                }
                else
                {
                    _live.Remove(entry.Id);
                }

                try
                {
                    entry.Action();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"[Scheduler] Callback {entry.Label ?? "#" + entry.Id} threw - continuing.");
                }

                ran++;
            }

            return ran;
        }

        private Handle Add(long dueMs, long intervalMs, Action action, string label)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var entry = new Entry { Id = _nextId++, DueMs = dueMs, IntervalMs = intervalMs, Action = action, Label = label };
            _live[entry.Id] = entry;
            _queue.Enqueue(entry, (dueMs, entry.Id));
            return new Handle(entry.Id);
        }
    }
}
