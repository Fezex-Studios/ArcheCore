using System.Threading.Tasks;
using ArcheCore.Network.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Senders
{
    public class W2PHelloWorldSender
    {
        private readonly PersistenceClient _client;


        public W2PHelloWorldSender(PersistenceClient client)
            => _client = client;

        public async Task Send(string message)
        {
            await _client.Send(PServerOpcodes.HelloWorld,new W2PHelloWorldPacket{Message = message});
        }
        
    }
}