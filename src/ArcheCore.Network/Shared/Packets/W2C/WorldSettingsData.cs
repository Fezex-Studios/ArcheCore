using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// How this shard's world is laid out, sent once on entering it (inside
    /// W2CEnterWorldPacket). The client used to hardcode copies of these
    /// numbers - NetworkCells' gizmo radii carried a "KEEP THESE IN SYNC"
    /// comment - and a copy that drifts is a client that culls, streams or
    /// draws debug circles in the wrong place without any error.
    ///
    /// String-keyed like the packet that carries it, so adding a field
    /// later doesn't break an older client.
    /// </summary>
    [MessagePackObject(true)]
    public class WorldSettingsData
    {
        /// <summary>Shard (one world server) name, e.g. "Kyrios".</summary>
        public string ShardName = "";

        /// <summary>
        /// World partition tile edge in world units. The client compares it
        /// against its own WorldGrid.TileSize and refuses to stream on a
        /// mismatch: both sides must cut the world the same way, and the
        /// constant lives in ArcheCore.Movement, which is built into both.
        /// </summary>
        public float TileSize;

        /// <summary>InterestManager.SpawnRadius - things closer than this are replicated.</summary>
        public float InterestSpawnRadius;

        /// <summary>InterestManager.DespawnRadius - things further than this are removed.</summary>
        public float InterestDespawnRadius;

        /// <summary>
        /// Hash of the server's zone map ("" if it has none). The client
        /// hashes its own StreamingAssets copy and warns on a mismatch - the
        /// zone banner would otherwise name the wrong zones with no error.
        /// </summary>
        public string ZoneMapHash = "";
    }
}
