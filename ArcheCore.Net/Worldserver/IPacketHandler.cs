using LiteNetLib;

namespace ArcheCore.Net.Worldserver
{
    public interface IPacketHandler
    {
        void Handle(
            NetPeer peer,
            NetPacketReader reader);
    }
}