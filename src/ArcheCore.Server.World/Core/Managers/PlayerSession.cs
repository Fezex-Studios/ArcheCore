using System.Numerics;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Everything the server knows about one connected player, attached
    /// directly to the owning NetPeer via peer.Tag.
    ///
    /// Lifecycle:
    ///   - Created by TrackPendingSelection right after auth succeeds.
    ///     NetworkId is null - authenticated, no character in world yet.
    ///   - SpawnRequested flips to true the moment a select/create request
    ///     is accepted, so a second (double-clicked) request is ignored
    ///     instead of spawning the same peer twice.
    ///   - NetworkId is assigned by PlayerSpawnManager.SpawnPlayer. From then
    ///     on, NetworkId != null means "actually in the world."
    ///   - peer.Tag is cleared in CleanupPeer on disconnect.
    ///
    /// Only touched on the tick thread.
    /// </summary>
    public class PlayerSession
    {
        public int      AccountId;
        public int?     NetworkId;
        public long     CharacterId;
        public string   Name;
        public int      Level;
        public Vector3  Position;

        /// <summary>A select/create for this peer is already in flight or done.</summary>
        public bool     SpawnRequested;

        // --- Save tracking (see AutosaveScheduler) ---

        /// <summary>False until the current state is known to match the database.</summary>
        public bool     HasBeenSaved;
        public Vector3  SavedPosition;
        public int      SavedLevel;

        // --- Movement validation (see MovementValidator) ---
        //
        // Deliberately here rather than in a dictionary inside the
        // validator. This object's lifetime is already the connection's
        // lifetime and is already cleaned up on disconnect, so hanging the
        // per-player state off it means there is no second thing to
        // remember to prune - which is exactly the leak that had to be
        // fixed in SnapshotDispatcher when NPCs moved onto it.

        /// <summary>
        /// False until the first movement packet or an authoritative move
        /// seeds a baseline. Until then there is nothing to measure a
        /// reported position against.
        /// </summary>
        public bool     MoveBaselineSet;

        /// <summary>
        /// Last position the server actually believed. This is what a
        /// correction snaps the client back to, so it must never be
        /// assigned from a rejected packet.
        /// </summary>
        public Vector3  LastValidPosition;

        /// <summary>Monotonic seconds (MovementValidator.Now), not DateTime.</summary>
        public double   LastMoveTime;

        // Banked movement allowance, in world units. Refills at the legal
        // speed and is spent by distance moved - see MovementValidator for
        // why it's a bank and not an instantaneous rate check.
        public float    HorizontalBudget;
        public float    UpBudget;
        public float    DownBudget;

        /// <summary>Consecutive-ish rejections; decays on accepted packets.</summary>
        public int      MovementViolations;

        /// <summary>Monotonic seconds of the last snap-back sent to this client.</summary>
        public double   LastCorrectionTime;

        /// <summary>Moved more than 10cm or changed level since the last save.</summary>
        public bool IsDirty =>
            !HasBeenSaved ||
            Level != SavedLevel ||
            Vector3.DistanceSquared(Position, SavedPosition) > 0.01f;

        public void MarkSaved()
        {
            HasBeenSaved  = true;
            SavedPosition = Position;
            SavedLevel    = Level;
        }
    }
}