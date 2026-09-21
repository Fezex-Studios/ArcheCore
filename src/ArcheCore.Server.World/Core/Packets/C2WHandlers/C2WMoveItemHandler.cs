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
/// Not debug-gated - this is a real player action, unlike the two
/// C2WDebug* handlers alongside it. PlayerManager.TryMoveItem does the
/// actual validation (slot range, same-slot no-op); this handler only
/// deserializes and calls it.
/// </summary>
[PacketOpcode(Opcodes.C2WMoveItem)]
public class C2WMoveItemHandler : IPacketHandler
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly PlayerManager _playerManager;

    public C2WMoveItemHandler(PlayerManager playerManager)
    {
        _playerManager = playerManager;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        C2WMoveItemPacket packet =
            MessagePackSerializer.Deserialize<C2WMoveItemPacket>(reader.GetRemainingBytes());

        if (!_playerManager.TryMoveItem(peer, packet.FromSlot, packet.ToSlot))
        {
            // Not logged at Warn - an out-of-range slot index from a
            // legitimate client is a UI bug on THEIR end (clicked past
            // the grid), not evidence of tampering, and will be common
            // during your own testing. Debug is enough to see it while
            // building the UI without treating every misclick as an
            // incident.
            Logger.Debug(
                $"[MoveItem] Rejected {packet.FromSlot} -> {packet.ToSlot} from {peer.Address}");
        }
    }
}
