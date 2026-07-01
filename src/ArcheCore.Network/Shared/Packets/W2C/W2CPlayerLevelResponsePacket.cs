using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    [MessagePackObject(true)]
    public class W2CPlayerLevelResponsePacket
    {
        public int Level;
    }
}