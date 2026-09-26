using System;
using System.Collections.Generic;
using ArcheCore.Server.World.Core.Services;
using ArcheCore.Server.World.GameData.Effects;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Networking.W2C;
using NLog;

namespace ArcheCore.Server.World.Core.Effects
{
    /// <summary>
    /// Carries out effects (roadmap fix-first #1). The one place health goes
    /// down because something hit it, or up because something healed it.
    ///
    /// Two steps, so a caller can refuse BEFORE it spends anything:
    ///
    ///   Check  - can every effect land? (target alive, not already at full
    ///            health for a heal, mount system present...). Changes
    ///            nothing. On false, `reason` is what to tell the player,
    ///            or null to refuse silently.
    ///   Apply  - do them, in order, and sum what happened.
    ///
    /// Items: Check, then Apply, then consume + start the cooldown - so a
    /// potion at full health isn't wasted. Skills: Check before the
    /// cooldown starts, Apply after - so a swing that can't land never costs
    /// the cooldown. TryApply is both in one call.
    ///
    /// Damage and heals change health and nothing else. Death, the combat
    /// event packet, loot and Lua hooks stay with the caller (CombatManager),
    /// because the caller knows the skill, the cooldown and who gets the
    /// credit. A heal on a player also sends them W2CHealthUpdate, exactly
    /// as the potion code did before.
    ///
    /// Tick thread only.
    /// </summary>
    public sealed class EffectApplier : IInitializable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Random _rng;
        private MountManager _mounts;
        private PetManager _pets;

        public EffectApplier() : this(new Random()) { }

        /// <summary>For tests: a seeded Random.</summary>
        public EffectApplier(Random rng) => _rng = rng;

        public void Initialize(ServiceContainer services)
        {
            services.TryGet(out _mounts);
            services.TryGet(out _pets);
        }

        // ── Check ────────────────────────────────────────────────────

        public bool Check(IReadOnlyList<Effect> effects, in EffectContext context, out string reason)
        {
            reason = null;

            if (effects == null || effects.Count == 0)
                return false;

            for (int i = 0; i < effects.Count; i++)
                if (!CheckOne(effects[i], context, out reason))
                    return false;

            return true;
        }

        private bool CheckOne(Effect effect, in EffectContext context, out string reason)
        {
            reason = null;
            var actor = context.Resolve(effect.Target);

            switch (effect.Kind)
            {
                case EffectKind.Script:
                    return true;

                case EffectKind.Heal:
                    if (!actor.IsValid || !actor.HasHealth) { reason = "No valid target."; return false; }
                    if (actor.IsDead) return false;
                    if (actor.Health >= actor.MaxHealth)
                    {
                        reason = effect.Target == EffectTarget.Self ? "You are already at full health." : "They are already at full health.";
                        return false;
                    }
                    return true;

                case EffectKind.Damage:
                    if (!actor.IsValid || !actor.HasHealth || actor.IsDead) { reason = "No valid target."; return false; }
                    return true;

                case EffectKind.Mount:
                    return actor.IsPlayer && _mounts != null;

                case EffectKind.SummonPet:
                    return actor.IsPlayer && _pets != null;

                case EffectKind.ApplyStatus:
                case EffectKind.CastSkill:
                    if (!actor.IsValid) { reason = "No valid target."; return false; }
                    return true;

                default:
                    Logger.Warn("[Effects] {Origin}: unknown effect kind {Kind} - refused", context.Origin, (int)effect.Kind);
                    return false;
            }
        }

        // ── Apply ────────────────────────────────────────────────────

        /// <summary>
        /// Run every effect in order. Call Check first. Returns false only if
        /// an effect that passed Check still failed (a mount toggle refused):
        /// effects before it have happened, effects after it are skipped.
        /// </summary>
        public bool Apply(IReadOnlyList<Effect> effects, in EffectContext context, out EffectResult result)
        {
            result = default;
            bool targetWasAlive = context.Target.IsValid && !context.Target.IsDead;

            for (int i = 0; i < effects.Count; i++)
            {
                if (!ApplyOne(effects[i], context, ref result))
                {
                    Logger.Debug("[Effects] {Origin}: {Effect} failed after its check - stopped", context.Origin, effects[i]);
                    return false;
                }
            }

            result.TargetKilled = targetWasAlive && context.Target.IsDead;
            return true;
        }

        /// <summary>Check then Apply.</summary>
        public bool TryApply(IReadOnlyList<Effect> effects, in EffectContext context, out EffectResult result, out string reason)
        {
            result = default;
            return Check(effects, context, out reason) && Apply(effects, context, out result);
        }

        private bool ApplyOne(Effect effect, in EffectContext context, ref EffectResult result)
        {
            var actor = context.Resolve(effect.Target);

            switch (effect.Kind)
            {
                case EffectKind.Script:
                    // The caller fires the Lua hook (OnItemUse etc.) after a
                    // successful use - nothing to do here.
                    return true;

                case EffectKind.Heal:
                {
                    if (!actor.IsValid || actor.IsDead) return true;   // nothing to heal; not a failure

                    int before = actor.Health;
                    int after = Math.Min(actor.MaxHealth, before + Math.Max(0, Roll(effect)));
                    actor.SetHealth(after);
                    result.Healed += after - before;

                    if (actor.IsPlayer && actor.Peer != null)
                        W2CHealthUpdatePacketSender.Send(actor.Peer, actor.Health, actor.MaxHealth);
                    return true;
                }

                case EffectKind.Damage:
                {
                    if (!actor.IsValid || actor.IsDead) return true;   // an earlier effect already killed it

                    int before = actor.Health;
                    int after = Math.Max(0, before - Math.Max(0, Roll(effect)));
                    actor.SetHealth(after);

                    if (effect.Target == EffectTarget.Target)
                        result.Damage += before - after;
                    return true;
                }

                case EffectKind.Mount:
                    // Toggles: using it while riding puts you back on foot.
                    return _mounts != null && actor.IsPlayer && _mounts.Toggle(actor.Peer, actor.Player, effect.RefId);

                case EffectKind.SummonPet:
                    return _pets != null && actor.IsPlayer && _pets.Toggle(actor.Peer, actor.Player, effect.RefId);

                case EffectKind.ApplyStatus:
                    Logger.Info("[Effects] STUB {Origin}: status {Status} for {Ms}ms on {Actor} - status effects are Phase 3",
                        context.Origin, effect.RefId, effect.DurationMs, actor);
                    return true;

                case EffectKind.CastSkill:
                    Logger.Info("[Effects] STUB {Origin}: cast skill {Skill} by {Actor} - not wired yet",
                        context.Origin, effect.RefId, actor);
                    return true;

                default:
                    return false;
            }
        }

        private int Roll(Effect effect) =>
            effect.Max > effect.Min ? _rng.Next(effect.Min, effect.Max + 1) : effect.Min;
    }
}
