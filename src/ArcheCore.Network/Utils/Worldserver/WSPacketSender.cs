using System.Collections.Generic;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Network.Worldserver
{
    public static class WorldserverPacketSender
    {
        /// <summary>
        /// The bytes on the wire for one packet: the opcode (ushort, little
        /// endian - what NetDataWriter.Put(ushort) wrote) followed by the
        /// MessagePack payload. Serialise ONCE and hand the same array to
        /// every recipient (audit M3).
        /// </summary>
        public static byte[] Encode<T>(Opcodes opcode, T payload)
        {
            byte[] body = MessagePackSerializer.Serialize(payload);
            var packet = new byte[body.Length + 2];

            ushort op = (ushort)opcode;
            packet[0] = (byte)op;
            packet[1] = (byte)(op >> 8);
            System.Buffer.BlockCopy(body, 0, packet, 2, body.Length);

            return packet;
        }

        public static void SendPacket<T>(
            NetPeer peer,
            Opcodes opcode,
            T payload,
            DeliveryMethod delivery = DeliveryMethod.ReliableOrdered)
        {
            peer.Send(Encode(opcode, payload), PacketChannels.For(opcode), delivery);
        }

        /// <summary>Send already-encoded bytes (from Encode) on the opcode's channel.</summary>
        public static void SendEncoded(
            NetPeer peer,
            Opcodes opcode,
            byte[] encoded,
            DeliveryMethod delivery = DeliveryMethod.ReliableOrdered)
        {
            peer.Send(encoded, PacketChannels.For(opcode), delivery);
        }

        /// <summary>One serialisation, many recipients (audit M3).</summary>
        public static void SendToAll<T>(
            IEnumerable<NetPeer> peers,
            Opcodes opcode,
            T payload,
            NetPeer? except = null,
            DeliveryMethod delivery = DeliveryMethod.ReliableOrdered)
        {
            byte[]? encoded = null;
            byte channel = PacketChannels.For(opcode);

            foreach (var peer in peers)
            {
                if (peer == except) continue;
                encoded ??= Encode(opcode, payload);   // nobody to send to = no serialisation
                peer.Send(encoded, channel, delivery);
            }
        }
    }
}
