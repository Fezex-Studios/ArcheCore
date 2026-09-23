using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// An NPC's health changed without anyone hitting it - today that means
    /// it healed after leashing home. Combat hits carry their own health in
    /// W2CCombatEvent; this is for everything else, so every client's health
    /// bar agrees without waiting for the next fight.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CNpcHealthPacket
    {
        public int NetworkId;
        public int Health;
        public int MaxHealth;
    }
}
