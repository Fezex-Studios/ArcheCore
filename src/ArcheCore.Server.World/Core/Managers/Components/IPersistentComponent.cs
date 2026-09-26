using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// A piece of a PlayerSession that is saved to the database
    /// (roadmap fix-first #5).
    ///
    /// Deliberately NOT "each component saves itself". A save is ONE
    /// snapshot of the whole character, written in one transaction
    /// (/characters/save-full, see CharacterPersistence and Docs/SAVES.md).
    /// If the inventory and the quest log saved separately, a crash between
    /// the two would hand out a quest reward without taking the quest items
    /// - exactly the half-applied saves C1-C3 removed.
    ///
    /// So a component only says three things:
    ///   IsDirty   - changed since the last snapshot?
    ///   WriteTo   - put your part into this snapshot
    ///   MarkSaved - the snapshot you wrote is now the saved state
    ///
    /// CharacterPersistence.Capture asks every component in
    /// PlayerSession.PersistentComponents, so adding a saved system
    /// (skills, housing, reputation...) is: a component, a field on the save
    /// request, a column/table in the persistence server - and no edits to
    /// CharacterPersistence or the autosave.
    ///
    /// Tick thread only, like the rest of the session.
    /// </summary>
    public interface IPersistentComponent
    {
        bool IsDirty { get; }

        void WriteTo(W2PCharacterSaveFullRequest snapshot);

        void MarkSaved();
    }
}
