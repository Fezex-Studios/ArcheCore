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
