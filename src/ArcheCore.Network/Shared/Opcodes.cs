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
        PlayerSpawned = 29,
        W2CWorldSnapshot = 30, // W2C: batched, quantized per-tick movement snapshot (unreliable) — see SnapshotDispatcher/SnapshotWriter
        W2CPositionCorrection = 31, // W2C: reliable snap-back after MovementValidator rejects a client's reported position
        W2CJumpEvent = 32,
        W2CGoldUpdate        = 33, // W2C: absolute gold balance for one player - see W2CGoldUpdatePacket
        C2WDebugAddGold      = 34, // C2W: DEV ONLY - see C2WDebugAddGoldHandler

    }
}