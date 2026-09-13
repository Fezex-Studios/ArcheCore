using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using LiteNetLib;

namespace ArcheCore.Server.World.Networking.W2C;

public static class W2CCharacterDataPacketSender
{
    public static void Send(NetPeer peer, CharacterData data)
    {
        WorldserverPacketSender.SendPacket(
            peer,
            Opcodes.PlayerSpawned,   // matches client's W2CCharacterDataHandler registration
            data);
    }
}