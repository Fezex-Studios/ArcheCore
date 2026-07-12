using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using LiteNetLib;

namespace ArcheCore.Server.World.Networking.W2C;

public static class W2CInteractLootPacketSender
{
    public static void Send(NetPeer peer, string itemName)
    {
        WorldserverPacketSender.SendPacket(
            peer,
            Opcodes.W2CInteractLoot,
            new W2CInteractLootPacket { ItemName = itemName });
    }
}