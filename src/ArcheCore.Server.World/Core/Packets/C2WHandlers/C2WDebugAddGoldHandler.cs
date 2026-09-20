using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Utils.Config;
using LiteNetLib;
using MessagePack;
using NLog;

namespace ArcheCore.Server.World.Networking.C2W;

/// <summary>
/// DEV ONLY - same shape as C2WLevelUpHandler, for the same reason. This
/// packet carries no proof of anything; it exists only to test the
/// currency round-trip (log in, gain gold, disconnect, log back in,
/// balance survived) before any real system - shop, quest reward, loot -
/// produces gold on its own.
///
/// Delete this handler and its opcode once a real gold source exists and
/// you no longer need a manual lever to test the save path.
/// </summary>
[PacketOpcode(Opcodes.C2WDebugAddGold)]
public class C2WDebugAddGoldHandler : IPacketHandler
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly PlayerManager _playerManager;
    private readonly WorldServerConfig _config;

    public C2WDebugAddGoldHandler(PlayerManager playerManager, WorldServerConfig config)
    {
        _playerManager = playerManager;
        _config = config;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        C2WDebugAddGoldPacket packet =
            MessagePackSerializer.Deserialize<C2WDebugAddGoldPacket>(
                reader.GetRemainingBytes());

        if (!_config.AllowDebugCommands)
        {
            Logger.Warn(
                $"[DebugAddGold] Rejected client-driven gold change from {peer.Address} - " +
                "AllowDebugCommands is false.");
            return;
        }

        bool ok = _playerManager.TryAddGold(peer, packet.Amount);

        if (!ok)
        {
            Logger.Warn(
                $"[DebugAddGold] Rejected: {packet.Amount} would have taken " +
                $"{peer.Address} below zero gold.");
            return;
        }

        Logger.Info($"[DebugAddGold] {_playerManager.GetCharacterId(peer)} gold delta {packet.Amount}");
    }
}