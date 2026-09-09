using System;
using System.Numerics;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// The most isolated piece of PlayerManager's old six responsibilities,
    /// which is why it's the first one pulled out. Owns the one thing it
    /// does: asking the persistence server to save a character, and
    /// logging success/failure. Any future load/create wrapping belongs
    /// here too.
    /// </summary>
    public class CharacterPersistence
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly PersistenceClient _persistence;

        public CharacterPersistence(PersistenceClient persistence)
        {
            _persistence = persistence;
        }

        public async void SaveCharacterAsync(
            long characterId, int accountId, string name, int level, Vector3 pos)
        {
            try
            {
                await _persistence.W2PCharacterSave.Send(
                    characterId, accountId, name, level, pos.X, pos.Y, pos.Z);

                Logger.Info($"[Save] CharacterId={characterId} saved successfully.");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[Save] FAILED to save CharacterId={characterId}");
            }
        }
    }
}