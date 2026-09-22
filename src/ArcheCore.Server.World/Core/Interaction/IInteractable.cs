using System.Numerics;

namespace ArcheCore.Server.World.Core.Interaction
{
    // Anything a player can press "interact" on: NPCs, harvest nodes,
    // corpses, and quest objects later. C2WInteractHandler routes on Kind
    // and otherwise only talks to this interface.
    public interface IInteractable
    {
        Vector3 Position { get; }
        int TemplateId { get; }
        InteractableKind Kind { get; }
        float InteractRange { get; }
    }

    public enum InteractableKind
    {
        Npc = 1,
        Lootable = 2,
        // QuestObject = 3 - add as it's built
        HarvestNode = 4,
    }
}
