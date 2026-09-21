-- PlayerEvent.OnItemUse = 7 (see Core/Lua/Playerevent.cs).
    -- Drop in Lua/Server/ next to npc_guard_example.lua.
        --
    -- Handler signature: function(player, itemTemplateId, slot)
    --   player         - LuaPlayer who used the item
--   itemTemplateId - ItemTable.item_id that was used
    --   slot           - inventory slot index it was used from, 0-19
        --
    -- Fires only AFTER the server accepted the use: the item had an ItemUse
    -- row, its cooldown had expired, the built-in effect succeeded, and (if
-- ConsumeOnUse) one was already removed. Nothing here needs to re-check
    -- any of that.
        --
    -- Items with EffectType = 0 (ScriptOnly) have no built-in effect, so this
    -- hook is the whole behaviour for them - teleport scrolls, quest items.

    Server:RegisterPlayerEvent(7, function(player, itemTemplateId, slot)
player:SendDialogue("Item", "You used item #" .. itemTemplateId .. " from slot " .. slot .. ".")
end)