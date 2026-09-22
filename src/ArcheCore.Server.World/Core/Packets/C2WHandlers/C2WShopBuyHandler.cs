using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Server.World.Networking.C2W;

/// <summary>Deserialize and hand to ShopManager.TryBuy, which does every check.</summary>
[PacketOpcode(Opcodes.C2WShopBuy)]
public class C2WShopBuyHandler : IPacketHandler
{
    private readonly ShopManager _shops;

    public C2WShopBuyHandler(ShopManager shops)
    {
        _shops = shops;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<C2WShopBuyPacket>(reader.GetRemainingBytes());
        _shops.TryBuy(peer, packet.NpcNetworkId, packet.ItemTemplateId, packet.Quantity);
    }
}
