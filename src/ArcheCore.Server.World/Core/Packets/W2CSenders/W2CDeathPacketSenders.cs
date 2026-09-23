using System.Numerics;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using LiteNetLib;

namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CPlayerDeathPacketSender
    {
        public static void Send(NetPeer peer, string killerName)
        {
            WorldserverPacketSender.SendPacket(
                peer,
                Opcodes.W2CPlayerDeath,
                new W2CPlayerDeathPacket { KillerName = killerName });
        }
    }

    public static class W2CRespawnPacketSender
    {
        public static void Send(NetPeer peer, Vector3 position, int health, int maxHealth)
        {
            WorldserverPacketSender.SendPacket(
                peer,
                Opcodes.W2CRespawn,
                new W2CRespawnPacket
                {
                    X = position.X,
                    Y = position.Y,
                    Z = position.Z,
                    Health = health,
                    MaxHealth = maxHealth
                });
        }
    }
}
