using System.Collections.Concurrent;

namespace ArcheCore.Server.Auth.Services;

/// <summary>
/// Failed-login lockout keyed on (username, IP) - audit gap 3.
///
/// The old lockout was keyed on the username alone, so ANYONE could lock
/// ANY account: type the name and five wrong passwords. Now five failures
/// lock that name only for the address they came from; the real owner,
/// logging in from their own address, is unaffected.
///
/// The account-wide lock in the failed_logins table still exists, at a much
/// higher threshold (Auth:AccountLockAttempts), as the backstop against one
/// account being guessed at from many addresses.
///
/// In memory: a restart forgets it, which only ever unlocks early. One Auth
/// process, so no sharing needed.
/// </summary>
public sealed class LoginThrottle
{
    private sealed class Entry
    {
        public int Attempts;
        public DateTime FirstFailure;
        public DateTime? LockedUntil;
    }

    private readonly ConcurrentDictionary<(string User, string Ip), Entry> _entries = new();
    private DateTime _lastSweep = DateTime.UtcNow;

    /// <summary>Locked for this (user, ip)? remaining = whole minutes left.</summary>
    public bool IsLocked(string username, string ip, DateTime now, out int remainingMinutes)
    {
        remainingMinutes = 0;

        if (!_entries.TryGetValue(Key(username, ip), out var e))
            return false;

        lock (e)
        {
            if (e.LockedUntil is { } until && until > now)
            {
                remainingMinutes = (int)Math.Ceiling((until - now).TotalMinutes);
                return true;
            }
        }

        return false;
    }

    /// <summary>Count a failure. True if this one locked the pair.</summary>
    public bool RecordFailure(string username, string ip, DateTime now, int maxAttempts, TimeSpan lockout)
    {
        Sweep(now, lockout);

        var e = _entries.GetOrAdd(Key(username, ip), _ => new Entry { FirstFailure = now });

        lock (e)
        {
            // An old lock that has run out, or failures long ago: start over.
            if ((e.LockedUntil is { } until && until <= now) || now - e.FirstFailure > lockout)
            {
                e.Attempts = 0;
                e.LockedUntil = null;
                e.FirstFailure = now;
            }

            e.Attempts++;

            if (e.Attempts >= maxAttempts)
            {
                e.LockedUntil = now + lockout;
                return true;
            }

            return false;
        }
    }

    public void Clear(string username, string ip) => _entries.TryRemove(Key(username, ip), out _);

    private static (string, string) Key(string username, string ip) =>
        (username.ToLowerInvariant(), ip);

    /// <summary>Drop stale entries now and then so a dictionary attack can't grow this for ever.</summary>
    private void Sweep(DateTime now, TimeSpan lockout)
    {
        if (now - _lastSweep < TimeSpan.FromMinutes(5))
            return;

        _lastSweep = now;

        foreach (var (key, e) in _entries)
        {
            bool stale;
            lock (e)
                stale = (e.LockedUntil ?? e.FirstFailure + lockout) < now;

            if (stale)
                _entries.TryRemove(key, out _);
        }
    }
}
