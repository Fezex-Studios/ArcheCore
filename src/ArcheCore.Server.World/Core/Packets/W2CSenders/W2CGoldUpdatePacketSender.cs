using LiteNetLib;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;

namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CGoldUpdatePacketSender
    {
        public static void Send(NetPeer peer, int gold)
        {
            WorldserverPacketSender.SendPacket(
                peer,
                Opcodes.W2CGoldUpdate,
                new W2CGoldUpdatePacket { Gold = gold });
        }
    }
}