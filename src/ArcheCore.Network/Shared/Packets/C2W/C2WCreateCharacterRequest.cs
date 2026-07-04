using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    [MessagePackObject(true)]
    public class C2WCreateCharacterRequest
    {
        public string Name;
    }
}