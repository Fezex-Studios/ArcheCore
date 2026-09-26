using ArcheCore.Server.World.Core.Entities;
using ArcheCore.Server.World.GameData.Effects;
using ArcheCore.Server.World.Managers;
using LiteNetLib;

namespace ArcheCore.Server.World.Core.Effects
{
    /// <summary>
    /// One thing that happens: "heal self 50", "damage target 5-10",
    /// "mount 1". Items, skills and NPC swings are all lists of these, and
    /// EffectApplier is the one place they're carried out (roadmap
    /// fix-first #1). Phase 3 status effects, a DoT tick or a world boss's
    /// AoE become more Effects, not a fourth copy of "roll damage, clamp,
    /// check death".
    ///
    /// Immutable, loaded once (EffectCatalog), shared by every use.
    /// </summary>
    public sealed record Effect(
        EffectKind   Kind,
        EffectTarget Target,
        int          Min        = 0,
        int          Max        = 0,
        int          RefId      = 0,
        int          DurationMs = 0,
        string       Script     = null)
    {
        public static Effect Damage(int min, int max, EffectTarget target = EffectTarget.Target) =>
            new(EffectKind.Damage, target, min, max);

        public static Effect Heal(int min, int max, EffectTarget target = EffectTarget.Self) =>
            new(EffectKind.Heal, target, min, max);

        public override string ToString() => Kind switch
        {
            EffectKind.Damage or EffectKind.Heal => $"{Kind} {Target} {Min}-{Max}",
            EffectKind.Script                    => $"Script {Script ?? "(default hook)"}",
            _                                    => $"{Kind} {Target} #{RefId}"
        };
    }

    /// <summary>
    /// Something an effect can land on: a player (session + peer) or an NPC.
    /// Default = nobody.
    /// </summary>
    public readonly struct EffectActor
    {
        public readonly NetPeer       Peer;
        public readonly PlayerSession Player;
        public readonly NpcEntity     Npc;

        private EffectActor(NetPeer peer, PlayerSession player, NpcEntity npc)
        {
            Peer = peer;
            Player = player;
            Npc = npc;
        }

        public static EffectActor Of(NetPeer peer, PlayerSession player) => new(peer, player, null);
        public static EffectActor Of(NpcEntity npc) => new(null, null, npc);
        public static readonly EffectActor None = default;

        public bool IsValid  => Player != null || Npc != null;
        public bool IsPlayer => Player != null;

        public int NetworkId => Player?.NetworkId ?? Npc?.NetworkId ?? 0;
        public string Name   => Player?.Name ?? Npc?.Name ?? "nobody";

        public int Health    => Player != null ? Player.Combat.Health    : Npc?.Health ?? 0;
        public int MaxHealth => Player != null ? Player.Combat.MaxHealth : Npc?.MaxHealth ?? 0;

        /// <summary>Has health at all. Unattackable NPCs (vendors, pets) have MaxHealth 0.</summary>
        public bool HasHealth => MaxHealth > 0;
        public bool IsDead    => Player != null ? Player.Combat.IsDead : Npc != null && Npc.IsDead;

        internal void SetHealth(int value)
        {
            if (Player != null) Player.Combat.Health = value;
            else if (Npc != null) Npc.Health = value;
        }

        public override string ToString() => IsValid ? $"{(IsPlayer ? "player" : "npc")} {NetworkId} ({Name})" : "nobody";
    }

    /// <summary>Who caused the effects and at whom, plus a label for logs ("item 11", "skill 1").</summary>
    public readonly struct EffectContext
    {
        public readonly EffectActor Source;
        public readonly EffectActor Target;
        public readonly string      Origin;

        public EffectContext(EffectActor source, EffectActor target, string origin)
        {
            Source = source;
            Target = target;
            Origin = origin;
        }

        /// <summary>Using something on yourself (items).</summary>
        public static EffectContext OnSelf(NetPeer peer, PlayerSession player, string origin)
        {
            var self = EffectActor.Of(peer, player);
            return new EffectContext(self, EffectActor.None, origin);
        }

        public EffectActor Resolve(EffectTarget target) =>
            target == EffectTarget.Self ? Source : Target;
    }

    /// <summary>What a list of effects did, summed. The caller turns it into packets (combat event, etc).</summary>
    public struct EffectResult
    {
        /// <summary>Damage dealt to the context's Target (after clamping at 0 health).</summary>
        public int Damage;

        /// <summary>Health actually restored (after clamping at max).</summary>
        public int Healed;

        /// <summary>The context's Target went from alive to dead.</summary>
        public bool TargetKilled;
    }
}
