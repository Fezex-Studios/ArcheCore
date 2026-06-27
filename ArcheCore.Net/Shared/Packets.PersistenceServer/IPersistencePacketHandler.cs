namespace ArcheCore.Net.Shared.Packets.PersistenceServer
{
    public interface IPersistencePacketHandler
    {
        void Handle(PersistencePacket persistencePacket);
    }
}