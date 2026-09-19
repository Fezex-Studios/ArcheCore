using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Utils.Config;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using MessagePack;
using NLog;

namespace ArcheCore.Server.World.Networking.C2W;

/// <summary>
/// DEV ONLY. This opcode is a client-driven "give me a level" button: the
/// packet carries no proof of anything and the server previously granted
/// the level on request, so a client could send opcode 28 in a loop and
/// level to cap in seconds. There is no XP system yet for it to check
/// against, so it cannot be made legitimate here - it can only be closed.
///
/// It stays behind AllowDebugCommands (default FALSE) because it is
/// genuinely useful for testing level-gated content. When a real
/// progression system lands, levels should be granted by the server as a
/// consequence of XP the server awarded, and this handler deleted
/// outright along with the C2W opcode.
/// </summary>
[PacketOpcode(Opcodes.LevelUp)]
public class C2WLevelUpHandler : IPacketHandler
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly PlayerManager _playerManager;
    private readonly WorldServerConfig _config;

    public C2WLevelUpHandler(PlayerManager playerManager, WorldServerConfig config)
    {
        _playerManager = playerManager;
        _config = config;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        MessagePackSerializer.Deserialize<C2WLevelUpPacket>(reader.GetRemainingBytes());

        if (!_config.AllowDebugCommands)
        {
            // Warn, not Debug: on a live shard nothing legitimate sends
            // this, so every hit is either a stale client build or someone
            // probing the opcode table. You want to see it.
            Logger.Warn(
                $"[LevelUp] Rejected client-driven level up from {peer.Address} - " +
                "AllowDebugCommands is false.");
            return;
        }

        int newLevel = _playerManager.LevelUp(peer);
        if (newLevel == -1) return;

        Logger.Info($"[LevelUp] {_playerManager.GetCharacterId(peer)} -> level {newLevel}");

        _playerManager.EnqueueAction(() =>
            W2CPlayerLevelResponsePacketSender.Send(peer, newLevel));
    }
}