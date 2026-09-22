using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    /// <summary>"Bring me back." No position - the server picks the spawn point.</summary>
    [MessagePackObject(true)]
    public class C2WRespawnPacket
    {
    }
}
