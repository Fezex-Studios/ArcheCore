using System;
using System.Collections.Generic;
using LiteNetLib;

namespace ArcheCore.Server.World.Core.Services.Authservice
{
    /// <summary>
    /// Rate limits C2W Authenticate per peer.
    ///
    /// WHY THIS EXISTS
    ///
    /// The authenticate handler kicks off an outbound HTTP call to the
    /// AuthServer and returns immediately (fire-and-forget). With no gate,
    /// one connected peer spamming opcode 6 turns a cheap UDP send into an
    /// unbounded number of in-flight HTTP requests against your own auth
    /// server — a peer costing one packet each makes the WorldServer do all
    /// the work, and the AuthServer takes the damage. That's an
    /// amplification factor, not a fair fight.
    ///
    /// Two separate limits, because they stop two different things:
    ///
    ///  - IN FLIGHT. At most one validation per peer at a time. This alone
    ///    caps concurrent HTTP fan-out at (connected peers), which is
    ///    already bounded by MaxPlayers.
    ///  - ATTEMPTS PER WINDOW. Stops a peer from serially retrying forever
    ///    with guessed tokens. Exceeding it disconnects rather than just
    ///    dropping, because a client with a valid token never needs more
    ///    than one or two attempts, so a peer at the limit is either broken
    ///    or hostile and there's nothing useful left to say to it.
    ///
    /// NOT THREAD SAFE. Only ever touched from the tick thread inside
    /// PollEvents, same as every other C2W handler.
    /// </summary>
    public sealed class AuthThrottle
    {
        /// <summary>Attempts allowed per peer within Window.</summary>
        public int MaxAttempts { get; init; } = 5;

        /// <summary>Rolling window for MaxAttempts.</summary>
        public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);

        private readonly Dictionary<int, Entry> _peers = new();
        private TimeSpan _lastPrune = ServerClock.Elapsed;

        private struct Entry
        {
            public int      Attempts;
            public TimeSpan WindowStart;
            public bool     InFlight;
        }

        public enum Decision
        {
            /// <summary>Proceed with validation.</summary>
            Allow,

            /// <summary>A validation for this peer is already running — ignore this packet.</summary>
            AlreadyInFlight,

            /// <summary>Too many attempts in the window — caller should disconnect.</summary>
            TooManyAttempts
        }

        public Decision TryBegin(NetPeer peer)
        {
            PruneIfDue();

            var now = ServerClock.Elapsed;

            if (!_peers.TryGetValue(peer.Id, out var entry))
                entry = new Entry { WindowStart = now };

            if (entry.InFlight)
                return Decision.AlreadyInFlight;

            if (now - entry.WindowStart > Window)
            {
                entry.WindowStart = now;
                entry.Attempts    = 0;
            }

            if (entry.Attempts >= MaxAttempts)
            {
                _peers[peer.Id] = entry;
                return Decision.TooManyAttempts;
            }

            entry.Attempts++;
            entry.InFlight  = true;
            _peers[peer.Id] = entry;

            return Decision.Allow;
        }

        /// <summary>
        /// Call when the validation finishes, however it finished. The
        /// attempt count is deliberately NOT reset on success — a peer that
        /// authenticates doesn't need to authenticate again.
        /// </summary>
        public void Complete(NetPeer peer)
        {
            if (!_peers.TryGetValue(peer.Id, out var entry))
                return;

            entry.InFlight  = false;
            _peers[peer.Id] = entry;
        }

        public void Forget(NetPeer peer) => _peers.Remove(peer.Id);

        /// <summary>
        /// Lazy cleanup so this doesn't need a disconnect hook to avoid
        /// growing forever. LiteNetLib reuses peer ids, and a reused id
        /// inheriting a stale window would only ever be stricter than a
        /// fresh one, never looser — so dropping entries on age is safe.
        /// </summary>
        private void PruneIfDue()
        {
            var now = ServerClock.Elapsed;
            if (now - _lastPrune < TimeSpan.FromMinutes(5))
                return;

            _lastPrune = now;

            var stale = new List<int>();
            foreach (var (id, entry) in _peers)
            {
                if (!entry.InFlight && now - entry.WindowStart > Window)
                    stale.Add(id);
            }

            for (int i = 0; i < stale.Count; i++)
                _peers.Remove(stale[i]);
        }
    }
}
