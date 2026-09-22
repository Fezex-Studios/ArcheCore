using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>Your harvest stopped without finishing. Reason is shown to the player.</summary>
    [MessagePackObject(true)]
    public class W2CHarvestCancelledPacket
    {
        public int    NodeNetworkId;
        public string Reason;
    }
}
