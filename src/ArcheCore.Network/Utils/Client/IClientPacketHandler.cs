using LiteNetLib;
using LiteNetLib.Utils;

namespace ArcheCore.Network.Client
{
    public interface IClientPacketHandler
    {
        void Handle(
            NetPacketReader reader);
    }
}