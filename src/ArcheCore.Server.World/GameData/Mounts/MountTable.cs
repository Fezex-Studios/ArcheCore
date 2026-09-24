namespace ArcheCore.Server.World.GameData.Mounts;

/// <summary>
/// A mount. Data only: what it's called, what model to draw, and how much
/// faster it goes.
///
/// A mount is summoned by USING AN ITEM - an ItemUse row with
/// EffectType = Mount and EffectValue = this Id - so a new mount is an item,
/// an ItemUse row and a row here. No code, same as every other content type.
/// </summary>
public class MountTable
{
    public int    Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>WorldObjectPrefabRegistry key on the client.</summary>
    public string ModelType { get; set; } = "";

    /// <summary>
    /// Multiplies the rider's run speed AND the server's speed allowance.
    /// Keep it sane: the movement validator allows this much and no more, so
    /// a 10x mount is also a 10x speed-hack allowance.
    /// </summary>
    public float  SpeedMultiplier { get; set; } = 1.6f;
}
