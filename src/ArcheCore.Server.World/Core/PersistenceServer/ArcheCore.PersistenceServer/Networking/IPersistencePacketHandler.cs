using ArcheCore.Network.Shared.Packets.PersistenceServer;
using MessagePack;

namespace ArcheCore.Server.World.PersistenceServer.Networking
{
    public interface IPersistencePacketHandler
    {
        void Handle(PersistencePacket persistencePacket);
    }
}