using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using LiteNetLib;

namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CShopOpenPacketSender
    {
        public static void Send(NetPeer peer, int npcNetworkId, int shopId, string shopName, ShopItemData[] items)
        {
            WorldserverPacketSender.SendPacket(
                peer,
                Opcodes.W2CShopOpen,
                new W2CShopOpenPacket
                {
                    NpcNetworkId = npcNetworkId,
                    ShopId       = shopId,
                    ShopName     = shopName,
                    Items        = items
                });
        }
    }

    public static class W2CShopResultPacketSender
    {
        public static void Send(NetPeer peer, bool success, string message)
        {
            WorldserverPacketSender.SendPacket(
                peer,
                Opcodes.W2CShopResult,
                new W2CShopResultPacket { Success = success, Message = message });
        }
    }
}
