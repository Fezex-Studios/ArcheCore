-- on_player_connect.lua
-- Loaded once at server boot. Registers a handler for PlayerEvent.OnConnect (1).
-- No longer a "callback by filename" — this is just one of however many
-- scripts can hook the same event.

local function OnPlayerConnect(player)
    Server:Log("Player connected: AccountId=" .. player.AccountId)
    player:SendAnnouncementMessage("Welcome to ArcheCore!")
end

Server:RegisterPlayerEvent(1, OnPlayerConnect) -- 1 = PlayerEvent.OnConnect