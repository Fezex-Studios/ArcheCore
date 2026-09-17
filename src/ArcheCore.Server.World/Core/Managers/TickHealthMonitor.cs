namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Answers "is the server itself okay?" without relying on anything
    /// client-observed — the load tester can lie about server health (as
    /// it did: it plateaued from its own thread-pool starvation while
    /// WorldServer's own logs showed nothing wrong). This is the
    /// server's own opinion of itself, logged periodically regardless of
    /// what any client sees.
    ///
    /// Call Record(elapsedMs) once per tick, right after the tick's work
    /// finishes. Every ReportEveryNTicks (default 100 = 5s at 20Hz) it
    /// logs a one-line summary and resets its window — cheap enough to
    /// run unconditionally in production, not just during load tests.
    /// </summary>
    public sealed class TickHealthMonitor
    {
        private readonly int _reportEveryNTicks;
        private readonly double _tickIntervalMs;
        private readonly Action<string> _log;

        private readonly List<double> _samples;
        private int _missedCount;
        private long _lastGen2Count;

        public TickHealthMonitor(int tickRateHz, Action<string> log, int reportEveryNTicks = 0)
        {
            _tickIntervalMs = 1000.0 / tickRateHz;
            _log = log;
            _reportEveryNTicks = reportEveryNTicks > 0 ? reportEveryNTicks : tickRateHz * 5; // ~5s window
            _samples = new List<double>(capacity: _reportEveryNTicks);
            _lastGen2Count = GC.CollectionCount(2);
        }

        public void Record(double elapsedMs)
        {
            _samples.Add(elapsedMs);
            if (elapsedMs > _tickIntervalMs) _missedCount++;

            if (_samples.Count < _reportEveryNTicks) return;

            _samples.Sort();
            var p50 = _samples[_samples.Count / 2];
            var p99 = _samples[(int)(_samples.Count * 0.99)];
            var max = _samples[^1];

            var gen2Now = GC.CollectionCount(2);
            var gen2Delta = gen2Now - _lastGen2Count;
            _lastGen2Count = gen2Now;

            // This line is the ground truth. If p99 is comfortably under
            // the tick interval and gen2 stays at 0, the SIMULATION is
            // healthy — full stop, regardless of what any client-side
            // tool reports. If p99 creeps toward or past the interval,
            // or missed climbs, THAT's a real server-side ceiling, not a
            // test-rig artifact.
            _log($"[TickHealth] p50={p50:F2}ms p99={p99:F2}ms max={max:F2}ms " +
                 $"missed={_missedCount}/{_samples.Count} gen2GC={gen2Delta} " +
                 $"target={_tickIntervalMs:F1}ms");

            _samples.Clear();
            _missedCount = 0;
        }
    }
}