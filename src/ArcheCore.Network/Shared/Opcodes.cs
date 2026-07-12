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
    }
}