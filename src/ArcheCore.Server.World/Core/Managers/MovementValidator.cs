using System;
using System.Diagnostics;
using System.Numerics;
using ArcheCore.Movement;
using ArcheCore.Movement.Terrain;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using NLog;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Decides whether to believe a client's reported position.
    ///
    /// Until now the world server took the client's word for everything on
    /// the movement path, which is the same choice ArcheAge itself made -
    /// its server relays what the client reports - and the reason that game
    /// had the speed-hack and teleport-hack reputation it did. Copying the
    /// packet layout is worth doing; copying that is not.
    ///
    /// WHY A BANKED BUDGET AND NOT distance/time > maxSpeed
    ///
    /// The naive check fires constantly on honest clients. Movement packets
    /// are unreliable UDP at 20Hz, and they bunch: the network goes quiet
    /// for 200ms and then delivers four at once. Measured against the
    /// arrival gap, the packets in that burst look like 4x speed, because
    /// the time went missing in transit, not in the simulation. Anything
    /// that rejects on a single over-rate sample punishes jitter, and
    /// players on bad connections get rubber-banded while the actual
    /// cheater - who can simply pace their lies - sails through.
    ///
    /// So allowance ACCUMULATES with elapsed time and is SPENT by distance
    /// moved. A burst spends banked allowance and passes. Sustained speed
    /// above the limit drains the bank and then fails, and keeps failing,
    /// because the bank refills at exactly the legal rate. The bank is
    /// capped (BurstSeconds) so it can't accrue over a long idle and then
    /// fund one enormous jump - without that cap, standing still for a
    /// minute would buy a free teleport across the map.
    ///
    /// Three separate budgets, because the three axes have genuinely
    /// different legal limits: horizontal is walk/run speed, upward is
    /// bounded by jump impulse, downward is bounded by terminal fall and is
    /// therefore far more permissive than either. Sharing one budget across
    /// them would mean either a long fall eating the horizontal allowance,
    /// or an upward limit loose enough to fly.
    ///
    /// WHAT THIS DOES AND DOESN'T CATCH
    ///
    /// It catches speed hacks, teleports, flight, and garbage/NaN input. It
    /// does NOT catch walking through walls, walking on water, or standing
    /// somewhere unreachable - that needs server-side collision against the
    /// same geometry the client uses, which is a much larger piece of work
    /// and needs the terrain and collision data server-side first.
    ///
    /// It also can't catch a patient cheater who moves at exactly the legal
    /// speed in a straight line through a mountain. That is the correct
    /// scope for this layer; treat it as the cheap ceiling, not as
    /// anti-cheat.
    /// </summary>
    public sealed class MovementValidator
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly ReplicationManager _replication;

        /// <summary>
        /// Real ground truth, when a heightmap has been loaded for this
        /// zone. Null is a supported state - a zone with no exported
        /// heightmap yet simply isn't terrain-validated, same as an
        /// instance/dungeon interior (see HeightmapCollisionWorld's
        /// remarks on out-of-bounds queries). Nothing else here changes
        /// behaviour when it's null; the budget checks above are the
        /// floor this class always had.
        /// </summary>
        private readonly HeightmapCollisionWorld _terrain;
        private readonly MovementProfile _terrainProfile;

        /// <summary>
        /// Slack, in world units, allowed between the client's reported Y
        /// and the heightmap's answer before it counts as "below ground".
        /// Not zero, on purpose: the exported heightmap resolution won't
        /// perfectly match Unity's rendered terrain mesh on every texel
        /// (see HeightmapData's remarks on consistency vs fidelity), the
        /// client's own capsule keeps a skin-width gap, and legitimate
        /// standing-on-a-moving-platform or standing-on-another-entity
        /// positions aren't terrain height at all. This is deliberately
        /// generous - it exists to catch someone meters under the map, not
        /// to nitpick centimetre slope disagreement.
        /// </summary>
        public float TerrainEmbedTolerance = 1.5f;

        public MovementValidator(ReplicationManager replication)
            : this(replication, null)
        {
        }

        /// <param name="terrain">
        /// Pass a loaded HeightmapCollisionWorld to additionally reject
        /// positions the server can prove are below the actual ground -
        /// the check the budget system was explicitly documented as NOT
        /// doing. Pass null (or use the other constructor) to keep the old
        /// budget-only behaviour, e.g. before any heightmap has been
        /// exported for a zone.
        /// </param>
        public MovementValidator(ReplicationManager replication, HeightmapCollisionWorld terrain)
        {
            _replication = replication;
            _terrain = terrain;

            // A stand-in profile for the validator's own terrain probe.
            // This does NOT need to match every player's real movement
            // profile exactly - it only asks "is there ground meaningfully
            // below this reported position", which is a coarse question a
            // generic humanoid capsule answers well enough. A mount or a
            // creature with a wildly different capsule can get its own
            // profile passed in later if this ever needs per-character
            // accuracy.
            _terrainProfile = new MovementProfile();
        }

        // --- Tuning. Wire these to WorldServerConfig when you care. ---

        /// <summary>
        /// Ceiling on sustained horizontal speed, world units/second.
        /// PlayerController's moveSpeed is 5; the headroom absorbs slope
        /// descent (a character walking downhill covers more ground per
        /// second than its nominal speed) and CharacterController's own
        /// depenetration nudges. RAISE THIS before adding mounts or sprint,
        /// or every mounted player is a cheater.
        /// </summary>
        public float MaxHorizontalSpeed = 8f;

        /// <summary>
        /// Ceiling on sustained upward speed. Jump impulse is
        /// sqrt(jumpHeight * -2 * gravity) = sqrt(1.5 * 40) ~ 7.75 u/s, so
        /// this is that plus headroom. This is the value that stops flight.
        /// </summary>
        public float MaxUpwardSpeed = 12f;

        /// <summary>
        /// Ceiling on sustained downward speed. Deliberately loose - a long
        /// fall is fast, legitimate, and entirely client-simulated, and
        /// there is no exploit in falling quickly.
        /// </summary>
        public float MaxDownwardSpeed = 80f;

        /// <summary>
        /// How much allowance can bank. Half a second of movement is enough
        /// to absorb any burst a 20Hz sender plus ordinary jitter produces,
        /// and small enough that the banked surplus is never a useful
        /// teleport.
        /// </summary>
        public float BurstSeconds = 0.5f;

        /// <summary>
        /// Any single reported step longer than this is rejected outright,
        /// budget or not. Catches the one-shot teleport that a banked
        /// budget would otherwise partially fund.
        /// </summary>
        public float MaxSingleStep = 20f;

        /// <summary>
        /// Reported positions this far outside the playable world are
        /// garbage or an attack. Widen for your actual world size; the
        /// point is to reject values that would poison the spatial grid
        /// (which buckets by position) rather than to enforce playable
        /// bounds.
        /// </summary>
        public float WorldBound = 100_000f;

        /// <summary>
        /// Packets arriving faster than this are dropped without counting
        /// as violations. A flood is a resource attack, not a movement lie,
        /// and treating it as a violation would mean an attacker could
        /// trigger their own corrections cheaply.
        /// </summary>
        public float MinPacketInterval = 0.008f;

        /// <summary>
        /// Rejections tolerated before the client is snapped back. Not 1:
        /// a single rejection is far more likely to be an honest client
        /// hitting an edge case this validator gets wrong than a cheater,
        /// and a wrong correction is a visible teleport for an innocent
        /// player. The rejected packets aren't applied either way, so a few
        /// forgiven ones cost nothing but a little divergence.
        /// </summary>
        public int ViolationsBeforeCorrection = 3;

        /// <summary>Minimum gap between corrections to one client.</summary>
        public float CorrectionCooldownSeconds = 1f;

        public enum Result
        {
            Accepted,

            /// <summary>Dropped, no correction, not a violation.</summary>
            Ignored,

            /// <summary>Not applied; may have triggered a correction.</summary>
            Rejected
        }

        /// <summary>Monotonic seconds. NOT DateTime - that can step backwards
        /// on an NTP correction, which would hand out a negative elapsed and
        /// with it an unbounded budget refill.</summary>
        public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        /// <summary>
        /// Call whenever the SERVER moves a character: spawn, teleport,
        /// respawn, summon, a skill that displaces. Without it the next
        /// client report looks like a teleport and gets rejected - and
        /// worse, gets corrected back to the pre-teleport position, which
        /// would undo the server's own move.
        /// </summary>
        public void NotifyAuthoritativeMove(PlayerSession session, Vector3 position)
        {
            session.LastValidPosition = position;
            session.MoveBaselineSet = true;
            session.LastMoveTime = Now;
            session.MovementViolations = 0;

            // Seeded FULL, not empty. A character that has just been placed
            // by the server is the most likely thing in the game to move
            // sharply in its first moments - settling onto ground,
            // depenetrating out of whatever it was spawned inside - and
            // starting it with no allowance means punishing it for the
            // server's own placement. The bank is capped anyway, so a full
            // start is worth at most BurstSeconds of movement.
            session.HorizontalBudget = MaxHorizontalSpeed * BurstSeconds;
            session.UpBudget = MaxUpwardSpeed * BurstSeconds;
            session.DownBudget = MaxDownwardSpeed * BurstSeconds;
        }

        public Result Validate(NetPeer peer, PlayerSession session, Vector3 position, Vector3 velocity)
        {
            // FIRST, and it has to be first. Every comparison involving NaN
            // is false, so a NaN coordinate passes every bound and speed
            // check below and lands in the spatial grid, where it poisons
            // whatever cell arithmetic touches it.
            if (!IsFinite(position) || !IsFinite(velocity))
                return Reject(peer, session, "non-finite position or velocity");

            if (MathF.Abs(position.X) > WorldBound ||
                MathF.Abs(position.Y) > WorldBound ||
                MathF.Abs(position.Z) > WorldBound)
                return Reject(peer, session, "position outside world bounds");

            var now = Now;

            // First report since spawn. Seed the baseline from wherever the
            // server already believes this character is, rather than from
            // the client's claim - the spawn position is authoritative and
            // taking the client's first packet as gospel would let it
            // choose where it starts.
            if (!session.MoveBaselineSet)
            {
                NotifyAuthoritativeMove(session, session.Position);
                session.LastValidPosition = session.Position;
            }

            var elapsed = now - session.LastMoveTime;

            if (elapsed < MinPacketInterval)
                return Result.Ignored;

            var delta = position - session.LastValidPosition;

            if (delta.Length() > MaxSingleStep)
                return Reject(peer, session, $"single step of {delta.Length():F1} units");

            var horizontal = new Vector3(delta.X, 0f, delta.Z).Length();
            var vertical = delta.Y;

            var dt = (float)elapsed;

            // REFILL ALL THREE, EVERY PACKET, BEFORE SPENDING ANY.
            //
            // An earlier version folded the refill into TryConsume, which
            // meant a budget only accrued on packets that spent it - so the
            // upward budget sat at zero for as long as a player stayed on
            // the ground, and their first jump had exactly one interval of
            // allowance to pay for it. Anything that moved a character
            // upward faster than one interval's worth was rejected on the
            // spot: a jump off a slope, a knockback, and in particular
            // CharacterController depenetrating out of geometry, which
            // resolves a full overlap in a single frame.
            //
            // The consequence was worse than a spurious rejection. Three of
            // them trip a correction, the correction snaps the client back
            // to the pre-push position - which is the position INSIDE the
            // geometry it was trying to escape - and ForcePosition
            // re-enables the CharacterController there. The character then
            // falls through the world. A validator that manufactures the
            // exact failure it exists to prevent is worse than no validator.
            Refill(ref session.HorizontalBudget, dt, MaxHorizontalSpeed);
            Refill(ref session.UpBudget,         dt, MaxUpwardSpeed);
            Refill(ref session.DownBudget,       dt, MaxDownwardSpeed);

            if (!TrySpend(ref session.HorizontalBudget, horizontal))
                return Reject(peer, session, $"horizontal speed ({horizontal / dt:F1} u/s sustained)");

            if (vertical > 0f)
            {
                if (!TrySpend(ref session.UpBudget, vertical))
                    return Reject(peer, session, $"upward speed ({vertical / dt:F1} u/s sustained)");
            }
            else if (vertical < 0f)
            {
                if (!TrySpend(ref session.DownBudget, -vertical))
                    return Reject(peer, session, $"downward speed ({-vertical / dt:F1} u/s sustained)");
            }

            // GROUND TRUTH. Everything above this catches how fast a
            // position was reached; this catches whether the position
            // itself could ever be legitimate. A speed budget alone cannot
            // - the class remarks say so directly, and it's what let a
            // buggy or malicious client report a Y underneath the terrain
            // and have every observer render it as "fell through the
            // floor", because nothing server-side had real geometry to
            // check that claim against. Now something does.
            if (!ValidateAgainstTerrain(position, out string terrainReason))
                return Reject(peer, session, terrainReason);

            session.LastValidPosition = position;
            session.LastMoveTime = now;

            // Decay rather than clear. A client that violates every third
            // packet is still cheating; resetting to zero on each good one
            // would let it stay permanently one under the threshold.
            if (session.MovementViolations > 0)
                session.MovementViolations--;

            return Result.Accepted;
        }

        /// <summary>
        /// Accrue allowance for elapsed time, capped so a long idle can't
        /// bank a teleport. Separate from spending on purpose - see the
        /// comment at the call site for what folding them together broke.
        /// </summary>
        private void Refill(ref float budget, float elapsed, float rate)
        {
            budget = MathF.Min(budget + elapsed * rate, rate * BurstSeconds);
        }

        /// <summary>
        /// Returns false when the move costs more than has been banked. The
        /// small absolute slack covers quantization and float drift on a
        /// move that is legal to within a millimetre.
        /// </summary>
        private static bool TrySpend(ref float budget, float distance)
        {
            if (distance > budget + 0.05f)
                return false;

            budget -= distance;
            return true;
        }

        private Result Reject(NetPeer peer, PlayerSession session, string reason)
        {
            session.MovementViolations++;

            if (session.MovementViolations < ViolationsBeforeCorrection)
                return Result.Rejected;

            var now = Now;
            if (now - session.LastCorrectionTime < CorrectionCooldownSeconds)
                return Result.Rejected;

            session.LastCorrectionTime = now;
            session.MovementViolations = 0;

            // Reset the bank along with the position. Leaving it drained
            // means the client's first legitimate step after a correction
            // is itself a violation, which produces a correction loop that
            // looks exactly like a much worse bug than whatever caused the
            // first one.
            session.HorizontalBudget = MaxHorizontalSpeed * BurstSeconds;
            session.UpBudget = MaxUpwardSpeed * BurstSeconds;
            session.DownBudget = MaxDownwardSpeed * BurstSeconds;
            session.LastMoveTime = now;

            Logger.Warn(
                "[MovementValidator] Correcting {Name} (net {Id}) to {Pos} - {Reason}",
                session.Name, session.NetworkId, session.LastValidPosition, reason);

            W2CPositionCorrectionPacketSender.Send(_replication, peer, session.LastValidPosition);

            return Result.Rejected;
        }

        private static bool IsFinite(Vector3 v) =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

        /// <summary>
        /// True if this position is consistent with the loaded terrain, or
        /// if there's no terrain to check it against. False only for a
        /// position the heightmap can PROVE is wrong - below ground by
        /// more than TerrainEmbedTolerance. Anything this can't prove
        /// (no heightmap loaded, position outside the loaded extent, an
        /// instance interior) passes, because "unproven" is not the same
        /// claim as "wrong" - see HeightmapCollisionWorld's remarks on
        /// out-of-bounds queries.
        /// </summary>
        private bool ValidateAgainstTerrain(Vector3 position, out string reason)
        {
            reason = null;

            if (_terrain == null)
                return true;

            float halfHeight = _terrainProfile.HalfHeight;
            float segment = _terrainProfile.CapsuleSegment;
            float radius = _terrainProfile.Radius - _terrainProfile.SkinWidth;

            // Capsule centre from the reported FEET position - position on
            // the wire is feet-on-ground, same convention PlayerSession and
            // the spawn/interest code already use. See MoveState.Position's
            // remark on centre vs. feet for why the motor itself works in
            // centre space; this stays in feet space because that's what
            // every other server system already agreed on for this field.
            Vector3 center = position + new Vector3(0f, halfHeight, 0f);

            if (!_terrain.CheckCapsule(center, radius, segment))
                return true; // above ground, or outside the loaded heightmap - nothing to catch

            _terrain.ComputePenetration(center, radius, segment, out _, out float depth);

            if (depth <= TerrainEmbedTolerance)
                return true; // within the slack this class documents above

            reason = $"below terrain by {depth:F1} units";
            return false;
        }
    }
}