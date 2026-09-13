using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.GameData.Items;
using LiteNetLib;

namespace ArcheCore.Server.World.Networking.W2C;

public static class W2CItemDataResponsePacketSender
{
    public static void Send(NetPeer peer, ItemTable item)
    {
        WorldserverPacketSender.SendPacket(
            peer,
            Opcodes.ItemDataResponse,
            new W2CItemDataResponsePacket
            {
                Found       = true,
                ItemId      = item.item_id,
                Name        = item.name,
                Description = item.description,
                IconName    = item.icon_name
            });
    }

    public static void SendNotFound(NetPeer peer, int itemId)
    {
        WorldserverPacketSender.SendPacket(
            peer,
            Opcodes.ItemDataResponse,
            new W2CItemDataResponsePacket { Found = false, ItemId = itemId });
    }
}
