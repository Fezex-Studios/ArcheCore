-- PlayerEvent.OnHarvest = 8 (see Core/Lua/PlayerEvent.cs).
--
-- Handler signature: function(player, nodeTemplateId, itemTemplateId, quantity)
--   nodeTemplateId - HarvestNodeTemplates.Id that was gathered
--   itemTemplateId - Items.item_id the player received
--   quantity       - how many
--
-- Fires only AFTER the item is already in the player's inventory. This is
-- the "collect" hook quest objectives (roadmap L) will listen to.

Server:RegisterPlayerEvent(8, function(player, nodeTemplateId, itemTemplateId, quantity)
    if nodeTemplateId == 1 and quantity >= 3 then
        player:SendAnnouncementMessage("A rich vein! You pulled out " .. quantity .. " ore.")
    end
end)
