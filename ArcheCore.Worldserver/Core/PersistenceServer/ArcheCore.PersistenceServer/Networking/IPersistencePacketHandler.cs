using ArcheCore.Net.Shared.Packets.PersistenceServer;
using MessagePack;

namespace ArcheCore.WorldServer.PersistenceServer.Networking
{
    public interface IPersistencePacketHandler
    {
        void Handle(PersistencePacket persistencePacket);
    }
}