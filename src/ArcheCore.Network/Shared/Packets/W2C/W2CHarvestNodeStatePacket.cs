using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>A node the player can see was depleted or respawned.</summary>
    [MessagePackObject(true)]
    public class W2CHarvestNodeStatePacket
    {
        public int  NetworkId;
        public bool IsDepleted;
    }
}
