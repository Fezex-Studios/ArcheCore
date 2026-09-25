using System.Numerics;
using ArcheCore.Server.World.Core.Interaction;
using ArcheCore.Server.World.GameData.Harvest;

namespace ArcheCore.Server.World.Core.Entities;

/// <summary>
/// One live harvest node. Created at boot by HarvestManager from a
/// HarvestNodeSpawn row and never destroyed while the server runs - a
/// depleted node is still there, just not harvestable until RespawnAtMs.
/// Only ever touched on the tick thread.
/// </summary>
public class HarvestNodeEntity : IInteractable
{
    public int                 NetworkId { get; init; }
    public int                 SpawnId   { get; init; }
    public HarvestNodeTemplate Template  { get; init; } = null!;
    public Vector3             Position  { get; init; }
    public float               Yaw       { get; init; }

    public int   TemplateId    => Template.Id;
    public float InteractRange => Template.InteractRange;
    public InteractableKind Kind => InteractableKind.HarvestNode;

    public bool IsDepleted;

    /// <summary>ServerClock.NowMs at which a depleted node comes back.</summary>
    public long RespawnAtMs;

    /// <summary>
    /// Player network id currently harvesting this node, or null. Set at
    /// the START of a harvest - claiming at the start is what stops two
    /// players both finishing the same node in the same tick.
    /// </summary>
    public int? ClaimedBy;
}
