using MessagePack;

namespace ArcheCore.Net.Shared.Packets.PersistenceServer
{
    [MessagePackObject(true)]
    public class PersistencePacket
    {
        public ushort Opcode;

        public byte[] Payload;
    }
}