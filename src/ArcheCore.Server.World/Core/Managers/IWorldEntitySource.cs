using LiteNetLib;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// A kind of non-player, non-NPC entity that lives in the shared
    /// interest grid - harvest nodes today, corpses and ground loot later.
    ///
    /// Everything in the grid with id &gt;= SpawnManager.NpcIdBase is already
    /// treated as "not a player" by spawner activation, snapshots and
    /// disconnect cleanup. The one thing those systems can't do on their
    /// own is tell a player what the entity IS when it comes into view -
    /// that's this. SpawnManager.TrySendSpawnTo asks NPCs first, then each
    /// registered source, so a new entity kind plugs in without touching
    /// the movement or spawn code again.
    ///
    /// Despawn needs nothing: walking away sends W2CNpcDespawn for any
    /// non-player id, and the client checks every registry for that id.
    /// </summary>
    public interface IWorldEntitySource
    {
        /// <summary>If networkId is one of yours, send its spawn packet to peer and return true.</summary>
        bool TrySendSpawn(NetPeer peer, int networkId);
    }
}
