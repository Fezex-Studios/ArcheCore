namespace ArcheCore.Server.World.Core.Services
{
    /// <summary>
    /// Two-phase manager start-up (roadmap fix-first #4).
    ///
    /// Phase 1: WorldServer constructs every manager and registers it in
    /// the ServiceContainer. Constructors take only what they need to EXIST
    /// (config, db factory, the shared grid) - never another manager that
    /// might not have been built yet.
    ///
    /// Phase 2: once everything is registered, WorldServer calls
    /// Initialize on every manager that implements this. That's where a
    /// manager looks up the others it talks to. Because everything already
    /// exists by then, construction order stops mattering - which is what
    /// the four static Current handles (QuestManager, MountManager,
    /// PetManager, InteractionActionCatalog) and the SetCombat/Mounts/Pets
    /// setters were working around.
    ///
    /// Initialize must not load data or talk to the network - that's the
    /// load step that follows, which may depend on everyone being wired.
    /// </summary>
    public interface IInitializable
    {
        void Initialize(ServiceContainer services);
    }
}
