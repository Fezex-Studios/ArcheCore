using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Server.World.Networking.C2W;

/// <summary>
/// Claim mail from the mailbox.
///
/// Every handler here starts the work and returns: the services are an HTTP
/// hop away and the tick thread never waits on them. The managers hop back
/// onto the tick thread before touching gold or inventory.
/// </summary>
[PacketOpcode(Opcodes.C2WMailClaim)]
public class C2WMailClaimHandler : IPacketHandler
{
    private readonly MailManager _mail;
    private readonly PlayerManager _players;

    public C2WMailClaimHandler(MailManager mail, PlayerManager players)
    {
        _mail = mail;
        _players = players;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<C2WMailClaimPacket>(reader.GetRemainingBytes());

        if (_players.TryGetSession(peer, out var session))
            _ = _mail.ClaimAsync(peer, session, packet.MailId);
    }
}

[PacketOpcode(Opcodes.C2WAuctionBrowse)]
public class C2WAuctionBrowseHandler : IPacketHandler
{
    private readonly AuctionManager _auctions;
    private readonly PlayerManager _players;

    public C2WAuctionBrowseHandler(AuctionManager auctions, PlayerManager players)
    {
        _auctions = auctions;
        _players = players;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<C2WAuctionBrowsePacket>(reader.GetRemainingBytes());

        if (_players.TryGetSession(peer, out var session))
            _ = _auctions.BrowseAsync(peer, session, packet.Search, packet.MineOnly);
    }
}

[PacketOpcode(Opcodes.C2WAuctionCreate)]
public class C2WAuctionCreateHandler : IPacketHandler
{
    private readonly AuctionManager _auctions;
    private readonly PlayerManager _players;

    public C2WAuctionCreateHandler(AuctionManager auctions, PlayerManager players)
    {
        _auctions = auctions;
        _players = players;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<C2WAuctionCreatePacket>(reader.GetRemainingBytes());

        // Taking the item happens on this thread; writing the row doesn't.
        if (_players.TryGetSession(peer, out var session))
            _auctions.Create(peer, session, packet.Slot, packet.Quantity, packet.Price);
    }
}

[PacketOpcode(Opcodes.C2WAuctionBuy)]
public class C2WAuctionBuyHandler : IPacketHandler
{
    private readonly AuctionManager _auctions;
    private readonly PlayerManager _players;

    public C2WAuctionBuyHandler(AuctionManager auctions, PlayerManager players)
    {
        _auctions = auctions;
        _players = players;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<C2WAuctionBuyPacket>(reader.GetRemainingBytes());

        if (_players.TryGetSession(peer, out var session))
            _ = _auctions.BuyAsync(peer, session, packet.AuctionId);
    }
}

[PacketOpcode(Opcodes.C2WAuctionCancel)]
public class C2WAuctionCancelHandler : IPacketHandler
{
    private readonly AuctionManager _auctions;
    private readonly PlayerManager _players;

    public C2WAuctionCancelHandler(AuctionManager auctions, PlayerManager players)
    {
        _auctions = auctions;
        _players = players;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<C2WAuctionCancelPacket>(reader.GetRemainingBytes());

        if (_players.TryGetSession(peer, out var session))
            _ = _auctions.CancelAsync(peer, session, packet.AuctionId);
    }
}

[PacketOpcode(Opcodes.C2WCashShopBrowse)]
public class C2WCashShopBrowseHandler : IPacketHandler
{
    private readonly CashShopManager _cashShop;
    private readonly PlayerManager _players;

    public C2WCashShopBrowseHandler(CashShopManager cashShop, PlayerManager players)
    {
        _cashShop = cashShop;
        _players = players;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        MessagePackSerializer.Deserialize<C2WCashShopBrowsePacket>(reader.GetRemainingBytes());

        if (_players.TryGetSession(peer, out var session))
            _ = _cashShop.BrowseAsync(peer, session);
    }
}

[PacketOpcode(Opcodes.C2WCashShopBuy)]
public class C2WCashShopBuyHandler : IPacketHandler
{
    private readonly CashShopManager _cashShop;
    private readonly PlayerManager _players;

    public C2WCashShopBuyHandler(CashShopManager cashShop, PlayerManager players)
    {
        _cashShop = cashShop;
        _players = players;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<C2WCashShopBuyPacket>(reader.GetRemainingBytes());

        if (_players.TryGetSession(peer, out var session))
            _ = _cashShop.BuyAsync(peer, session, packet.CashShopItemId);
    }
}

[PacketOpcode(Opcodes.C2WCashShopGift)]
public class C2WCashShopGiftHandler : IPacketHandler
{
    private readonly CashShopManager _cashShop;
    private readonly PlayerManager _players;

    public C2WCashShopGiftHandler(CashShopManager cashShop, PlayerManager players)
    {
        _cashShop = cashShop;
        _players = players;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<C2WCashShopGiftPacket>(reader.GetRemainingBytes());

        if (_players.TryGetSession(peer, out var session))
            _ = _cashShop.GiftAsync(peer, session, packet.CashShopItemId, packet.RecipientName);
    }
}
