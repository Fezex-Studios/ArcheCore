// Shared/Packets/W2P/W2PCharacterListRequest.cs

using MessagePack;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer.W2P
{
    [MessagePackObject(true)]
    public class W2PCharacterListRequest
    {
        public int AccountId;
    }
}