using System.Collections.Generic;
using System.Numerics;
using ArcheCore.Server.World.Core.Interaction;

namespace ArcheCore.Server.World.Core.Entities;

/// <summary>
/// What a killed NPC leaves behind: the rolled loot, lootable by the
/// player who got the kill. Lives in the shared interest grid like a
/// harvest node, and disappears once it's emptied or its time runs out.
/// Tick thread only.
/// </summary>
public class CorpseEntity : IInteractable
{
    public int     NetworkId        { get; init; }
    public int     SourceTemplateId { get; init; }
    public string  Name             { get; init; } = string.Empty;
    public Vector3 Position         { get; init; }

    /// <summary>Player network id allowed to loot.</summary>
    public int     OwnerId          { get; init; }
    public string  OwnerName        { get; init; } = string.Empty;

    public long    ExpiresAtMs;
    public int     Gold;
    public readonly List<(int ItemId, int Quantity)> Items = new();

    public bool IsEmpty => Gold <= 0 && Items.Count == 0;

    public int   TemplateId    => SourceTemplateId;
    public float InteractRange => 4f;
    public InteractableKind Kind => InteractableKind.Lootable;
}
