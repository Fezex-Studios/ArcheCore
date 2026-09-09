using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Senders
{
    public class W2PHelloWorldSender
    {
        private readonly PersistenceClient _client;

        public W2PHelloWorldSender(PersistenceClient client)
            => _client = client;

        // Fire-and-forget, matching the original — Persistence just logs
        // this and never sends a response.
        public Task Send(string message)
            => _client.PostAsync("/hello-world", new W2PHelloWorldPacket { Message = message });
    }
}