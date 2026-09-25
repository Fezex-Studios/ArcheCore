using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Senders
{
    /// <summary>
    /// The one character save (see W2PCharacterSaveFullRequest). Throws on
    /// network errors, timeouts and non-2xx answers - the caller
    /// (CharacterSaveChain) decides what each of those means.
    /// </summary>
    public class W2PCharacterSaveFullSender
    {
        private readonly PersistenceClient _client;

        public W2PCharacterSaveFullSender(PersistenceClient client)
            => _client = client;

        public Task<P2WCharacterSaveFullResponse> Send(W2PCharacterSaveFullRequest request)
            => _client.PostAsync<W2PCharacterSaveFullRequest, P2WCharacterSaveFullResponse>(
                "/characters/save-full", request, logTiming: false);
    }
}
