// Shared/Packets/C2W/C2WSelectCharacterRequest.cs

using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    [MessagePackObject(true)]
    public class C2WSelectCharacterRequest
    {
        public long CharacterId;
    }
}