using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Server.World.Networking.C2W;

/// <summary>One click in the loot window. LootManager.TryTakeOne re-checks owner and range.</summary>
[PacketOpcode(Opcodes.C2WLootTake)]
public class C2WLootTakeHandler : IPacketHandler
{
    private readonly LootManager _loot;

    public C2WLootTakeHandler(LootManager loot)
    {
        _loot = loot;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<C2WLootTakePacket>(reader.GetRemainingBytes());
        _loot.TryTakeOne(peer, packet.CorpseNetworkId, packet.ItemTemplateId);
    }
}
