using LiteNetLib;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;

namespace ArcheCore.Server.World.Networking.W2C
{
    /// <summary>
    /// See W2CEnterWorldPacket. One send, called once, from
    /// PlayerSpawnManager.SpawnPlayer.
    ///
    /// Sends the inventory DENSE - every slot index 0..SlotCount-1,
    /// including empty ones - unlike W2PInventorySaveRequest, which is
    /// sparse. The client builds a fixed grid and wants to write every
    /// cell exactly once; making it infer "not in the array means empty"
    /// would be one more place for a stale slot to survive a relog.
    /// </summary>
    public static class W2CEnterWorldPacketSender
    {
        public static void Send(NetPeer peer, CharacterData character, int gold, InventorySlot[] inventory, int health, int maxHealth, WorldSettingsData world)
        {
            var slots = new InventorySlotData[inventory.Length];

            for (int i = 0; i < inventory.Length; i++)
            {
                slots[i] = new InventorySlotData
                {
                    Index          = i,
                    ItemTemplateId = inventory[i].ItemTemplateId,
                    Quantity       = inventory[i].Quantity
                };
            }

            WorldserverPacketSender.SendPacket(
                peer,
                Opcodes.W2CEnterWorld,
                new W2CEnterWorldPacket
                {
                    Character = character,
                    Gold      = gold,
                    Inventory = slots,
                    Health    = health,
                    MaxHealth = maxHealth,
                    World     = world
                });
        }
    }
}