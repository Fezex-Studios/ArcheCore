using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    /// <summary>
    /// "Use SkillId on TargetNetworkId." No damage, position or timing is
    /// sent - the server checks the cooldown, range and target itself and
    /// rolls the damage. See CombatManager.TryAttack.
    /// </summary>
    [MessagePackObject(true)]
    public class C2WAttackPacket
    {
        public int TargetNetworkId;
        public int SkillId;
    }
}
