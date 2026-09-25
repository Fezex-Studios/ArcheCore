namespace ArcheCore.Server.World.Lua.Scripting
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
        OnDeath      = 5, // (player, killerNpcTemplateId) - the player was killed, see CombatManager.KillPlayer
        OnInteract   = 6,
        OnItemUse    = 7, // (player, itemTemplateId, slot) - fired AFTER a successful use, see PlayerManager.TryUseItem
        OnHarvest    = 8, // (player, nodeTemplateId, itemTemplateId, quantity) - after a successful harvest, see HarvestManager
        OnKill       = 9, // (player, npcTemplateId) - after an NPC the player attacked dies, see CombatManager
        OnEnterZone  = 10, // (player, zoneId, zoneKey, previousZoneId) - crossed into a zone (0 = no zone), incl. on spawn, see PlayerManager.UpdateZone
    }
}