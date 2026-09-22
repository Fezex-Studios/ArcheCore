using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// The server accepted your harvest and claimed the node for you. Show
    /// a progress bar for DurationMs. The bar is cosmetic - the server
    /// decides when it's done and sends Completed or Cancelled.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CHarvestStartedPacket
    {
        public int    NodeNetworkId;
        public string NodeName;
        public int    DurationMs;
    }
}
