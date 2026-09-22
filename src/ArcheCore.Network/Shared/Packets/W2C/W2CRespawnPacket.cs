using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// You're alive again: where the server put you, and your health. The
    /// client teleports the local player there - the same path a position
    /// correction uses - so client and server agree immediately.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CRespawnPacket
    {
        public float X;
        public float Y;
        public float Z;
        public int   Health;
        public int   MaxHealth;
    }
}
