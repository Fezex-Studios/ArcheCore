namespace ArcheCore.PersistenceServer.Api.Models
{
    // Mirrors the original `characters` SQLite table 1:1 — same columns,
    // same defaults (Level=1, PosY=2 on create).
    public class Character
    {
        public long   CharacterId { get; set; }
        public int    AccountId   { get; set; }
        public string Name        { get; set; } = string.Empty;
        public int    Level       { get; set; }
        public float  PosX        { get; set; }
        public float  PosY        { get; set; }
        public float  PosZ        { get; set; }
        
        // First stateful field beyond position/level. Everything that
        // follows it -- inventory, quest state -- copies this exact shape:
        // a column here, a field in P2W/W2P, a field on PlayerSession.
        public int Gold { get; set; }
    }
}