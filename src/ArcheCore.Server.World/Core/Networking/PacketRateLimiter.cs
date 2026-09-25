using System;
using System.Collections.Generic;
using System.Diagnostics;
using ArcheCore.Library.Net.Worldserver;
using NLog;

namespace ArcheCore.Server.World.Networking
{
    /// <summary>
    /// Per-peer, per-opcode token buckets (audit H3).
    ///
    /// Every packet costs one token from its opcode's bucket. A bucket refills
    /// at <see cref="Rule.PerSecond"/> and holds at most <see cref="Rule.Burst"/>,
    /// so a player can do things in quick bursts but can't sustain more than
    /// the rate. A packet with no token is DROPPED before its handler runs -
    /// which is what stops one client turning AuctionBrowse into a flood of
    /// HTTP calls to the auction service, or spamming chat.
    ///
    /// Dropping is silent to the client: a legitimate client never gets near
    /// these limits, and telling a flooder "slow down" is just more traffic.
    /// A peer that keeps flooding (many drops in a short window) is
    /// disconnected - that's a bot or a modified client, not a player.
    ///
    /// Tick thread only (OnNetworkReceive runs inside PollEvents).
    /// </summary>
    public sealed class PacketRateLimiter
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public readonly struct Rule
        {
            public readonly float PerSecond;
            public readonly float Burst;
            public Rule(float perSecond, float burst) { PerSecond = perSecond; Burst = burst; }
        }

        /// <summary>Anything not listed below.</summary>
        public static readonly Rule Default = new(10f, 20f);

        /// <summary>
        /// Chosen well above what the real client sends, so only a flood
        /// trips them. If a new feature legitimately sends faster, raise its
        /// line here - a dropped packet is a bug report nobody files.
        /// </summary>
        private static readonly Dictionary<Opcodes, Rule> Rules = new()
        {
            // Movement: the client sends at 20Hz. Headroom for jitter bunching.
            [Opcodes.PlayerMove]                = new(40f, 80f),

            // Login flow.
            [Opcodes.Authenticate]              = new(0.5f, 3f),
            [Opcodes.C2WSelectCharacter]        = new(0.5f, 3f),
            [Opcodes.C2WCreateCharacterRequest] = new(0.5f, 3f),

            // Chat.
            [Opcodes.ChatMessage]               = new(2f, 6f),

            // Gameplay.
            [Opcodes.Interact]                  = new(6f, 12f),
            [Opcodes.C2WAttack]                 = new(10f, 20f),
            [Opcodes.C2WMoveItem]               = new(10f, 25f),
            [Opcodes.C2WDropItem]               = new(5f, 10f),
            [Opcodes.C2WUseItem]                = new(8f, 16f),
            [Opcodes.C2WShopBuy]                = new(6f, 15f),
            [Opcodes.C2WShopSell]               = new(6f, 15f),
            [Opcodes.C2WLootTake]               = new(10f, 20f),
            [Opcodes.C2WRespawn]                = new(1f, 3f),
            [Opcodes.C2WQuestAccept]            = new(3f, 8f),
            [Opcodes.C2WQuestComplete]          = new(3f, 8f),
            [Opcodes.C2WQuestAbandon]           = new(3f, 8f),

            // Market: every one of these becomes an HTTP request to another
            // service, so they're the tightest.
            [Opcodes.C2WAuctionBrowse]          = new(1f, 4f),
            [Opcodes.C2WAuctionCreate]          = new(1f, 3f),
            [Opcodes.C2WAuctionBuy]             = new(1f, 3f),
            [Opcodes.C2WAuctionCancel]          = new(1f, 3f),
            [Opcodes.C2WMailClaim]              = new(1f, 4f),
            [Opcodes.C2WCashShopBrowse]         = new(1f, 3f),
            [Opcodes.C2WCashShopBuy]            = new(0.5f, 2f),
            [Opcodes.C2WCashShopGift]           = new(0.5f, 2f),

            // Cheap lookups the client makes in batches.
            [Opcodes.ItemRequestData]           = new(20f, 60f),
            [Opcodes.RequestPlayerLevel]        = new(2f, 5f),
        };

        /// <summary>Drops within <see cref="FloodWindowSeconds"/> that get a peer disconnected.</summary>
        public int FloodDropLimit { get; set; } = 200;
        public double FloodWindowSeconds { get; set; } = 10;

        private sealed class Bucket
        {
            public float Tokens;
            public long LastTicks;
        }

        private sealed class PeerState
        {
            public readonly Dictionary<Opcodes, Bucket> Buckets = new();
            public int DropsInWindow;
            public long WindowStartTicks;
            public long LastLogTicks;
        }

        private readonly Dictionary<int, PeerState> _peers = new();
        private readonly Func<long> _now;
        private static readonly double TicksToSeconds = 1.0 / Stopwatch.Frequency;

        public PacketRateLimiter() : this(Stopwatch.GetTimestamp) { }

        /// <summary>For tests: a clock in Stopwatch ticks.</summary>
        public PacketRateLimiter(Func<long> now) => _now = now;

        public enum Verdict { Allow, Drop, Disconnect }

        public static Rule RuleFor(Opcodes opcode) =>
            Rules.TryGetValue(opcode, out var rule) ? rule : Default;

        /// <summary>Spend one token for this packet. Tick thread only.</summary>
        public Verdict Check(int peerId, Opcodes opcode, string peerLabel = null)
        {
            long now = _now();

            if (!_peers.TryGetValue(peerId, out var peer))
                _peers[peerId] = peer = new PeerState { WindowStartTicks = now };

            var rule = RuleFor(opcode);

            if (!peer.Buckets.TryGetValue(opcode, out var bucket))
                peer.Buckets[opcode] = bucket = new Bucket { Tokens = rule.Burst, LastTicks = now };

            float elapsed = (float)((now - bucket.LastTicks) * TicksToSeconds);
            bucket.LastTicks = now;
            bucket.Tokens = Math.Min(rule.Burst, bucket.Tokens + elapsed * rule.PerSecond);

            if (bucket.Tokens >= 1f)
            {
                bucket.Tokens -= 1f;
                return Verdict.Allow;
            }

            // Dropped. Count it toward the flood window.
            if ((now - peer.WindowStartTicks) * TicksToSeconds > FloodWindowSeconds)
            {
                peer.WindowStartTicks = now;
                peer.DropsInWindow = 0;
            }

            peer.DropsInWindow++;

            if (peer.DropsInWindow >= FloodDropLimit)
            {
                Logger.Warn($"[RateLimit] {peerLabel ?? peerId.ToString()} flooding ({peer.DropsInWindow} packets over the limit " +
                            $"in {FloodWindowSeconds:F0}s, last {opcode}) - disconnecting.");
                _peers.Remove(peerId);
                return Verdict.Disconnect;
            }

            // At most one log line per peer per 5s.
            if ((now - peer.LastLogTicks) * TicksToSeconds > 5)
            {
                peer.LastLogTicks = now;
                Logger.Info($"[RateLimit] {peerLabel ?? peerId.ToString()}: dropping {opcode} " +
                            $"(limit {rule.PerSecond}/s, burst {rule.Burst}).");
            }

            return Verdict.Drop;
        }

        /// <summary>Forget a peer's buckets. Call on disconnect.</summary>
        public void Forget(int peerId) => _peers.Remove(peerId);

        public int TrackedPeers => _peers.Count;
    }
}
