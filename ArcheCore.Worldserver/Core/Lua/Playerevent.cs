namespace ArcheCore.WorldServer.Lua.Scripting
{
    /// <summary>
    /// Event IDs Lua scripts can hook via Server:RegisterPlayerEvent(id, fn).
    /// Mirrors the Eluna-style "register once, fire by id" pattern instead of
    /// running a script file per event.
    /// </summary>
    public enum PlayerEvent
    {
        OnConnect    = 1,
        OnDisconnect = 2,
        OnLevelUp    = 3,
        OnChat       = 4,
        OnDeath      = 5,
    }
}