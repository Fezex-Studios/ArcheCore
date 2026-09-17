namespace ArcheCore.LoadTester
{
    /// <summary>
    /// Every bot writes into this via Interlocked (never a lock — this gets
    /// touched from every bot's receive callback, and a lock here would
    /// mean the load tester itself becomes the bottleneck it's supposed to
    /// be measuring around).
    ///
    /// Printed once a second by Program.cs. These four numbers are the
    /// ones that matter per INTEGRATION.md:
    ///
    ///   1. Bytes/sec down ÷ connected bots  -> compare against the ~15KB/s
    ///      target from the roadmap doc
    ///   2. Connected vs Spawned            -> a gap that grows means the
    ///      auth/persistence path is the bottleneck, not the world tick
    ///   3. Snapshot inter-arrival time      -> a proxy for tick health.
    ///      Should hover at your tick interval; drift upward means the
    ///      server is falling behind
    ///   4. Disconnects                      -> anything other than zero at
    ///      steady state means you found the ceiling
    /// </summary>
    public sealed class Metrics
    {
        private long _bytesSent;
        private long _bytesReceived;
        private long _packetsReceived;
        private long _connected;
        private long _spawned;
        private long _disconnected;
        private long _authFailures;
        private long _connectTimeouts;

        public void AddSent(int bytes) => Interlocked.Add(ref _bytesSent, bytes);
        public void AddReceived(int bytes)
        {
            Interlocked.Add(ref _bytesReceived, bytes);
            Interlocked.Increment(ref _packetsReceived);
        }

        public void BotConnected() => Interlocked.Increment(ref _connected);
        public void BotSpawned() => Interlocked.Increment(ref _spawned);
        public void BotDisconnected() => Interlocked.Increment(ref _disconnected);
        public void AuthFailed() => Interlocked.Increment(ref _authFailures);
        public void ConnectTimedOut() => Interlocked.Increment(ref _connectTimeouts);

        public readonly record struct Snapshot(
            long BytesSent, long BytesReceived, long PacketsReceived,
            long Connected, long Spawned, long Disconnected, long AuthFailures, long ConnectTimeouts);

        public Snapshot Read() => new(
            Interlocked.Read(ref _bytesSent),
            Interlocked.Read(ref _bytesReceived),
            Interlocked.Read(ref _packetsReceived),
            Interlocked.Read(ref _connected),
            Interlocked.Read(ref _spawned),
            Interlocked.Read(ref _disconnected),
            Interlocked.Read(ref _authFailures),
            Interlocked.Read(ref _connectTimeouts));

        /// <summary>Resets the per-interval counters used for the /sec printout, keeps the gauges.</summary>
        public (long bytesSent, long bytesReceived, long packets) DrainInterval()
        {
            return (
                Interlocked.Exchange(ref _bytesSent, 0),
                Interlocked.Exchange(ref _bytesReceived, 0),
                Interlocked.Exchange(ref _packetsReceived, 0));
        }
    }
}