using LiteNetLib;

namespace ArcheCore.Network.Worldserver
{
    public interface IPacketHandler
    {
        void Handle(
            NetPeer peer,
            NetPacketReader reader);
    }
}