using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using MessagePack;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Networking.P2W
{
    public class P2WCharacterCreateResponseHandler : IPersistencePacketHandler
    {
        private static readonly Logger Logger =
            LogManager.GetCurrentClassLogger();

        private readonly PersistenceClient _client;

        public P2WCharacterCreateResponseHandler(PersistenceClient client)
            => _client = client;

        public void Handle(PersistencePacket packet)
        {
            var response = MessagePackSerializer
                .Deserialize<P2WCreateCharacterResponse>(packet.Payload);

            Logger.Info(
                $"[CharacterCreate] AccountId={response.AccountId} " +
                $"Success={response.Success} Name={response.Name}");

            _client.ResolveCreate(response);
        }
    }
}