using LiteNetLib;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;

namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CMOTDPacketSender
    {
        public static void Send(NetPeer peer, string message)
        {
            WorldserverPacketSender.SendPacket(
                peer,
                Opcodes.MOTD,
                new W2CMOTDPacket { Message = message });
        }
    }
}