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

        /// <summary>
        /// Returns true if the persistence server confirmed the save (2xx),
        /// false if it returned an error. Throws on network errors/timeouts.
        /// </summary>
        public Task<bool> Send(
            long   characterId,
            int    accountId,
            string name,
            int    level,
            float  x,
            float  y,
            float  z,
            int    gold)
            => _client.PostForStatusAsync(
                "/characters/save",
                new W2PCharacterSaveRequest
                {
                    CharacterId = characterId,
                    AccountId   = accountId,
                    Name        = name,
                    Level       = level,
                    X           = x,
                    Y           = y,
                    Z           = z,
                    Gold        = gold
                });
    }
}