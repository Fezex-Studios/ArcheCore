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
    }
}