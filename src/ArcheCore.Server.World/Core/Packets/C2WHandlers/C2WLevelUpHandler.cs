using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using MessagePack;
using NLog;

namespace ArcheCore.Server.World.Networking.C2W;

[PacketOpcode(Opcodes.LevelUp)]
public class C2WLevelUpHandler : IPacketHandler
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly PlayerManager _playerManager;

    public C2WLevelUpHandler(PlayerManager playerManager)
    {
        _playerManager = playerManager;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        MessagePackSerializer.Deserialize<C2WLevelUpPacket>(reader.GetRemainingBytes());

        int newLevel = _playerManager.LevelUp(peer);
        if (newLevel == -1) return;

        Logger.Info($"[LevelUp] {_playerManager.GetCharacterId(peer)} -> level {newLevel}");

        _playerManager.EnqueueAction(() =>
            W2CPlayerLevelResponsePacketSender.Send(peer, newLevel));
    }
}