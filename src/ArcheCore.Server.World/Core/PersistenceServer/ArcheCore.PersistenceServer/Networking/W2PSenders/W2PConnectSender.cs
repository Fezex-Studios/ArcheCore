using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Senders
{
    public class W2PConnectSender
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly PersistenceClient _client;

        public W2PConnectSender(PersistenceClient client)
            => _client = client;

        public async Task Send(string message)
        {
            var response = await _client.PostAsync<W2PConnectionRequest, P2WConnectResponse>(
                "/connect",
                new W2PConnectionRequest { Message = message });

            Logger.Info(response.Message);
        }
    }
}