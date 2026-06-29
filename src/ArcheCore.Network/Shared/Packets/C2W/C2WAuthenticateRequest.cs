using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    [MessagePackObject(true)]
    public class C2WAuthenticateRequest
    {
        public string Token;
    }
}