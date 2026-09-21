using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using MessagePack;
using NLog;

namespace ArcheCore.Server.World.Networking.C2W;

/// <summary>
/// Real player action, not debug-gated. PlayerManager.TryUseItem does all
/// of it - usable check, cooldown, effect, consume; this only deserializes.
/// </summary>
[PacketOpcode(Opcodes.C2WUseItem)]
public class C2WUseItemHandler : IPacketHandler
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly PlayerManager _playerManager;

    public C2WUseItemHandler(PlayerManager playerManager)
    {
        _playerManager = playerManager;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        C2WUseItemPacket packet =
            MessagePackSerializer.Deserialize<C2WUseItemPacket>(reader.GetRemainingBytes());

        // Debug: not usable / on cooldown is normal play (spam-clicking a
        // potion), not an error.
        if (!_playerManager.TryUseItem(peer, packet.Slot))
            Logger.Debug($"[UseItem] Rejected slot {packet.Slot} from {peer.Address}");
    }
}