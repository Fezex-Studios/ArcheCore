namespace ArcheCore.Library.Net.Worldserver
{
    public enum Opcodes : ushort
    {
        Connect                    = 1,
        SpawnPlayer                = 2,
        MOTD                       = 3,
        PlayerMove                 = 4,
        PlayerPosition             = 5,
        Authenticate               = 6,
        PlayerLeave                = 7,
        Announcement               = 9,
        SpawnNpc                   = 10,
        W2CTestPacket              = 11,
        RequestPlayerLevel         = 12,
        PlayerLevelResponse        = 13,
        W2CCharacterNotFound       = 14,
        C2WCreateCharacterRequest  = 15,
        // --- Interaction system ---
        Interact                   = 16, // C2W: player pressed interact on a target
        W2CInteractDialogue        = 17, // W2C: NPC dialogue response
        W2CInteractLoot            = 18, // W2C: item pickup response (placeholder until inventory exists)
        W2CInteractDenied          = 19, // W2C: interaction rejected (out of range, target gone, etc.)
        W2CCharacterList           = 20,
        C2WSelectCharacter         = 21,
        ChatMessage                = 22,
        NpcPosition                = 23,
        NpcDespawn                 = 24,
        ItemRequestData            = 25,
        ItemDataResponse           = 26,
        LevelUp = 28,   // NEW
        // PlayerSpawned = 29 retired - replaced by W2CEnterWorld (39). The
        // client used to ack "I'm actually spawned" before the server
        // would send Level/Name; Gold/Inventory never waited for that ack
        // in the first place. W2CEnterWorld sends all three the same way
        // Gold/Inventory always were: pushed blind from SpawnPlayer, no
        // handshake required.
        W2CWorldSnapshot = 30, // W2C: batched, quantized per-tick movement snapshot (unreliable) — see SnapshotDispatcher/SnapshotWriter
        W2CPositionCorrection = 31, // W2C: reliable snap-back after MovementValidator rejects a client's reported position
        W2CJumpEvent = 32,
        W2CGoldUpdate        = 33, // W2C: absolute gold balance for one player - see W2CGoldUpdatePacket
        C2WDebugAddGold      = 34, // C2W: DEV ONLY - see C2WDebugAddGoldHandler
        W2CInventorySnapshot   = 35, // W2C: full inventory - kept for a possible future manual resync; not sent at spawn anymore, see W2CEnterWorld
        W2CInventorySlotChanged = 36, // W2C: one slot changed
        C2WMoveItem            = 37, // C2W: move/swap/merge two slots
        C2WDebugAddItem        = 38, // C2W: DEV ONLY - see C2WDebugAddItemHandler
        W2CEnterWorld           = 39, // W2C: everything the client needs on entering the world - CharacterData + Gold + Inventory, one atomic send
        C2WDropItem             = 40, // C2W: destroy all or part of one slot - see PlayerManager.TryDropItem
        C2WUseItem              = 41, // C2W: use one slot - ItemUse row decides effect/consume/cooldown, see PlayerManager.TryUseItem
        W2CItemCooldown         = 42, // W2C: a cooldown group started - which item ids it covers and for how long
        
        // --- Harvesting (roadmap E) ---
        W2CSpawnHarvestNode     = 43, // W2C: a harvest node came into view - template, model, position, depleted or not
        W2CHarvestNodeState     = 44, // W2C: a node you can see was depleted or respawned
        W2CHarvestStarted       = 45, // W2C: your harvest began - show the progress bar for DurationMs
        W2CHarvestCompleted     = 46, // W2C: your harvest finished - what you got
        W2CHarvestCancelled     = 47, // W2C: your harvest stopped early - moved, inventory full, node gone

        // --- NPC shops (roadmap F) ---
        W2CShopOpen             = 48, // W2C: the full buy/sell list for the merchant you interacted with
        C2WShopBuy              = 49, // C2W: buy N of an item from a merchant
        C2WShopSell             = 50, // C2W: sell N from an inventory slot to a merchant
        W2CShopResult           = 51, // W2C: success/failure message for a buy or sell
        
        
        
    }
}