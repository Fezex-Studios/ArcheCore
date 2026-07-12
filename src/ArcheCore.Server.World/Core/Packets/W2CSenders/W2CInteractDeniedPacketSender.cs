using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using LiteNetLib;

namespace ArcheCore.Server.World.Networking.W2C;

public static class W2CInteractDeniedPacketSender
{
    public static void Send(NetPeer peer, string reason)
    {
        WorldserverPacketSender.SendPacket(
            peer,
            Opcodes.W2CInteractDenied,
            new W2CInteractDeniedPacket { Reason = reason });
    }
}