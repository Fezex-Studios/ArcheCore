using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    [MessagePackObject(true)]
    public class W2CItemDataResponsePacket
    {
        public bool Found;
        public int ItemId;
        public string Name;
        public string Description;
        public int CategoryId;
        public string IconName;
    }
}
