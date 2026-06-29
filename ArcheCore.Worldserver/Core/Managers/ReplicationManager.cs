using System.Collections.Generic;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Net.Worldserver;
using LiteNetLib;


namespace ArcheCore.WorldServer.Managers
{
    public class ReplicationManager
    {
        public void Broadcast<T>(
            Opcodes opcode,
            T payload,
            IEnumerable<NetPeer> peers)
        {
            foreach (var peer in peers)
                PacketSender.SendPacket(peer, opcode, payload);
        }

        public void BroadcastExcept<T>(
            Opcodes opcode,
            T payload,
            IEnumerable<NetPeer> peers,
            NetPeer except)
        {
            foreach (var peer in peers)
            {
                if (peer == except) continue;
                PacketSender.SendPacket(peer, opcode, payload);
            }
        }

        public void Send<T>(
            Opcodes opcode,
            T payload,
            NetPeer peer)
        {
            PacketSender.SendPacket(peer, opcode, payload);
        }

        public void SendUnreliable<T>(
            Opcodes opcode,
            T payload,
            IEnumerable<NetPeer> peers,
            NetPeer except)
        {
            foreach (var peer in peers)
            {
                if (peer == except) continue;
                PacketSender.SendPacket(peer, opcode, payload, DeliveryMethod.Unreliable);
            }
        }
    }
}