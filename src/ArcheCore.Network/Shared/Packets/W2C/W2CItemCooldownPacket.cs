using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// A cooldown group just started. Sent only after the server has
    /// accepted a use, so the client's cooldown sweep always reflects
    /// something that actually happened.
    ///
    /// Carries the item ids in the group rather than just the group id,
    /// so the client never needs to know the group table - it just greys
    /// out any slot holding one of these ids. Using one health potion
    /// greys out every health potion, in any slot, which is the point of
    /// grouping.
    ///
    /// Purely cosmetic on the client. The server rejects an early use
    /// regardless of what the client displays.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CItemCooldownPacket
    {
        public int CooldownGroup;
        public int[] ItemTemplateIds;
        public int DurationMs;
    }
}