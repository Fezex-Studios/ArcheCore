namespace ArcheCore.Server.World.GameData.Combat;

/// <summary>
/// One usable combat skill. Roadmap H is a single melee skill, but it's a
/// table from day one so tuning is data, not a rebuild - and the next skill
/// is an INSERT.
/// </summary>
public class SkillTemplate
{
    public int    Id         { get; set; }
    public string Name       { get; set; } = string.Empty;

    /// <summary>Max distance to the target, in world units. Range-only - no line of sight yet.</summary>
    public float  Range      { get; set; } = 3f;
    public int    CooldownMs { get; set; } = 1500;
    public int    MinDamage  { get; set; } = 5;
    public int    MaxDamage  { get; set; } = 10;
}
