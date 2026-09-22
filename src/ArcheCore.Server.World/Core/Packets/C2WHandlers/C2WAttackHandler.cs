using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Server.World.Networking.C2W;

/// <summary>Deserialize and hand to CombatManager.TryAttack, which does every check.</summary>
[PacketOpcode(Opcodes.C2WAttack)]
public class C2WAttackHandler : IPacketHandler
{
    private readonly CombatManager _combat;

    public C2WAttackHandler(CombatManager combat)
    {
        _combat = combat;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<C2WAttackPacket>(reader.GetRemainingBytes());
        _combat.TryAttack(peer, packet.TargetNetworkId, packet.SkillId);
    }
}
