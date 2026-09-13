using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Server.World.Networking.C2W;

[PacketOpcode(Opcodes.PlayerSpawned)]
public class C2WPlayerSpawnedHandler : IPacketHandler
{
    private readonly PlayerManager _playerManager;

    public C2WPlayerSpawnedHandler(PlayerManager playerManager)
    {
        _playerManager = playerManager;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        MessagePackSerializer.Deserialize<C2WPlayerSpawnedPacket>(reader.GetRemainingBytes());

        int level = _playerManager.GetLevel(peer);
        if (level == -1) return;

        var data = new CharacterData { Level = level, Name = _playerManager.GetName(peer) };

        _playerManager.EnqueueAction(() =>
            W2CCharacterDataPacketSender.Send(peer, data));
    }
}