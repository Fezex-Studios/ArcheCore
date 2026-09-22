using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ArcheCore.Network.Shared.Packets.W2C;
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
    ///   6. Start the cooldown, roll damage, apply it
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
    /// NPCs don't fight back yet; that's roadmap J (player death).
    /// </summary>
    public class CombatManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly Random Rng = new();

        /// <summary>Slack on the range check, so standing right at the edge doesn't flicker.</summary>
        private const float RangeTolerance = 0.5f;

        private readonly IDbContextFactory<WorldDataDbContext> _dbFactory;
        private readonly PlayerManager _players;
        private readonly SpawnManager _spawnManager;
        private readonly NpcAiManager _npcAi;
        private readonly LootManager _loot;
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
            InterestManager interest,
            ReplicationManager replication)
        {
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

            if (session.IsDead)
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
            long now = Environment.TickCount64;
            if (session.SkillCooldowns.TryGetValue(skillId, out long readyAt) && now < readyAt)
                return;

            // 4
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

            // 6
            session.SkillCooldowns[skillId] = now + skill.CooldownMs;

            int damage = Rng.Next(skill.MinDamage, skill.MaxDamage + 1);
            npc.Health = Math.Max(0, npc.Health - damage);
            bool killed = npc.Health == 0;

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

        /// <summary>Everyone who can see the target, plus the attacker even if the grid hasn't paired them yet.</summary>
        private void Broadcast(NetPeer attacker, int targetId, W2CCombatEventPacket packet)
        {
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
