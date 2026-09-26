using System.Collections.Generic;
using System.Numerics;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Health, death and skill cooldowns. Not saved: health is set to full
    /// from HealthRules.PlayerMaxHealth(Level) at spawn. Written by
    /// PlayerManager (heals, level-ups) and CombatManager (damage, death).
    /// </summary>
    public sealed class CombatComponent
    {
        public int Health;
        public int MaxHealth;

        public bool IsDead => MaxHealth > 0 && Health <= 0;

        /// <summary>Where this player died - picks the nearest respawn point.</summary>
        public Vector3 DiedAt;

        /// <summary>Skill id -> ServerClock.NowMs when it's ready again.</summary>
        public readonly Dictionary<int, long> SkillCooldowns = new();
    }

    /// <summary>
    /// What the player is riding and what they've summoned (roadmap N/O).
    /// Not saved: you log in on foot, pet dismissed. Written only by
    /// MountManager and PetManager.
    /// </summary>
    public sealed class MountComponent
    {
        /// <summary>Mounts.Id being ridden, or 0.</summary>
        public int MountId;

        /// <summary>The ridden mount's model, for spawn packets; null on foot.</summary>
        public string Model;

        /// <summary>
        /// What the movement check allows, relative to normal: 1 on foot, the
        /// mount's multiplier while riding.
        /// </summary>
        public float SpeedMultiplier = 1f;

        /// <summary>Network id of this player's summoned pet, or 0.</summary>
        public int PetNetworkId;

        public bool IsMounted => MountId != 0;
    }

    /// <summary>
    /// MovementValidator's per-player state (speed budgets, violations).
    /// Nothing else reads or writes it.
    /// </summary>
    public sealed class MovementComponent
    {
        public bool    BaselineSet;
        public Vector3 LastValidPosition;
        public double  LastMoveTime;
        public float   HorizontalBudget;
        public float   UpBudget;
        public float   DownBudget;
        public int     Violations;
        public double  LastCorrectionTime;
    }

    /// <summary>Auction house and mailbox state (MarketAccess, MailManager).</summary>
    public sealed class MarketComponent
    {
        /// <summary>
        /// Network id of the NPC the player last opened the auction house or
        /// mailbox at, or 0. Every auction/mail packet is checked against it
        /// (MarketAccess, audit H4).
        /// </summary>
        public int TargetId;

        /// <summary>
        /// Mail ids whose claim-save is in flight. A second claim of the same
        /// mail is ignored until the first has an answer, so one mail can't be
        /// paid out twice in memory.
        /// </summary>
        public readonly HashSet<long> ClaimingMail = new();
    }
}
