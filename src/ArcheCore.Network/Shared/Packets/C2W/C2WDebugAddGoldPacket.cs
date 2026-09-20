using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    /// <summary>
    /// DEV ONLY - see C2WDebugAddGoldHandler. Not a real economy action;
    /// exists to prove the persistence round-trip (log in, gain gold, log
    /// out, log back in, balance survived) before anything real produces
    /// gold. Amount can be negative, to test the refusal-to-go-negative
    /// path in PlayerManager.TryAddGold without needing a shop yet.
    /// </summary>
    [MessagePackObject(true)]
    public class C2WDebugAddGoldPacket
    {
        public int Amount;
    }
}