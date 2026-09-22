using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// A hit landed. Sent to everyone who can see the target (plus the
    /// attacker), the same way W2CJumpEvent goes to everyone who can see
    /// the jumper. Carries the target's health AFTER the hit, so every
    /// client's health bar agrees with the server without extra packets.
    ///
    /// CooldownMs is the skill's cooldown, so the attacker's client can
    /// grey out the attack key instead of sending presses the server will
    /// drop. Other clients ignore it.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CCombatEventPacket
    {
        public int  AttackerId;
        public int  TargetId;
        public int  SkillId;
        public int  Damage;
        public int  TargetHealth;
        public int  TargetMaxHealth;
        public bool Killed;
        public int  CooldownMs;
    }
}
