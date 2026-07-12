using System.Numerics;

namespace ArcheCore.Server.World.Core.Interaction
{
    // Anything a player can press "interact" on: NPCs today, lootable
    // objects / quest objects / resource nodes later. The handler only
    // ever talks to this interface, never to NpcEntity or any future
    // concrete type directly - that's what keeps C2WInteractHandler from
    // needing to grow a branch per entity type.
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
        // Lootable = 2, QuestObject = 3, ResourceNode = 4, ... add as they're built
    }
}