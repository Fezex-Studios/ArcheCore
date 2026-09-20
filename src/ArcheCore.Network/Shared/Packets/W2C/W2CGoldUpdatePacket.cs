using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// Sent to one player: their current gold total, absolute (not a
    /// delta). Absolute rather than delta so a dropped packet can't leave
    /// the client's displayed balance permanently wrong - the next send
    /// self-corrects it regardless of what was missed.
    ///
    /// Sent on spawn, and whenever PlayerManager.TryAddGold changes the
    /// value. Not part of the world snapshot: gold is not spatial state,
    /// it doesn't need LOD tiers, and nobody but the owning player is ever
    /// told about it.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CGoldUpdatePacket
    {
        public int Gold;
    }
}