using System.Linq;
using LiteNetLib;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;

namespace ArcheCore.Server.World.Networking.W2C
{
    /// <summary>Both inventory senders together - same trivial shape as
    /// W2CGoldUpdatePacketSender, kept in one file since neither is more
    /// than a few lines.</summary>
    public static class W2CInventorySnapshotPacketSender
    {
        public static void Send(NetPeer peer, InventorySlot[] inventory)
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
                Opcodes.W2CInventorySnapshot,
                new W2CInventorySnapshotPacket { Slots = slots });
        }
    }

    public static class W2CInventorySlotChangedPacketSender
    {
        public static void Send(NetPeer peer, int index, InventorySlot slot)
        {
            WorldserverPacketSender.SendPacket(
                peer,
                Opcodes.W2CInventorySlotChanged,
                new W2CInventorySlotChangedPacket
                {
                    Index          = index,
                    ItemTemplateId = slot.ItemTemplateId,
                    Quantity       = slot.Quantity
                });
        }
    }
}
