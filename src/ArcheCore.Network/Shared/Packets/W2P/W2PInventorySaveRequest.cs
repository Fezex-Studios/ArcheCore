using MessagePack;
using ArcheCore.Network.Shared.Packets.PersistenceServer;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer.W2P
{
    /// <summary>
    /// Only the slots that changed since the last confirmed save - never
    /// the full 20-slot array. Separate endpoint from /characters/save on
    /// purpose: gold/level/position are one row's worth of columns and a
    /// full-value UPDATE is idempotent and cheap; inventory is a variable
    /// number of rows and a diff, which needs its own transaction and its
    /// own delete-vs-upsert branch per entry (see Program.cs).
    /// </summary>
    [MessagePackObject(true)]
    public class W2PInventorySaveRequest
    {
        public long CharacterId;
        public int  AccountId;
        public InventorySlotDto[] Changes;
    }
}
