-- PlayerEvent.OnKill = 9 (see Core/Lua/PlayerEvent.cs).
--
-- Handler signature: function(player, npcTemplateId)
--   npcTemplateId - NpcTemplates.Id of what died
--
-- Fires after the NPC has died and its corpse (if it dropped anything)
-- exists. This is the "kill" hook quest objectives (roadmap L) will use.

Server:RegisterPlayerEvent(9, function(player, npcTemplateId)
    if npcTemplateId == 1 then
        player:SendAnnouncementMessage("The orc falls.")
    end
end)
