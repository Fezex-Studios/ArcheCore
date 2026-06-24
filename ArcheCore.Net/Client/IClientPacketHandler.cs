using LiteNetLib;
using LiteNetLib.Utils;

namespace ArcheCore.Net.Client
{
    public interface IClientPacketHandler
    {
        void Handle(
            NetPacketReader reader);
    }
}