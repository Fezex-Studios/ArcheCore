using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// The local player's own health changed outside of combat - a potion,
    /// a level-up. Absolute values, not a delta.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CHealthUpdatePacket
    {
        public int Health;
        public int MaxHealth;
    }
}
