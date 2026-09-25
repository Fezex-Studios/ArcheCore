-- PlayerEvent.OnEnterZone = 10 (see Core/Lua/PlayerEvent.cs).
--
-- Handler signature: function(player, zoneId, zoneKey, previousZoneId)
--   zoneId         - ZoneMap id of the zone just entered, 0 = no zone
--   zoneKey        - its Key from Dev Tools > Zones ("solzreed"), "" for no zone
--   previousZoneId - where they came from, 0 on login
--
-- Fires on login, on every border crossing, and after teleports/respawns.
-- Zone ids and keys come from the zone map file (WorldServerConfig.ZoneMapPath).
-- Match on zoneKey rather than zoneId - keys survive re-numbering.

Server:RegisterPlayerEvent(10, function(player, zoneId, zoneKey, previousZoneId)
    if zoneKey == "solzreed" and previousZoneId ~= 0 then
        player:SendAnnouncementMessage("Welcome to Solzreed.")
    end
end)
