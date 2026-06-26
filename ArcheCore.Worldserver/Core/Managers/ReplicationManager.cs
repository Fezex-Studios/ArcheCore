using System.Collections.Generic;
using LiteNetLib;
using Shared;

namespace ArcheCore.WorldServer.Managers
{
    public class ReplicationManager
    {
        public void Broadcast<T>(
            Opcode opcode,
            T payload,
            IEnumerable<NetPeer> peers)
        {
            foreach (var peer in peers)
                PacketSender.SendPacket(peer, opcode, payload);
        }

        public void BroadcastExcept<T>(
            Opcode opcode,
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
            Opcode opcode,
            T payload,
            NetPeer peer)
        {
            PacketSender.SendPacket(peer, opcode, payload);
        }

        public void SendUnreliable<T>(
            Opcode opcode,
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