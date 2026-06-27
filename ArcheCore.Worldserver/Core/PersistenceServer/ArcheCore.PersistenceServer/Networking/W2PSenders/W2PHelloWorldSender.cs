using System.Threading.Tasks;

using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.WorldServer.PersistenceServer.Senders
{
    public class W2PHelloWorldSender
    {
        private readonly PersistenceClient _client;


        public W2PHelloWorldSender(PersistenceClient client)
            => _client = client;

        public async Task Send(string message)
        {
            await _client.Send(PersistenceOpcode.HelloWorld,new W2PHelloWorldPacket{Message = message});
        }
        
    }
}