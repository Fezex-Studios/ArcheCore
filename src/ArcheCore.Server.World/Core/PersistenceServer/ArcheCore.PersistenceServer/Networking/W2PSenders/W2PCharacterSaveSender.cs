using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Senders
{
    public class W2PCharacterSaveSender
    {
        private readonly PersistenceClient _client;

        public W2PCharacterSaveSender(PersistenceClient client)
            => _client = client;

        // Fire-and-forget, matching the original — /characters/save never
        // returned a body under TCP either.
        public Task Send(
            long   characterId,
            int    accountId,
            string name,
            int    level,
            float  x,
            float  y,
            float  z)
            => _client.PostAsync(
                "/characters/save",
                new W2PCharacterSaveRequest
                {
                    CharacterId = characterId,
                    AccountId   = accountId,
                    Name        = name,
                    Level       = level,
                    X           = x,
                    Y           = y,
                    Z           = z
                });
    }
}