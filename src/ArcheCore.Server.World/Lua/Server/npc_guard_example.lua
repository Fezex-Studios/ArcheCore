-- Example script for NpcTemplate.Id = 1 ("Guard").
-- Drop files like this in Lua/Server/ next to your other scripts -
-- LuaEngine.LoadAllScripts runs every .lua file in that folder once at boot.
--
-- PlayerEvent.OnInteract = 6 (see Core/Lua/Playerevent.cs)
-- Handler signature: function(player, targetTemplateId, targetKind)
--   player           - LuaPlayer, the one who pressed interact
--   targetTemplateId - NpcTemplate.Id of whatever they interacted with
--   targetKind       - InteractableKind as an int (1 = Npc)

Server:RegisterPlayerEvent(6, function(player, targetTemplateId, targetKind)

    if targetTemplateId == 1 then
        player:SendDialogue("Guard", "Halt! State your business, traveler.")
        return
    end

    if targetTemplateId == 2 then
        -- Placeholder until inventory exists - see W2CInteractLootPacket.
        player:SendLootMessage("Rusty Sword")
        return
    end

    -- Unhandled template id - say nothing rather than guess.
end)