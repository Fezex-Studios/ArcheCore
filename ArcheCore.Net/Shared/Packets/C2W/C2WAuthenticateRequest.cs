using MessagePack;

namespace MMO.Shared.Packets
{
    [MessagePackObject(true)]
    public class C2WAuthenticateRequest
    {
        public string Token;
    }
}