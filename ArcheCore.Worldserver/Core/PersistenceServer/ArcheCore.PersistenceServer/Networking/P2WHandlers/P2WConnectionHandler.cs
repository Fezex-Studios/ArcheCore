using System.Diagnostics;
using ArcheCore.Net.Shared.Packets.PersistenceServer;
using ArcheCore.Net.Shared.Packets.PersistenceServer.P2W;
using MessagePack;


namespace ArcheCore.WorldServer.PersistenceServer.Networking.P2W
{
    public class P2WConnectResponseHandler
        : IPersistencePacketHandler
    {
        public void Handle(PersistencePacket persistencePacket)
        {
            var response =
                MessagePackSerializer
                    .Deserialize<P2WConnectResponse>(
                        persistencePacket.Payload);

            Debug.Log(
                response.Message);
        }
    }
}