namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// The tick loop (WorldServer.RunTickLoopAsync) is the only writer,
    /// once per tick, before PollEvents. Everything that runs during
    /// PollEvents - movement handlers, PlayerMovementBroadcaster - reads
    /// it to timestamp SnapshotDispatcher.SetTransform calls without
    /// needing "current tick" threaded through every method signature
    /// between WorldServer and PlayerMovementBroadcaster.
    ///
    /// Not thread-safe by design - single writer, single-threaded tick
    /// loop, same rule as everything else touching InterestManager.
    /// </summary>
    public sealed class TickClock
    {
        public uint Current { get; private set; }
        public void Advance(uint tick) => Current = tick;
    }
}