using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using MessagePack;
using NLog;

namespace ArcheCore.Server.World.Networking.C2W;

[PacketOpcode(Opcodes.ItemRequestData)]
public class C2WItemRequestDataHandler : IPacketHandler
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly ItemManager _itemManager;

    public C2WItemRequestDataHandler(ItemManager itemManager)
    {
        _itemManager = itemManager;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer
            .Deserialize<C2WItemRequestDataPacket>(reader.GetRemainingBytes());

        var item = _itemManager.GetById(packet.ItemId);

        if (item == null)
        {
            Logger.Info($"[ItemRequestData] Unknown item id {packet.ItemId}");
            W2CItemDataResponsePacketSender.SendNotFound(peer, packet.ItemId);
            return;
        }

        Logger.Info($"[ItemRequestData] {packet.ItemId} -> {item.name}");
        W2CItemDataResponsePacketSender.Send(peer, item);
    }
}
