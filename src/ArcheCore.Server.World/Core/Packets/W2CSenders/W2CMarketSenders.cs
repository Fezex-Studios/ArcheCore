using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using LiteNetLib;

namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CMailListPacketSender
    {
        public static void Send(NetPeer peer, MailEntryData[] mail) =>
            WorldserverPacketSender.SendPacket(peer, Opcodes.W2CMailList,
                new W2CMailListPacket { Mail = mail });
    }

    public static class W2CAuctionListPacketSender
    {
        public static void Send(NetPeer peer, AuctionEntryData[] listings, string search, bool mineOnly, int feePercent) =>
            WorldserverPacketSender.SendPacket(peer, Opcodes.W2CAuctionList,
                new W2CAuctionListPacket
                {
                    Listings = listings, Search = search ?? string.Empty,
                    MineOnly = mineOnly, FeePercent = feePercent
                });
    }

    public static class W2CCashShopListPacketSender
    {
        public static void Send(NetPeer peer, CashShopEntryData[] items, int balance) =>
            WorldserverPacketSender.SendPacket(peer, Opcodes.W2CCashShopList,
                new W2CCashShopListPacket { Items = items, Balance = balance });
    }

    /// <summary>Shared by the auction house, the cash shop and the mailbox.</summary>
    public static class W2CMarketResultPacketSender
    {
        /// <summary>refresh: 0 none, 1 auction, 2 cash shop, 3 mail.</summary>
        public static void Send(NetPeer peer, bool success, string message, int refresh = 0) =>
            WorldserverPacketSender.SendPacket(peer, Opcodes.W2CMarketResult,
                new W2CMarketResultPacket { Success = success, Message = message ?? string.Empty, Refresh = refresh });
    }
}
