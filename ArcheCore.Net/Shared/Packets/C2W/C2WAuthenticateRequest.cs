using MessagePack;

namespace ArcheCore.Net.Shared.Packets.C2W
{
    [MessagePackObject(true)]
    public class C2WAuthenticateRequest
    {
        public string Token;
    }
}