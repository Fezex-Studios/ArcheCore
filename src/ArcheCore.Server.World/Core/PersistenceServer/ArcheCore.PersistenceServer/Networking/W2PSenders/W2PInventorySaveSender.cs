using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Senders
{
    public class W2PInventorySaveSender
    {
        private readonly PersistenceClient _client;

        public W2PInventorySaveSender(PersistenceClient client)
            => _client = client;

        /// <summary>
        /// Sends only the changed slots. Returns true if the persistence
        /// server confirmed and applied every change in the batch.
        /// </summary>
        public Task<bool> Send(long characterId, int accountId, InventorySlotDto[] changes)
            => _client.PostForStatusAsync(
                "/characters/inventory/save",
                new W2PInventorySaveRequest
                {
                    CharacterId = characterId,
                    AccountId   = accountId,
                    Changes     = changes
                });
    }
}
