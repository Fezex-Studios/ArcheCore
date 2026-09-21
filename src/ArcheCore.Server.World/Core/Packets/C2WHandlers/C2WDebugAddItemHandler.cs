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

/// <summary>DEV ONLY - same gate, same reasoning, same eventual deletion
/// as C2WDebugAddGoldHandler. See that file's doc comment.</summary>
[PacketOpcode(Opcodes.C2WDebugAddItem)]
public class C2WDebugAddItemHandler : IPacketHandler
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly PlayerManager _playerManager;
    private readonly WorldServerConfig _config;

    public C2WDebugAddItemHandler(PlayerManager playerManager, WorldServerConfig config)
    {
        _playerManager = playerManager;
        _config = config;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        C2WDebugAddItemPacket packet =
            MessagePackSerializer.Deserialize<C2WDebugAddItemPacket>(reader.GetRemainingBytes());

        if (!_config.AllowDebugCommands)
        {
            Logger.Warn(
                $"[DebugAddItem] Rejected client-driven item grant from {peer.Address} - " +
                "AllowDebugCommands is false.");
            return;
        }

        if (!_playerManager.TryAddItem(peer, packet.ItemTemplateId, packet.Quantity))
        {
            Logger.Warn(
                $"[DebugAddItem] {peer.Address} - inventory full, item {packet.ItemTemplateId} not granted.");
            return;
        }

        Logger.Info(
            $"[DebugAddItem] {_playerManager.GetCharacterId(peer)} += " +
            $"{packet.Quantity}x item {packet.ItemTemplateId}");
    }
}
