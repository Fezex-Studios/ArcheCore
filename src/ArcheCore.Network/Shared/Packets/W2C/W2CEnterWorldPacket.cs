using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// Everything the client needs to render itself the moment it enters
    /// the world - name, level, gold, and full inventory - in one atomic
    /// send from PlayerSpawnManager.SpawnPlayer. No handshake: this is
    /// pushed blind, the same way Gold and Inventory always were, now
    /// covering CharacterData too instead of that being a separate
    /// client-request round trip.
    ///
    /// This replaces the INITIAL send only. Everything that happens after
    /// entering the world still goes through the existing delta packets -
    /// W2CGoldUpdate on a purchase, W2CInventorySlotChanged on a pickup,
    /// W2CPlayerLevelResponse on a level-up. Adding a new category of
    /// starting state later (equipment, skills, XP) means adding one field
    /// here, not adding a fourth "how do I get my initial X" mechanism.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CEnterWorldPacket
    {
        public CharacterData Character;
        public int Gold;
        public InventorySlotData[] Inventory;

        /// <summary>Your health on entering the world (full, for now - health isn't saved yet).</summary>
        public int Health;
        public int MaxHealth;
    }
}
