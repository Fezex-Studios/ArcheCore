using System;
using System.Diagnostics;

namespace ArcheCore.Server.World
{
    /// <summary>
    /// The one clock for game timers (audit M5).
    ///
    /// Monotonic - it never jumps when the machine's wall clock is changed
    /// or synced - and the same everywhere: cooldowns, corpse expiry,
    /// harvest timers, NPC AI, respawns, spawner grace, movement validation.
    /// These used to mix DateTime.UtcNow (NPC AI, spawners), which a clock
    /// change moves, with Environment.TickCount64 (cooldowns) and Stopwatch
    /// (the validator), so two timers in one feature could disagree.
    ///
    /// Only for durations inside this process. Anything stored or shared
    /// with another service (auction listing expiry) stays on wall-clock
    /// UTC, because another process has to agree on it.
    ///
    /// The roadmap's Scheduler should run on this, in ticks.
    /// </summary>
    public static class ServerClock
    {
        private static readonly Stopwatch Watch = Stopwatch.StartNew();

        /// <summary>Time since the server started.</summary>
        public static TimeSpan Elapsed => Watch.Elapsed;

        /// <summary>Milliseconds since the server started. Replaces Environment.TickCount64 for timers.</summary>
        public static long NowMs => Watch.ElapsedMilliseconds;

        /// <summary>Seconds since the server started, high resolution.</summary>
        public static double NowSeconds => Watch.Elapsed.TotalSeconds;
    }
}
