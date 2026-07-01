using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using LiteNetLib;

namespace ArcheCore.Server.World.Networking.W2C;

public static class W2CPlayerLevelResponsePacketSender
{
    public static void Send(NetPeer peer, int level)
    {
        WorldserverPacketSender.SendPacket(
            peer,
            Opcodes.PlayerLevelResponse,
            new W2CPlayerLevelResponsePacket {Level = level}
            );
    }
}