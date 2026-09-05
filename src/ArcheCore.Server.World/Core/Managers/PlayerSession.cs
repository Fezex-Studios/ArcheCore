using System.Numerics;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Everything the server knows about one connected player, attached
    /// directly to the owning NetPeer via peer.Tag.
    ///
    /// Lifecycle:
    ///   - Created by TrackPendingSelection right after auth succeeds.
    ///     At this point NetworkId is null - the peer is authenticated
    ///     but hasn't chosen/created a character yet.
    ///   - NetworkId gets assigned by PlayerManager.SpawnPlayer once the
    ///     player creates or selects a character. From that point on,
    ///     NetworkId != null means "actually in the world."
    ///   - Peer.Tag is cleared to null in CleanupPeer on disconnect, so
    ///     nothing outlives the connection it belongs to.
    ///
    /// "Pending selection" is intentionally NOT a separate flag/dictionary -
    /// it's just "session exists but NetworkId is still null." That means
    /// there's no separate clear-step to forget, and no second collection
    /// that can fall out of sync with this one.
    /// </summary>
    public class PlayerSession
    {
        public int      AccountId;
        public int?     NetworkId;
        public long     CharacterId;
        public string   Name;
        public int      Level;
        public Vector3  Position;
    }
}