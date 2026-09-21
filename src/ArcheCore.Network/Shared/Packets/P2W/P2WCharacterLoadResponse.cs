using MessagePack;
using ArcheCore.Network.Shared.Packets.PersistenceServer;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer.P2W
{
    [MessagePackObject(true)]
    public class P2WCharacterLoadResponse
    {
        public bool   Found;
        public int    AccountId;
        public long   CharacterId;
        public string Name;
        public int    Level;
        public float  X;
        public float  Y;
        public float  Z;
        public int    Gold;

        /// <summary>
        /// Occupied inventory slots only - an empty inventory is an empty
        /// array, not 20 zeroed entries. PlayerSpawnManager reconstructs
        /// the dense 20-slot session.Inventory array from this on spawn.
        /// </summary>
        public InventorySlotDto[] Inventory;
    }
}
