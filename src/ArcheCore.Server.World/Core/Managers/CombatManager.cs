using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Core.Effects;
using ArcheCore.Server.World.Core.Services;
using ArcheCore.Server.World.Utils.Config;
using ArcheCore.Server.World.GameData.Combat;
using ArcheCore.Server.World.Networking.W2C;
using ArcheCore.Server.World.Utils.Database.SQLite;
using LiteNetLib;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Melee combat. Roadmap H: one skill, players hitting NPCs, range-only.
    ///
    /// C2WAttack -> TryAttack, checked in this order:
    ///
    ///   1. Attacker is in the world and alive
    ///   2. Skill exists                          (Skills table)
    ///   3. Skill is off cooldown                 (dropped silently - see below)
    ///   4. Target is a live NPC that CAN be hit  (MaxHealth > 0)
    ///   5. Target is in range                    (no line of sight yet)
    ///   6. Start the cooldown, apply the skill's effects (EffectApplier -
    ///      damage today; the Effects table decides)
    ///   7. W2CCombatEvent to everyone who can see the target, plus the
    ///      attacker - the same fan-out as JumpEventBroadcaster
    ///   8. If that killed it: NpcAiManager.KillNpc (despawn + respawn
    ///      queue), LootManager.CreateCorpse, Lua OnKill
    ///
    /// A too-far or invalid-target attack is refused BEFORE the cooldown
    /// starts, so pressing attack out of range never costs you a swing.
    ///
    /// On-cooldown presses are dropped without a reply. The client already
    /// knows the cooldown (every combat event carries it) and greys the key
    /// out, so the only way to hit this is a double-press on a laggy
    /// connection - answering each one with "not ready" would just spam.
    ///
    /// NPCs FIGHT BACK (roadmap J): NpcAiManager calls NpcAttack when an
    /// aggressive NPC is in reach and off cooldown. It runs the same
    /// check-then-apply shape as a player's attack, and the hit goes out as
    /// the same W2CCombatEvent - so a player being hit and an NPC being hit
    /// are one code path and one packet, not two.
    ///
    /// PLAYER VS PLAYER is off unless AllowPlayerVersusPlayer is set in the
    /// world server config, and even then not inside a safe zone (any spawn
    /// point with a SafeRadius) - checked for attacker AND victim, so a town
    /// can't be shot into from outside it.
    ///
    /// At zero health a player DIES: they stay in the world where they fell,
    /// can't act, and every NPC chasing them goes home. They press Respawn
    /// (C2WRespawn -> TryRespawn) to come back at the spawn point, at full
    /// health. Nothing is dropped or lost - what death costs is a decision
    /// for later, and this is the hook it will hang off.
    /// </summary>
    public class CombatManager : IInitializable
    {
        private EffectCatalog _effectCatalog;
        private EffectApplier _effects;

        /// <summary>
        /// Two-phase start-up. What a hit DOES - damage today, a slow or a
        /// DoT later - is the skill's effect list, carried out by the shared
        /// EffectApplier (roadmap fix-first #1). This class decides WHETHER
        /// a hit happens (range, cooldown, PvP rules) and reports it.
        /// </summary>
        public void Initialize(ServiceContainer services)
        {
            _effectCatalog = services.Get<EffectCatalog>();
            _effects = services.Get<EffectApplier>();
        }

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>Slack on the range check, so standing right at the edge doesn't flicker.</summary>
        private const float RangeTolerance = 0.5f;

        private readonly IDbContextFactory<WorldDataDbContext> _dbFactory;
        private readonly PlayerManager _players;
        private readonly SpawnManager _spawnManager;
        private readonly NpcAiManager _npcAi;
        private readonly LootManager _loot;
        private readonly HarvestManager _harvest;
        private readonly SpawnPointService _spawnPoints;
        private readonly WorldServerConfig _config;
        private readonly InterestManager _interest;
        private readonly ReplicationManager _replication;

        private Dictionary<int, SkillTemplate> _skills = new();
        private readonly List<NetPeer> _observerScratch = new();

        public CombatManager(
            IDbContextFactory<WorldDataDbContext> dbFactory,
            PlayerManager players,
            SpawnManager spawnManager,
            NpcAiManager npcAi,
            LootManager loot,
            HarvestManager harvest,
            SpawnPointService spawnPoints,
            WorldServerConfig config,
            InterestManager interest,
            ReplicationManager replication)
        {
            _config = config;
            _harvest = harvest;
            _spawnPoints = spawnPoints;
            _dbFactory = dbFactory;
            _players = players;
            _spawnManager = spawnManager;
            _npcAi = npcAi;
            _loot = loot;
            _interest = interest;
            _replication = replication;
        }

        public void LoadFromDatabase()
        {
            using var db = _dbFactory.CreateDbContext();

            _skills = new Dictionary<int, SkillTemplate>();
            foreach (var s in db.Skills.ToList())
            {
                string problem =
                    s.Range <= 0                ? "Range must be positive" :
                    s.CooldownMs < 0            ? "CooldownMs is negative" :
                    s.MinDamage < 0             ? "MinDamage is negative" :
                    s.MaxDamage < s.MinDamage   ? "MaxDamage is below MinDamage" :
                    null;

                if (problem != null)
                {
                    Logger.Warn("[Combat] Skill {Id} '{Name}' skipped: {Problem}", s.Id, s.Name, problem);
                    continue;
                }

                _skills[s.Id] = s;
            }

            Logger.Info("[Combat] Loaded {Count} skill(s).", _skills.Count);
        }

        public void TryAttack(NetPeer peer, int targetNetworkId, int skillId)
        {
            // 1
            if (!_players.TryGetSession(peer, out var session) || session.NetworkId is not int attackerId)
                return;

            if (session.Combat.IsDead)
            {
                W2CInteractDeniedPacketSender.Send(peer, "You can't fight while dead.");
                return;
            }

            // 2
            if (!_skills.TryGetValue(skillId, out var skill))
            {
                Logger.Debug("[Combat] Unknown skill {Skill} from account {Account}", skillId, session.AccountId);
                return;
            }

            // 3
            long now = ArcheCore.Server.World.ServerClock.NowMs;
            if (session.Combat.SkillCooldowns.TryGetValue(skillId, out long readyAt) && now < readyAt)
                return;

            // 4 - a player target is PvP, and goes its own way
            if (!SpawnManager.IsNpcId(targetNetworkId))
            {
                AttackPlayer(peer, session, attackerId, targetNetworkId, skill, now);
                return;
            }

            if (!_spawnManager.TryGetNpc(targetNetworkId, out var npc) || npc.IsDead)
            {
                W2CInteractDeniedPacketSender.Send(peer, "No valid target.");
                return;
            }

            if (!npc.IsAttackable)
            {
                W2CInteractDeniedPacketSender.Send(peer, "You can't attack that.");
                return;
            }

            // 5
            if (Vector3.Distance(session.Position, npc.Position) > skill.Range + RangeTolerance)
            {
                W2CInteractDeniedPacketSender.Send(peer, "Too far away.");
                return;
            }

            var effects = _effectCatalog.ForSkill(skill);
            var context = new EffectContext(EffectActor.Of(peer, session), EffectActor.Of(npc), $"skill {skill.Id}");

            if (!_effects.Check(effects, context, out string refused))
            {
                if (refused != null) W2CInteractDeniedPacketSender.Send(peer, refused);
                return;
            }

            // 6
            session.Combat.SkillCooldowns[skillId] = now + skill.CooldownMs;

            // Hitting something makes it fight back, whatever its aggro radius.
            _npcAi.OnNpcAttacked(npc.NetworkId, attackerId);

            // You can't swing from the saddle.
            _players.Mounts?.Dismount(peer, session, "You dismount to fight.");

            _effects.Apply(effects, context, out var hit);
            int damage = hit.Damage;
            bool killed = npc.IsDead;

            // 7 - before any death handling, which empties the observer list.
            Broadcast(peer, npc.NetworkId, new W2CCombatEventPacket
            {
                AttackerId      = attackerId,
                TargetId        = npc.NetworkId,
                SkillId         = skill.Id,
                Damage          = damage,
                TargetHealth    = npc.Health,
                TargetMaxHealth = npc.MaxHealth,
                Killed          = killed,
                CooldownMs      = skill.CooldownMs
            });

            // 8
            if (killed)
            {
                Logger.Info("[Combat] Account {Account} killed {Npc} ({Id})", session.AccountId, npc.Name, npc.NetworkId);

                _npcAi.KillNpc(npc);
                _loot.CreateCorpse(npc, attackerId);
                _players.FireKillEvent(peer, npc.TemplateId);
            }
        }

        // ── Player vs player ─────────────────────────────────────────

        /// <summary>
        /// One player attacking another. Same shape as attacking an NPC -
        /// check everything, then apply - with two extra gates: PvP has to be
        /// switched on for this server, and neither of them may be standing
        /// in a safe zone.
        /// </summary>
        private void AttackPlayer(NetPeer attackerPeer, PlayerSession attacker, int attackerId, int targetId, SkillTemplate skill, long now)
        {
            if (targetId == attackerId)
                return;

            if (!_config.AllowPlayerVersusPlayer)
            {
                W2CInteractDeniedPacketSender.Send(attackerPeer, "You can't attack other players here.");
                return;
            }

            if (!_players.TryGetPeer(targetId, out var targetPeer) ||
                !_players.TryGetSession(targetPeer, out var target) ||
                target.NetworkId != targetId ||
                target.Combat.IsDead)
            {
                W2CInteractDeniedPacketSender.Send(attackerPeer, "No valid target.");
                return;
            }

            if (_spawnPoints.IsInSafeZone(attacker.Position, out var attackerZone))
            {
                W2CInteractDeniedPacketSender.Send(attackerPeer, $"You can't fight in {attackerZone}.");
                return;
            }

            if (_spawnPoints.IsInSafeZone(target.Position, out var targetZone))
            {
                W2CInteractDeniedPacketSender.Send(attackerPeer, $"They're protected in {targetZone}.");
                return;
            }

            // Zone rules, for attacker AND victim - same reasoning as safe
            // zones above: a sanctuary can't be shot into from outside it.
            var zones = _players.Zones;
            if (zones != null)
            {
                if (!zones.AllowsPvpAt(attacker.Position, out var attackerZoneDef))
                {
                    W2CInteractDeniedPacketSender.Send(attackerPeer, $"You can't fight other players in {attackerZoneDef.DisplayName}.");
                    return;
                }

                if (!zones.AllowsPvpAt(target.Position, out var targetZoneDef))
                {
                    W2CInteractDeniedPacketSender.Send(attackerPeer, $"They're protected in {targetZoneDef.DisplayName}.");
                    return;
                }
            }

            if (Vector3.Distance(attacker.Position, target.Position) > skill.Range + RangeTolerance)
            {
                W2CInteractDeniedPacketSender.Send(attackerPeer, "Too far away.");
                return;
            }

            var effects = _effectCatalog.ForSkill(skill);
            var context = new EffectContext(EffectActor.Of(attackerPeer, attacker), EffectActor.Of(targetPeer, target), $"skill {skill.Id}");

            if (!_effects.Check(effects, context, out string refused))
            {
                if (refused != null) W2CInteractDeniedPacketSender.Send(attackerPeer, refused);
                return;
            }

            attacker.Combat.SkillCooldowns[skill.Id] = now + skill.CooldownMs;

            _effects.Apply(effects, context, out var hit);
            int damage = hit.Damage;
            bool killed = target.Combat.IsDead;

            Broadcast(attackerPeer, targetId, new W2CCombatEventPacket
            {
                AttackerId      = attackerId,
                TargetId        = targetId,
                SkillId         = skill.Id,
                Damage          = damage,
                TargetHealth    = target.Combat.Health,
                TargetMaxHealth = target.Combat.MaxHealth,
                Killed          = killed,
                CooldownMs      = skill.CooldownMs
            });

            // The victim always hears about their own health, even if the
            // interest grid hasn't paired them with the attacker.
            W2CHealthUpdatePacketSender.Send(targetPeer, target.Combat.Health, target.Combat.MaxHealth);

            if (killed)
            {
                Logger.Info("[Combat] Account {Account} was killed by account {Killer} (PvP)", target.AccountId, attacker.AccountId);
                KillPlayer(targetPeer, target, targetId, attacker.Name ?? "another player", killerTemplateId: 0);
            }
        }

        // ── NPCs hitting players (roadmap J) ─────────────────────────

        /// <summary>
        /// An NPC swings at a player. Called from NpcAiManager on the tick
        /// thread; it re-checks everything itself, because the AI decided to
        /// attack up to a tick ago and the player may have moved or died.
        /// </summary>
        public void NpcAttack(int npcNetworkId, int playerId)
        {
            if (!_spawnManager.TryGetNpc(npcNetworkId, out var npc) || npc.IsDead || !npc.IsAggressive)
                return;

            if (!_players.TryGetPeer(playerId, out var peer) ||
                !_players.TryGetSession(peer, out var session) ||
                session.NetworkId != playerId ||
                session.Combat.IsDead)
                return;

            if (Vector3.Distance(npc.Position, session.Position) > npc.AttackRange + 1f)
                return;

            // An NPC's swing is a Damage effect like any skill's, so the one
            // damage rule (roll, clamp at 0) applies to both directions.
            var swing = _effectCatalog.ForNpcSwing(npc.AttackDamageMin, npc.AttackDamageMax);
            _effects.Apply(swing, new EffectContext(EffectActor.Of(npc), EffectActor.Of(peer, session), $"npc {npc.TemplateId} swing"), out var hit);
            int damage = hit.Damage;
            bool killed = session.Combat.IsDead;

            Broadcast(peer, playerId, new W2CCombatEventPacket
            {
                AttackerId      = npc.NetworkId,
                TargetId        = playerId,
                SkillId         = 0,
                Damage          = damage,
                TargetHealth    = session.Combat.Health,
                TargetMaxHealth = session.Combat.MaxHealth,
                Killed          = killed,
                CooldownMs      = 0
            });

            W2CHealthUpdatePacketSender.Send(peer, session.Combat.Health, session.Combat.MaxHealth);

            // Being hit throws you off - otherwise riding would be a way to
            // shrug off everything that hits you.
            _players.Mounts?.Dismount(peer, session, "You are thrown from your mount!");

            if (killed)
            {
                Logger.Info("[Combat] Account {Account} was killed by {Npc} ({Id})", session.AccountId, npc.Name, npc.NetworkId);
                KillPlayer(peer, session, playerId, npc.Name, npc.TemplateId);
            }
        }

        /// <summary>
        /// Shared by both ways to die. killerTemplateId is the NPC template,
        /// or 0 when another player did it - that's what Lua's OnDeath gets.
        /// </summary>
        private void KillPlayer(NetPeer peer, PlayerSession session, int playerId, string killerName, int killerTemplateId)
        {
            // Nothing carries on through death.
            _harvest.CancelFor(playerId, "You black out.");
            _players.Mounts?.Dismount(peer, session);
            _players.Pets?.Dismiss(session);
            _npcAi.OnPlayerGone(playerId);

            // Remembered so the corpse can send them to the nearest
            // graveyard rather than wherever they are when they press it.
            session.Combat.DiedAt = session.Position;

            W2CPlayerDeathPacketSender.Send(peer, killerName);
            _players.FireDeathEvent(peer, killerTemplateId);
        }

        /// <summary>
        /// C2WRespawn: back at the spawn point on full health. The snapshot
        /// store is updated so other clients see the move, and the movement
        /// validator is told it was authoritative - otherwise the jump across
        /// the map would look like a speed hack.
        /// </summary>
        public void TryRespawn(NetPeer peer)
        {
            if (!_players.TryGetSession(peer, out var session) || session.NetworkId is not int playerId)
                return;

            if (!session.Combat.IsDead)
                return;

            // The nearest respawn point to where they fell, not to where
            // they are now - a dead player doesn't move, but this keeps the
            // rule honest if that ever changes.
            Vector3 spawn = _spawnPoints.GetRespawnNear(session.Combat.DiedAt);

            session.Combat.Health = session.Combat.MaxHealth;

            // A graveyard can be anywhere in the shard. TeleportPlayer moves
            // them through the interest grid too, so the people and NPCs
            // around the graveyard appear and the ones at the corpse go away.
            _players.TeleportPlayer(peer, session, spawn);

            W2CRespawnPacketSender.Send(peer, spawn, session.Combat.Health, session.Combat.MaxHealth);

            Logger.Info("[Combat] Account {Account} respawned at {Spawn}", session.AccountId, spawn);
        }

        /// <summary>Everyone who can see the target, plus the attacker even if the grid hasn't paired them yet.</summary>
        private void Broadcast(NetPeer attacker, int targetId, W2CCombatEventPacket packet)
        {
            // `attacker` is the peer that must see this hit whatever the grid
            // says - the attacking player, or the victim when an NPC swings.
            _observerScratch.Clear();
            _observerScratch.Add(attacker);

            foreach (int id in _interest.GetKnownBy(targetId))
            {
                if (SpawnManager.IsNpcId(id)) continue;
                if (_players.TryGetPeer(id, out var p) && p != attacker)
                    _observerScratch.Add(p);
            }

            W2CCombatEventPacketSender.Send(_replication, _observerScratch, packet);
        }
    }
}
