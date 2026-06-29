using System.Diagnostics;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using MessagePack;
using NLog;


namespace ArcheCore.Server.World.PersistenceServer.Networking.P2W
{
    public class P2WConnectResponseHandler : IPersistencePacketHandler
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        
        public void Handle(PersistencePacket persistencePacket)
        {
            var response =
                MessagePackSerializer
                    .Deserialize<P2WConnectResponse>(
                        persistencePacket.Payload);

            Logger.Info(
                response.Message);
        }
    }
}