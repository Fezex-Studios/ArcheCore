using System.Collections.Generic;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Worldserver;
using LiteNetLib;


namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Sending one message to several players. Every broadcast serialises the
    /// payload ONCE and sends the same bytes to each recipient (audit M3) -
    /// it used to build a new writer and re-run MessagePack per peer. The
    /// channel comes from the opcode (PacketChannels, audit M4).
    /// </summary>
    public class ReplicationManager
    {
        public void Broadcast<T>(
            Opcodes opcode,
            T payload,
            IEnumerable<NetPeer> peers)
        {
            WorldserverPacketSender.SendToAll(peers, opcode, payload);
        }

        public void BroadcastExcept<T>(
            Opcodes opcode,
            T payload,
            IEnumerable<NetPeer> peers,
            NetPeer except)
        {
            WorldserverPacketSender.SendToAll(peers, opcode, payload, except);
        }

        public void Send<T>(
            Opcodes opcode,
            T payload,
            NetPeer peer)
        {
            WorldserverPacketSender.SendPacket(peer, opcode, payload);
        }

        public void SendUnreliable<T>(
            Opcodes opcode,
            T payload,
            IEnumerable<NetPeer> peers,
            NetPeer except)
        {
            WorldserverPacketSender.SendToAll(peers, opcode, payload, except, DeliveryMethod.Unreliable);
        }
    }
}
