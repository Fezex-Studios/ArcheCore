namespace ArcheCore.Server.World.Core.Interaction
{
    public static class InteractionConstants
    {
        // Authoritative range check. The client's raycast in PlayerInteraction
        // is only ever a UX hint - this is the number that actually matters.
        public const float MaxRange = 5f;
    }
}