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
/// Real player action, not debug-gated. Validation lives in
/// PlayerManager.TryDropItem; this only deserializes and calls it.
/// </summary>
[PacketOpcode(Opcodes.C2WDropItem)]
public class C2WDropItemHandler : IPacketHandler
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly PlayerManager _playerManager;

    public C2WDropItemHandler(PlayerManager playerManager)
    {
        _playerManager = playerManager;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        C2WDropItemPacket packet =
            MessagePackSerializer.Deserialize<C2WDropItemPacket>(reader.GetRemainingBytes());

        if (!_playerManager.TryDropItem(peer, packet.Slot, packet.Quantity))
        {
            // Debug, same reasoning as C2WMoveItemHandler: an empty or
            // out-of-range slot from a real client is a UI bug, not tampering.
            Logger.Debug($"[DropItem] Rejected slot {packet.Slot} x{packet.Quantity} from {peer.Address}");
        }
    }
}