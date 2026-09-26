using Microsoft.EntityFrameworkCore;

namespace ArcheCore.Server.World.GameData.Effects;

/// <summary>
/// What an effect does. The numbers 0-5 are the same as ItemEffectType, so
/// an ItemUses row converts to an Effects row by copying the number
/// (SQL patch 036 does exactly that).
/// </summary>
public enum EffectKind
{
    /// <summary>Nothing built in - a Lua hook (OnItemUse, ...) does the work.</summary>
    Script = 0,

    /// <summary>Restore Min..Max health.</summary>
    Heal = 1,

    /// <summary>Apply status RefId for DurationMs (buff, debuff, DoT). Logged stub until Phase 3 status effects.</summary>
    ApplyStatus = 2,

    /// <summary>Cast skill RefId. Logged stub until skills can be cast by something other than the attack key.</summary>
    CastSkill = 3,

    /// <summary>Get on (or off) mount RefId (Mounts.Id).</summary>
    Mount = 4,

    /// <summary>Summon (or dismiss) pet RefId (NpcTemplates.Id).</summary>
    SummonPet = 5,

    /// <summary>Deal Min..Max damage.</summary>
    Damage = 6,
}

/// <summary>Who an effect lands on.</summary>
public enum EffectTarget
{
    /// <summary>Whoever caused it: the player using the item, the attacker.</summary>
    Self = 0,

    /// <summary>What the cause was aimed at: the attack's target.</summary>
    Target = 1,
}

/// <summary>What an Effects row belongs to.</summary>
public enum EffectOwner
{
    /// <summary>OwnerId = Items.item_id. Runs when the item is used (it still needs an ItemUses row for consume/cooldown).</summary>
    Item = 1,

    /// <summary>OwnerId = Skills.Id. Runs when the skill hits.</summary>
    Skill = 2,
}

/// <summary>
/// One effect of one item or skill (roadmap fix-first #1). An owner can
/// have several rows - they run in Sort order, e.g. a skill that does
/// damage AND applies a slow:
///
///   OwnerType OwnerId Sort Kind        Target Min Max RefId DurationMs
///   2 (Skill) 4       0    6 Damage    1      20  30  0     0
///   2 (Skill) 4       1    2 ApplyStat 1      0   0   3     5000
///
/// Items and skills with NO rows fall back to their old columns
/// (ItemUses.EffectType/EffectValue, Skills.MinDamage/MaxDamage), so
/// existing data keeps working. Once an owner has rows, the rows are the
/// truth and the old columns are ignored for it.
/// </summary>
[Index(nameof(OwnerType), nameof(OwnerId))]
public class EffectRow
{
    public int Id { get; set; }

    public EffectOwner OwnerType { get; set; }
    public int OwnerId { get; set; }

    /// <summary>Run order within the owner, lowest first.</summary>
    public int Sort { get; set; }

    public EffectKind Kind { get; set; }
    public EffectTarget Target { get; set; }

    /// <summary>Amount range for Heal / Damage, rolled inclusive. Min = Max for a fixed amount.</summary>
    public int Min { get; set; }
    public int Max { get; set; }

    /// <summary>What Mount / SummonPet / ApplyStatus / CastSkill refer to.</summary>
    public int RefId { get; set; }

    /// <summary>How long a status lasts. 0 for everything else.</summary>
    public int DurationMs { get; set; }

    /// <summary>Optional Lua hook name for Script effects. Null = the owner's usual hook (OnItemUse).</summary>
    public string? Script { get; set; }
}
