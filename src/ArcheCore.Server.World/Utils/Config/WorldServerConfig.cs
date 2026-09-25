namespace ArcheCore.Server.World.Utils.Config;

public class WorldServerConfig
{
    public int    TickRate         { get; set; } = 20;
    public int    MaxPlayers       { get; set; } = 500;
    public String MOTD             { get; set; } = String.Empty;
    public String AuthServerUrl    { get; set; } = String.Empty;
    public String InternalSecret   { get; set; } = String.Empty;
    public String PersistenceBaseUrl { get; set; } = "http://127.0.0.1:7778";

    /// <summary>
    /// How often every in-world character is saved, in seconds. Saves are
    /// spread evenly across the interval (each player lands on its own tick),
    /// so there is never a burst of N saves at once. Only characters whose
    /// position or level changed since their last save are sent.
    /// 0 or less disables autosave (disconnect / level-up / shutdown saves
    /// still happen).
    /// </summary>
    public int AutosaveIntervalSeconds { get; set; } = 120;

    /// <summary>
    /// DEV/LOAD-TEST ONLY. When true, AuthService accepts tokens of the
    /// form "loadtest:{n}" and returns account id (900000 + n) WITHOUT
    /// calling the real AuthServer. Never true outside a local/staging
    /// load-test run — this is a full auth bypass.
    /// </summary>
    public bool AllowLoadTestBypass { get; set; } = false;

    /// <summary>
    /// DEV ONLY. Enables client-driven debug opcodes that grant state the
    /// server has no way to verify - currently just C2W LevelUp. Never
    /// true on anything players can reach.
    /// </summary>
    public bool AllowDebugCommands { get; set; } = false;

    /// <summary>
    /// Can players attack each other? Off by default: turning PvP on is a
    /// decision about what kind of server this is, not something to inherit
    /// by accident. Safe zones (SpawnPoints.SafeRadius) still apply when on.
    /// </summary>
    public bool AllowPlayerVersusPlayer { get; set; } = false;

    /// <summary>
    /// Where ArcheCore.Server.Auction is listening. That service has its own
    /// MySQL database holding the listings and the auction mailbox; this
    /// server never touches it directly.
    /// </summary>
    public string AuctionServerUrl { get; set; } = "http://127.0.0.1:5090";

    /// <summary>
    /// This world server's shard name, shown to players (launcher realm
    /// list, character select, the HUD). ONE WORLD SERVER PER SHARD: a
    /// shard is one seamless world run by one process - "Kyrios" is one
    /// WorldServer, a second shard is a second process with its own
    /// config, its own port and its own persistence rows.
    /// </summary>
    public String ShardName { get; set; } = "Dev";

    /// <summary>
    /// Folder of exported terrain heightmaps (*.achtmap) for the whole
    /// shard - one file per Unity Terrain, any number of them, exported by
    /// Dev Tools > World Tiles > Export All Terrain. They are stitched into
    /// one seamless height field (TiledHeightField) and used by
    /// MovementValidator to reject positions below the ground, and by NPC
    /// AI to walk on the terrain instead of on a flat plane.
    ///
    /// Relative paths resolve against the server's own folder. A missing
    /// or empty folder is a supported state: the shard simply runs without
    /// terrain validation. Interiors and instances have no heightmap and
    /// are never validated against one.
    /// </summary>
    public String TerrainDirectory { get; set; } = "Data/terrain_data";

    /// <summary>
    /// LEGACY single-file setting, from before the world was partitioned.
    /// Still honoured - the file is added to the same stitched field - so
    /// existing configs keep working. Prefer TerrainDirectory.
    /// </summary>
    public String HeightmapTerrainPath { get; set; } = String.Empty;

    /// <summary>
    /// True loads every heightmap at boot. False (default) loads each one
    /// the first time a player or NPC needs it and unloads it after
    /// TerrainIdleUnloadMinutes unused - the right choice once the world is
    /// bigger than a handful of tiles, since a full continent of heightmaps
    /// is gigabytes and only the areas with people in them matter.
    /// </summary>
    public bool PreloadAllTerrain { get; set; } = false;

    /// <summary>How long an unused heightmap stays in memory. See PreloadAllTerrain.</summary>
    public int TerrainIdleUnloadMinutes { get; set; } = 10;

    /// <summary>
    /// NPCs follow the terrain height while they walk, where terrain data
    /// exists. Off keeps the old behaviour (NPCs keep the Y they spawned
    /// at), for debugging.
    /// </summary>
    public bool NpcGroundSnap { get; set; } = true;

    /// <summary>
    /// The shard's zone map (*.aczmap), painted and saved by Dev Tools >
    /// Zones: which zone every point of the world is in, plus each zone's
    /// name, level range and PvP mode. The client ships its own copy in
    /// StreamingAssets; the two are compared by hash on entering the world.
    /// Missing is fine - the whole shard is simply "no zone".
    /// </summary>
    public String ZoneMapPath { get; set; } = "Data/world/zones.aczmap";

    /// <summary>
    /// Called once at boot, before the socket opens. Every check here is
    /// something that silently produces a working-looking server with no
    /// security: a default secret that an attacker already knows, or a
    /// bypass flag left on after a load test. A config mistake that costs
    /// you the shard should cost you a failed startup instead, loudly,
    /// while you are watching.
    /// </summary>
    public void Validate(bool isDevelopment)
    {
        const string Placeholder = "replace_this_with_a_real_secret";

        if (string.IsNullOrWhiteSpace(InternalSecret)
            || InternalSecret == Placeholder
            || InternalSecret.Length < 32)
        {
            throw new InvalidOperationException(
                "World:InternalSecret is missing, still the placeholder, or shorter than " +
                "32 characters. It authenticates this server to BOTH the AuthServer and " +
                "the Persistence server, so all three must carry the same value. " +
                "Generate one with: openssl rand -base64 48");
        }

        if (AllowLoadTestBypass && !isDevelopment)
        {
            throw new InvalidOperationException(
                "World:AllowLoadTestBypass is true outside the Development environment. " +
                "This is a complete authentication bypass - any client can present " +
                "\"loadtest:N\" and be account 900000+N. Set it to false.");
        }

        if (AllowDebugCommands && !isDevelopment)
        {
            throw new InvalidOperationException(
                "World:AllowDebugCommands is true outside the Development environment. " +
                "Set it to false.");
        }
    }
}