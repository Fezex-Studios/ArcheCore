using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Senders
{
    public class W2PQuestSaveSender
    {
        private readonly PersistenceClient _client;

        public W2PQuestSaveSender(PersistenceClient client)
            => _client = client;

        /// <summary>
        /// Sends the character's quest rows. Returns true if the persistence
        /// server confirmed and applied them all. A quest with status 0 is
        /// one that was abandoned, and the row is deleted.
        /// </summary>
        public Task<bool> Send(long characterId, int accountId, QuestStateDto[] quests)
            => _client.PostForStatusAsync(
                "/characters/quests/save",
                new W2PQuestSaveRequest
                {
                    CharacterId = characterId,
                    AccountId   = accountId,
                    Quests      = quests
                });
    }
}
