using System.Security.Cryptography;
using ArcheCore.Server.Auth.Config;
using Microsoft.Extensions.Options;

namespace ArcheCore.Server.Auth.Services;

/// <summary>
/// Backs /gamedata/version and /gamedata/db.
///
/// The old GameDataRoute.ts had a split personality: it computed DB_HASH
/// once at import time and commented that the hash was cached, but
/// /gamedata/version called getDbHash() again on every single request,
/// re-reading and re-hashing the whole multi-megabyte file each time. The
/// cached value was only ever used as an existence check by /gamedata/db.
/// So the comment described the intent and the code did the opposite, and
/// the launcher polls /gamedata/version as its "is the server up?" health
/// check.
///
/// This caches for real, and invalidates on the file's last-write time and
/// length, so replacing gamedata.bin no longer requires a server restart
/// to be noticed — which is what the old comment told you to do.
/// </summary>
public sealed class GameDataProvider
{
    private readonly string _path;
    private readonly ILogger<GameDataProvider> _log;
    private readonly object _gate = new();

    private string?  _cachedHash;
    private DateTime _cachedWriteTimeUtc;
    private long     _cachedLength;

    public GameDataProvider(IOptions<AuthServerConfig> config, ILogger<GameDataProvider> log)
    {
        _path = config.Value.GameDataPath;
        _log  = log;
    }

    public string Path => _path;

    public bool Exists => File.Exists(_path);

    /// <summary>
    /// SHA-256 of the game data file as lowercase hex, or null if the file
    /// isn't there. Recomputed only when the file changed on disk.
    /// </summary>
    public string? GetHash()
    {
        var info = new FileInfo(_path);

        if (!info.Exists)
        {
            lock (_gate) _cachedHash = null;
            return null;
        }

        lock (_gate)
        {
            if (_cachedHash is not null
                && _cachedWriteTimeUtc == info.LastWriteTimeUtc
                && _cachedLength == info.Length)
            {
                return _cachedHash;
            }
        }

        // Hash outside the lock — this can take a while on a large file and
        // there is no correctness problem with two callers computing the
        // same value concurrently.
        string hash;
        using (var stream = File.OpenRead(_path))
        {
            hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        lock (_gate)
        {
            _cachedHash         = hash;
            _cachedWriteTimeUtc = info.LastWriteTimeUtc;
            _cachedLength       = info.Length;
        }

        _log.LogInformation("[GameData] Hashed {Path} ({Bytes} bytes) -> {Hash}",
            _path, info.Length, hash);

        return hash;
    }
}
