using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using MessagePack;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Networking.P2W
{
    public class P2WCharacterListResponseHandler : IPersistencePacketHandler
    {
        private static readonly Logger Logger =
            LogManager.GetCurrentClassLogger();

        private readonly PersistenceClient _client;

        public P2WCharacterListResponseHandler(PersistenceClient client)
            => _client = client;

        public void Handle(PersistencePacket packet)
        {
            var response = MessagePackSerializer
                .Deserialize<P2WCharacterListResponse>(packet.Payload);

            Logger.Info(
                $"[CharacterList] AccountId={response.AccountId} " +
                $"Count={response.Characters?.Length ?? 0}");

            _client.ResolveList(response);
        }
    }
}