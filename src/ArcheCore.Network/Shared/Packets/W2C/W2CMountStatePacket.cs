using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// Someone got on or off a mount. Sent to everyone who can see them, so
    /// every client swaps the model at the same moment.
    ///
    /// There is no "Mounted" bit in MovementState on purpose: that field is
    /// a byte and all eight bits are already spoken for (Moving, Airborne,
    /// Jumping, Swimming, Gliding, InCombat, Sitting, Dead). Widening it
    /// would change the snapshot format for every entity in the world, and
    /// observers need this packet anyway - a bit could say "mounted" but not
    /// WHICH mount to draw.
    ///
    /// SpeedMultiplier matters to the rider's own client (it moves faster)
    /// and to the server (the speed check allows for it). Other clients can
    /// ignore it; they're told positions, not speeds.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CMountStatePacket
    {
        public int    PlayerNetworkId;

        /// <summary>0 = dismounted; the model is removed.</summary>
        public int    MountId;

        /// <summary>WorldObjectPrefabRegistry key for the mount model.</summary>
        public string ModelType;

        public float  SpeedMultiplier;
    }
}
