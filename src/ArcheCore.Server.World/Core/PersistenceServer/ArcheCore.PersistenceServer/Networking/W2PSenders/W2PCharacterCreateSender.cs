using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Senders
{
    public class W2PCharacterCreateSender
    {
        private readonly PersistenceClient _client;

        public W2PCharacterCreateSender(PersistenceClient client)
            => _client = client;

        public Task<P2WCreateCharacterResponse> Send(int accountId, string name)
            => _client.PostAsync<W2PCreateCharacterRequest, P2WCreateCharacterResponse>(
                "/characters/create",
                new W2PCreateCharacterRequest
                {
                    AccountId = accountId,
                    Name      = name
                });
    }
}