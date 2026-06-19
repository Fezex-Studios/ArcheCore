using LiteNetLib;
using LiteNetLib.Utils;

namespace ArcheCore.Client.Networking
{
    public interface IClientPacketHandler
    {
        void Handle(
            NetPacketReader reader);
    }
}