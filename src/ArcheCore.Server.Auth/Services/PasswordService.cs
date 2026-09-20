namespace ArcheCore.Server.Auth.Services;

/// <summary>
/// Port of PasswordService.ts.
///
/// Same algorithm, same cost factor, same output format, so every
/// password_hash already in auth.db verifies without a reset. BCrypt.Net-Next
/// reads and writes the $2a$/$2b$ strings the Node `bcrypt` package
/// produced.
/// </summary>
public sealed class PasswordService
{
    /// <summary>
    /// 10 is the library default; 12 is the recommended floor for anything
    /// public-facing, and it's what the old server used. Changing this only
    /// affects NEW hashes — existing ones carry their own cost factor
    /// inside the hash string and keep verifying at whatever they were
    /// created with.
    /// </summary>
    private const int WorkFactor = 12;

    /// <summary>
    /// A real hash of a throwaway password, computed once at startup, used
    /// to burn the same CPU time on a login for a username that doesn't
    /// exist as on one that does.
    ///
    /// The old server had a hardcoded constant for this
    /// ("$2b$12$invalidhashfortimingpurposesonly000...") which is not a
    /// valid bcrypt string — the salt section isn't in bcrypt's alphabet.
    /// A malformed hash makes the comparison fail FAST instead of doing the
    /// work, so the timing defense it was written for wasn't actually
    /// there: unknown usernames still returned measurably quicker than
    /// known ones. Hashing a real value fixes that.
    /// </summary>
    private readonly string _dummyHash;

    public PasswordService()
    {
        _dummyHash = BCrypt.Net.BCrypt.HashPassword("archecore-timing-dummy", WorkFactor);
    }

    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    /// <summary>
    /// Returns false rather than throwing on a malformed stored hash. A
    /// corrupt row should fail the login, not 500 the endpoint and tell the
    /// caller something interesting about the database.
    /// </summary>
    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Call on the account-not-found path so the response time matches the
    /// account-found path. The result is meaningless and must be discarded.
    /// </summary>
    public void BurnTime(string password) => Verify(password, _dummyHash);
}
