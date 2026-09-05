using System.Security.Authentication.ExtendedProtection;
using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    [MessagePackObject(true)]
    public class W2CChatMessagePacket
    {
        public int SenderNetworkId;
        public string SenderName;
        public string Message;
        public ChatChannel Channel;
    }
}