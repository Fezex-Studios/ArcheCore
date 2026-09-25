using System.Diagnostics;

namespace ArcheCore.Server.Auction.Services;

/// <summary>
/// The line between the endpoints and the MailDispatcher.
///
/// A sale queues its mail in the outbox and then calls NudgeAsync: the
/// dispatcher wakes at once instead of waiting for its next lap, and the
/// caller waits (briefly) until a delivery pass that STARTED AFTER the nudge
/// has finished. So by the time the buyer is told "bought", their item is
/// normally already in their mailbox.
///
/// It's a nudge, not a guarantee: if the persistence server is slow or down,
/// the wait gives up after a moment and the mail simply follows when the
/// dispatcher gets through. Nothing depends on the wait succeeding.
/// </summary>
public class MailSignal
{
    private readonly SemaphoreSlim _wake = new(0, 1);
    private long _passesStarted;
    private long _passesFinished;

    // ── Dispatcher side ──────────────────────────────────────────────

    /// <summary>Sleep until nudged, or until the idle time is up - whichever comes first.</summary>
    public Task<bool> WaitForWorkAsync(TimeSpan idle, CancellationToken stop) => _wake.WaitAsync(idle, stop);

    public void PassStarted() => Interlocked.Increment(ref _passesStarted);
    public void PassFinished() => Interlocked.Increment(ref _passesFinished);

    // ── Endpoint side ────────────────────────────────────────────────

    /// <summary>Wake the dispatcher, without waiting for it.</summary>
    public void Nudge()
    {
        try { _wake.Release(); }
        catch (SemaphoreFullException) { /* already has a wake-up pending */ }
    }

    /// <summary>
    /// Wake the dispatcher and wait until a pass that began after this call
    /// has finished, or <paramref name="maxWait"/> runs out.
    /// </summary>
    public async Task NudgeAsync(TimeSpan maxWait)
    {
        // A pass that is running right now may have read the outbox before the
        // caller's rows were committed, so the pass we need is the NEXT one.
        long target = Interlocked.Read(ref _passesStarted) + 1;

        Nudge();

        var clock = Stopwatch.StartNew();
        while (Interlocked.Read(ref _passesFinished) < target && clock.Elapsed < maxWait)
            await Task.Delay(10);
    }
}
