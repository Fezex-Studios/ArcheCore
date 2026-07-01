using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using LiteNetLib;

namespace ArcheCore.Server.World.Networking.W2C;

public static class W2CTestPacketSender
{
    public static void Send(NetPeer peer, string message)
    {
        WorldserverPacketSender.SendPacket(
            peer,
            Opcodes.W2CTestPacket,
            new W2CTestPacket {Message =  message}
        );
    }
    
}