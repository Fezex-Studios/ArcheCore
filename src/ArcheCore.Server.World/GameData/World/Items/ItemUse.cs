namespace ArcheCore.Server.World.GameData.Items;

/// <summary>What happens when an item is used. See ItemUse.EffectType.</summary>
public enum ItemEffectType
{
    /// <summary>No built-in effect. Only PlayerEvent.OnItemUse fires, so a
    /// Lua script decides everything (teleport scrolls, quest items).</summary>
    ScriptOnly = 0,

    /// <summary>Restore EffectValue HP. Stubbed until health exists
    /// (roadmap G) - see PlayerManager.TryApplyItemEffect.</summary>
    Heal = 1,

    /// <summary>Apply buff id EffectValue. Stubbed until buffs exist.</summary>
    ApplyBuff = 2,

    /// <summary>Cast skill id EffectValue. Stubbed until skills exist (H).</summary>
    CastSkill = 3,

    /// <summary>Summon/dismiss mount EffectValue (Mounts.Id). Roadmap N.</summary>
    Mount = 4,

    /// <summary>Summon/dismiss pet EffectValue (NpcTemplates.Id). Roadmap O.</summary>
    SummonPet = 5,
}

/// <summary>
/// One row per USABLE item. Same 1:0-or-1 shape as ItemStats: a crafting
/// material or a sword simply has no row, and TryUseItem rejects it.
///
/// "Use" behaviour is data here rather than branches in code, so a new
/// potion is an INSERT, not a server rebuild.
///
///   Health potion : ConsumeOnUse=1, EffectType=Heal,      EffectValue=50, CooldownMs=30000
///   Skill trinket : ConsumeOnUse=0, EffectType=ApplyBuff, EffectValue=7,  CooldownMs=120000
/// </summary>
public class ItemUse
{
    public int Id { get; set; }

    /// <summary>FK-by-convention -> ItemTable.item_id. One row per item.</summary>
    public int ItemId { get; set; }

    /// <summary>Remove one from the stack after a SUCCESSFUL use.</summary>
    public bool ConsumeOnUse { get; set; }

    public ItemEffectType EffectType { get; set; }

    /// <summary>Meaning depends on EffectType: HP amount, buff id, skill id.</summary>
    public int EffectValue { get; set; }

    /// <summary>0 = no cooldown.</summary>
    public int CooldownMs { get; set; }

    /// <summary>
    /// Items sharing a non-zero group share ONE cooldown - drink a minor
    /// health potion and the major one is locked too. 0 means "this item
    /// is its own group." Cooldowns are keyed by group, never by slot, so
    /// moving a potion to another slot can't reset its timer.
    /// </summary>
    public int CooldownGroup { get; set; }
}