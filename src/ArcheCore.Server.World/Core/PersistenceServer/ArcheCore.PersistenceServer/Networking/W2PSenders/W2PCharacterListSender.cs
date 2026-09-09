using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Senders
{
    public class W2PCharacterListSender
    {
        private readonly PersistenceClient _client;

        public W2PCharacterListSender(PersistenceClient client)
            => _client = client;

        public Task<P2WCharacterListResponse> Send(int accountId)
            => _client.PostAsync<W2PCharacterListRequest, P2WCharacterListResponse>(
                "/characters/list",
                new W2PCharacterListRequest { AccountId = accountId });
    }
}