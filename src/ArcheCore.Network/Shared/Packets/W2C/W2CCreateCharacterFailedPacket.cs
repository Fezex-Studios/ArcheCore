using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// Creating a character didn't work - the name is taken, reserved or not
    /// allowed. The connection stays open and the create screen shows Reason,
    /// so the player can just try another name.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CCreateCharacterFailedPacket
    {
        public string Reason = "";
    }
}
