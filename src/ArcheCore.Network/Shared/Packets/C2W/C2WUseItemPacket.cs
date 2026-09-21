using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    /// <summary>
    /// "Use whatever is in Slot." What that means - heal, buff, consume or
    /// not, how long the cooldown is - comes from the item's ItemUse row
    /// on the server, never from the client. See PlayerManager.TryUseItem.
    /// </summary>
    [MessagePackObject(true)]
    public class C2WUseItemPacket
    {
        public int Slot;
    }
}