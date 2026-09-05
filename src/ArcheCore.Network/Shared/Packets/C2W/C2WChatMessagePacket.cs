using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    [MessagePackObject(true)]
    public class C2WChatMessagePacket
    {
        public string Message;
        public ChatChannel Channel;
        public string TargetName;
    }
}