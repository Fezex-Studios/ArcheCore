using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Server.World.Networking.C2W;

/// <summary>The Respawn button. CombatManager.TryRespawn ignores it unless the player is actually dead.</summary>
[PacketOpcode(Opcodes.C2WRespawn)]
public class C2WRespawnHandler : IPacketHandler
{
    private readonly CombatManager _combat;

    public C2WRespawnHandler(CombatManager combat)
    {
        _combat = combat;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        MessagePackSerializer.Deserialize<C2WRespawnPacket>(reader.GetRemainingBytes());
        _combat.TryRespawn(peer);
    }
}
