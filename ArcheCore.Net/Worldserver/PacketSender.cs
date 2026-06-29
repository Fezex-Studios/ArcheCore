using ArcheCore.Library.Net.Worldserver;
using LiteNetLib;
using LiteNetLib.Utils;
using MessagePack;

namespace ArcheCore.Net.Worldserver
{
    public static class PacketSender
    {
        public static void SendPacket<T>(
            NetPeer peer,
            Opcodes opcode,
            T payload,
            DeliveryMethod delivery = DeliveryMethod.ReliableOrdered)
        {
            NetDataWriter writer =
                new();

            writer.Put(
                (ushort)opcode);

            writer.Put(
                MessagePackSerializer
                    .Serialize(payload));

            peer.Send(
                writer,
                delivery);
        }
    }
}