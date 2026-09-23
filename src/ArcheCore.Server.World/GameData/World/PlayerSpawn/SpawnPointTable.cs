namespace ArcheCore.Server.World.GameData.World.PlayerSpawn;

/// <summary>
/// A named place in the world players can appear. One row is the login
/// spawn (IsDefault); rows marked IsRespawnPoint are where the dead come
/// back - the nearest one to where you fell, WoW-graveyard style.
///
/// A row can be both, one, or neither: "neither" is a plain landmark that
/// SafeRadius can still use to keep a town free of player-vs-player fighting.
/// </summary>
public class SpawnPointTable
{
    public int Id { get; set; }
    public string Name { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>The login spawn. Exactly one row should have this.</summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Dead players respawn at the nearest of these. None marked = everyone
    /// goes back to the default spawn, which is what happened before.
    /// </summary>
    public bool IsRespawnPoint { get; set; }

    /// <summary>
    /// No player-vs-player fighting within this many units of the point.
    /// 0 = no protection. Checked for both attacker and victim, so you
    /// can't stand outside a town and shoot into it.
    /// </summary>
    public float SafeRadius { get; set; }
}
