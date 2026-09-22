using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Server.World.Networking.C2W;

/// <summary>Deserialize and hand to ShopManager.TrySell, which does every check.</summary>
[PacketOpcode(Opcodes.C2WShopSell)]
public class C2WShopSellHandler : IPacketHandler
{
    private readonly ShopManager _shops;

    public C2WShopSellHandler(ShopManager shops)
    {
        _shops = shops;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<C2WShopSellPacket>(reader.GetRemainingBytes());
        _shops.TrySell(peer, packet.NpcNetworkId, packet.Slot, packet.Quantity);
    }
}
