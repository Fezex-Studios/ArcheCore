using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    [MessagePackObject(true)]
    public class W2CSpawnPlayerPacket
    {
        public int NetworkId;

        public float x;
        public float y;
        public float z;

        public bool IsLocalPlayer;

        /// <summary>Character name, for nameplates.</summary>
        public string Name;

        /// <summary>For other players' nameplates and the target frame (PvP). 0/0 = unknown.</summary>
        public int Health;
        public int MaxHealth;

        /// <summary>
        /// The mount they're riding, or empty. Carried here as well as in
        /// W2CMountState so someone who rides into view is drawn mounted
        /// straight away, instead of on foot until they next dismount.
        /// </summary>
        public string MountModelType;
    }
}