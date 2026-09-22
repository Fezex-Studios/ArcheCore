namespace ArcheCore.Server.World.Core.Combat;

/// <summary>
/// The one place player health is derived from. Players have no health
/// column yet (it isn't persisted - you log in at full health), so max
/// health is a function of level. When gear and stats arrive, this is
/// the only method that changes.
/// </summary>
public static class HealthRules
{
    public static int PlayerMaxHealth(int level) => 100 + (System.Math.Max(1, level) - 1) * 10;
}
