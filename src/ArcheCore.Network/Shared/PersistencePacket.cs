using MessagePack;

[MessagePackObject(true)]
public class PersistencePacket
{
    public ushort Opcode;

    public byte[] Payload;
}