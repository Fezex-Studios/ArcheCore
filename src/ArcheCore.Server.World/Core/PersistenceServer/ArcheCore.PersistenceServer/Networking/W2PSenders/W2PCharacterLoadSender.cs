using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Senders
{
    public class W2PCharacterLoadSender
    {
        private readonly PersistenceClient _client;

        public W2PCharacterLoadSender(PersistenceClient client)
            => _client = client;

        public Task<P2WCharacterLoadResponse> Send(int accountId)
            => Send(accountId, 0);

        public Task<P2WCharacterLoadResponse> Send(int accountId, long characterId)
            => _client.PostAsync<W2PCharacterLoadRequest, P2WCharacterLoadResponse>(
                "/characters/load",
                new W2PCharacterLoadRequest
                {
                    AccountId   = accountId,
                    CharacterId = characterId
                });
    }
}