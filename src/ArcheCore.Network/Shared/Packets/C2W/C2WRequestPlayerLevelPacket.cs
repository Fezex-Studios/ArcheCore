using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    [MessagePackObject(true)]
    public class C2WRequestPlayerLevelPacket
    {
        // empty payload for now - server just uses the sender's peer.
        // (add a RequestId field here later if you need to correlate
        // multiple concurrent requests)
    }
}