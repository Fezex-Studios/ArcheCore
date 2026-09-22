using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// Your health hit zero. The hit itself already arrived as a
    /// W2CCombatEvent with Killed set, so this is the "what now" packet:
    /// it's what puts the death screen up. You stay in the world, lying
    /// where you fell, until you press Respawn (C2WRespawn).
    /// </summary>
    [MessagePackObject(true)]
    public class W2CPlayerDeathPacket
    {
        public string KillerName;
    }
}
