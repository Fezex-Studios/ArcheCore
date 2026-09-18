using System;
using System.Collections.Generic;
using System.Numerics;
using ArcheCore.Server.World.Managers;
using LiteNetLib;

namespace ArcheCore.Server.World.Replication
{
    /// <summary>
    /// Replaces the send-immediately-per-observer path in
    /// PlayerMovementBroadcaster.
    ///
    /// The old flow was: a movement packet arrives, and the server
    /// synchronously sends one datagram to every observer of that player.
    /// One player moving at 10Hz with 100 observers = 1,000 datagrams per
    /// second from one mover. Scale that to 20k movers and the syscall
    /// count alone is unservable, before any bandwidth is considered.
    ///
    /// The new flow inverts the loop. Movement updates a transform in a
    /// flat store and nothing is sent. Once per tick, the dispatcher walks
    /// OBSERVERS (not movers) and builds exactly ONE datagram per observer
    /// containing every entity that observer needs an update for. 20k
    /// players at 10Hz is 200k datagrams/sec regardless of how many
    /// entities are moving, and that is a number a single box can do.
    ///
    /// Three mechanisms keep the per-datagram cost bounded:
    ///
    /// LOD TIERS. An entity 200 units away does not need 10Hz. Near gets
    /// every tick, mid gets every 3rd, far gets every 10th. This is the
    /// single biggest bandwidth lever after quantization, because the
    /// entity count in a crowded hub grows with the SQUARE of radius while
    /// the perceptual value of an update falls off just as fast.
    ///
    /// PHASE SPREADING. Rather than tracking last-sent-tick per
    /// observer/entity PAIR (which is O(n^2) memory - unusable at 20k), the
    /// decision is (tick + networkId) % interval == 0. Deterministic, zero
    /// state, and it naturally staggers which entities update on which
    /// tick so you don't get a sawtooth of huge packets every Nth tick.
    ///
    /// OBSERVER CAP. Hard ceiling on entities per snapshot, sorted by
    /// distance. A siege or a capital-city hub will exceed any AOI radius
    /// you pick; the cap is what stops one crowded zone from taking the
    /// whole shard down with it. Entities past the cap simply aren't in
    /// this tick's packet - they still exist, are still spawned on the
    /// client, and will come back into the cut as the crowd shifts.
    ///
    /// SPAWNS AND DESPAWNS DO NOT GO THROUGH HERE. They stay on the
    /// existing reliable W2C senders. This packet is unreliable and may be
    /// dropped, so it must only ever carry state the client can miss
    /// without desyncing.
    ///
    /// ADDED: velocity, stored alongside position and written only for
    /// NEAR-TIER entries. The client uses it to extrapolate through a
    /// dropped or late snapshot instead of freezing the remote character
    /// until the next one lands. Restricting it to the near tier is the
    /// whole reason it's affordable: a mid-tier entity updates every 3rd
    /// tick and a far-tier one every 10th, and at those rates the client
    /// gets a real position back before extrapolation would have earned
    /// its three bytes. Near-tier entries are 15 bytes, everything else
    /// stays at 12.
    /// </summary>
    public sealed class SnapshotDispatcher
    {
        // -- Tuning. These are the knobs you turn after load testing. --

        /// <summary>Full rate. Every tick.</summary>
        public float NearRange = 30f;
        public int   NearInterval = 1;

        /// <summary>Reduced rate. Interpolation covers the gap.</summary>
        public float MidRange = 80f;
        public int   MidInterval = 3;

        /// <summary>Background rate. Mostly "still there, roughly here".</summary>
        public int   FarInterval = 10;

        /// <summary>
        /// Max entities in one observer's snapshot. 150 is a reasonable
        /// starting point; profile in your densest hub and adjust. Note
        /// this interacts with the MTU budget - 150 entities x 12-15 bytes
        /// is well over a 900-byte budget, so in practice the writer's fill
        /// limit bites first and the cap is a cheap pre-filter that keeps
        /// the sort small.
        /// </summary>
        public int MaxEntitiesPerSnapshot = 150;

        private readonly SessionManager _sessions;
        private readonly InterestManager _interest;
        private readonly ushort _snapshotOpcode;

        /// <summary>
        /// Flat transform store for every replicated entity, players and
        /// NPCs alike. Deliberately NOT reaching into PlayerSession /
        /// NpcEntity per entity per observer - at target load that's tens
        /// of millions of pointer-chasing dictionary lookups a second
        /// across two different managers.
        /// </summary>
        private readonly Dictionary<int, Transform> _transforms = new(capacity: 32_768);

        // Scratch, reused every observer every tick. Never allocate in the
        // flush loop.
        private readonly List<Candidate> _scratch = new(capacity: 512);

        private struct Transform
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float   Yaw;
            public uint    LastChangedTick;
            public bool    IsNpc;
        }

        private struct Candidate
        {
            public int     NetworkId;
            public Vector3 Position;
            public Vector3 Velocity;
            public float   Yaw;
            public float   DistanceSq;
            public bool    IsNpc;

            /// <summary>
            /// The LOD interval this entity landed on for this observer.
            /// Carried through the sort so the write step knows whether to
            /// spend velocity bytes on it without recomputing the distance
            /// tier a second time.
            /// </summary>
            public int     Interval;
        }

        public SnapshotDispatcher(
            SessionManager sessions,
            InterestManager interest,
            ushort snapshotOpcode)
        {
            _sessions = sessions;
            _interest = interest;
            _snapshotOpcode = snapshotOpcode;
        }

        /// <summary>
        /// Call from the movement handler instead of broadcasting. Cheap by
        /// design: one dictionary write, no sends, no allocation.
        /// </summary>
        /// <param name="velocity">
        /// World units per second. Pass Vector3.Zero for anything that
        /// doesn't report it (spawns, teleports, the NPC wander AI) — a
        /// zero here is not a missing value, it is a positive statement
        /// that the entity is stationary, and the client will correctly
        /// extrapolate it nowhere.
        /// </param>
        public void SetTransform(int networkId, Vector3 position, Vector3 velocity, float yaw, bool isNpc, uint tick)
        {
            _transforms[networkId] = new Transform
            {
                Position        = position,
                Velocity        = velocity,
                Yaw             = yaw,
                LastChangedTick = tick,
                IsNpc           = isNpc
            };
        }

        public void Remove(int networkId) => _transforms.Remove(networkId);

        /// <summary>
        /// Once per tick, after simulation. This is the only place
        /// movement packets leave the server.
        /// </summary>
        public void Flush(uint tick)
        {
            foreach (var peer in _sessions.GetAllConnectedPeers())
            {
                if (peer.Tag is not PlayerSession { NetworkId: int observerId } session)
                    continue;

                BuildAndSend(peer, observerId, session.Position, tick);
            }
        }

        /// <summary>
        /// Hard ceiling on how many candidates get SCANNED per observer,
        /// before any sort or send-cap even applies. Without this, a
        /// dense hub (or, worse, everyone clustered near one spawn point
        /// the way this test's bots are) makes GetKnownBy(observerId)
        /// return a large fraction of the ENTIRE population for every
        /// single observer — the old MaxEntitiesPerSnapshot cap only
        /// trimmed what got SENT after that full scan already ran, so
        /// total tick cost was still O(bots) per observer and O(bots^2)
        /// overall. This is what turned a 35-minute ramp to 5000 bots
        /// into a monotonically worsening tick time even with GC pressure
        /// at zero (see TickHealth logs from 2026-09-17 - gen2GC stayed
        /// near 0 for the whole run while p50 climbed from 0ms to 2.6s).
        ///
        /// 4x MaxEntitiesPerSnapshot is generous headroom for the sort to
        /// still pick a genuinely-nearest 150 in ordinary conditions,
        /// while putting a hard, bot-count-independent ceiling on the
        /// worst case. Tune down if this is still too expensive in your
        /// own densest-hub test.
        /// </summary>
        public int MaxCandidatesScanned = 600;

        private void BuildAndSend(NetPeer peer, int observerId, Vector3 observerPos, uint tick)
        {
            _scratch.Clear();

            var known = _interest.GetKnownBy(observerId);
            var scanLimit = Math.Min(known.Count, MaxCandidatesScanned);

            for (int i = 0; i < scanLimit; i++)
            {
                var id = known[i];
                if (id == observerId) continue;

                if (!_transforms.TryGetValue(id, out var t))
                    continue;

                var distSq = Vector3.DistanceSquared(observerPos, t.Position);
                var interval = IntervalFor(distSq);

                // Phase spreading: no per-pair state, and staggered so
                // packet size stays flat instead of spiking every Nth tick.
                if (interval > 1 && (tick + (uint)id) % (uint)interval != 0)
                    continue;

                // Nothing to say about an entity that hasn't moved since
                // we last would have sent it.
                //
                // NOTE this is also what makes the client's "stop packet"
                // load-bearing. A player who stops moving stops sending,
                // so this check goes true and the server goes quiet about
                // them — which is correct and is the point. But it means
                // the LAST thing an observer heard is whatever the final
                // update said. If that final update still carried a
                // non-zero velocity, every observer extrapolates the
                // character onward past where they actually stopped and
                // then has nothing to correct it with. The client sends
                // one zero-velocity packet on the moving->stopped edge for
                // exactly this reason.
                if (tick - t.LastChangedTick > (uint)interval)
                    continue;

                if (!EntityStateCodec.IsEncodable(t.Position, observerPos))
                    continue;

                _scratch.Add(new Candidate
                {
                    NetworkId  = id,
                    Position   = t.Position,
                    Velocity   = t.Velocity,
                    Yaw        = t.Yaw,
                    DistanceSq = distSq,
                    IsNpc      = t.IsNpc,
                    Interval   = interval
                });
            }

            if (_scratch.Count == 0)
                return;

            // Only pay for the sort when the cut actually matters. In open
            // country this branch is never taken; in a hub it's what keeps
            // the nearby players smooth while the far crowd degrades.
            if (_scratch.Count > MaxEntitiesPerSnapshot)
            {
                _scratch.Sort(static (a, b) => a.DistanceSq.CompareTo(b.DistanceSq));
                _scratch.RemoveRange(
                    MaxEntitiesPerSnapshot,
                    _scratch.Count - MaxEntitiesPerSnapshot);
            }

            using var writer = new SnapshotWriter(_snapshotOpcode, tick, observerPos);

            for (int i = 0; i < _scratch.Count; i++)
            {
                var c = _scratch[i];

                // Velocity only where the client will actually extrapolate
                // with it. See the class comment.
                var includeVelocity = c.Interval == NearInterval;

                if (!writer.TryWriteEntity(c.NetworkId, c.Position, c.Velocity, c.Yaw, c.IsNpc, includeVelocity))
                    break; // MTU reached; remainder rides the next tick.
            }

            writer.Finish();

            // Defense in depth: WorldServer.RunTickLoopAsync already
            // catches per-tick, but that turns one bad snapshot into a
            // skipped tick for EVERY player, not just this one observer.
            // A too-big-packet bug (LiteNetLib's ceiling is 1023 bytes for
            // Unreliable, independent of the mtuBudget set on SnapshotWriter
            // — see its doc comment) should degrade to "this one player
            // misses a frame," not crash the whole server. It did exactly
            // that once already; this is what should have been here from
            // the start.
            try
            {
                peer.Send(writer.Buffer, 0, writer.Length, DeliveryMethod.Unreliable);
            }
            catch (LiteNetLib.TooBigPacketException ex)
            {
                // Should be unreachable given SnapshotWriter's mtuBudget,
                // but "should be" is exactly the assumption that broke
                // last time — log loudly so a regression here is visible
                // in testing instead of surfacing as a mass disconnect.
                System.Console.Error.WriteLine(
                    $"[SnapshotDispatcher] Oversized snapshot for observer {observerId} " +
                    $"({writer.Length} bytes, {writer.EntryCount} entities) — dropped, not sent. {ex.Message}");
            }
        }

        private int IntervalFor(float distanceSq)
        {
            if (distanceSq <= NearRange * NearRange) return NearInterval;
            if (distanceSq <= MidRange  * MidRange)  return MidInterval;
            return FarInterval;
        }
    }
}