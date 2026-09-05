// Shared/Packets/W2C/W2CCharacterListPacket.cs

using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    [MessagePackObject(true)]
    public class W2CCharacterListPacket
    {
        public CharacterSummary[] Characters; // reuse the P2W one, or duplicate if you want W2C decoupled
    }
}