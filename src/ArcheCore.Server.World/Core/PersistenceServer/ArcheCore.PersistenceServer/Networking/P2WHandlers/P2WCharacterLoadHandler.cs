

using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using MessagePack;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Networking.P2W
{
    public class P2WCharacterLoadHandler : IPersistencePacketHandler
    {
        private readonly PersistenceClient persistenceClient;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public P2WCharacterLoadHandler(PersistenceClient persistenceClient)
        {
            this.persistenceClient = persistenceClient;
        }

        public void Handle(PersistencePacket persistencePacket)
        {
            var character =
                MessagePackSerializer
                    .Deserialize<P2WCharacterLoadResponse>(persistencePacket.Payload);

            Logger.Info($"[CharacterLoadHandler] Loaded CharacterId={character.CharacterId} Name={character.Name} Level={character.Level} Pos=({character.X}, {character.Y}, {character.Z})");

            persistenceClient.ResolveLoad(character);
        }
    }
}