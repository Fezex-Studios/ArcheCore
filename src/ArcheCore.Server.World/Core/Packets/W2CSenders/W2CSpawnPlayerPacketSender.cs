using System.Numerics;
using LiteNetLib;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Managers;
using Shared;


namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CSpawnPlayerPacketSender
    {
        public static void Send(
            ReplicationManager replication,
            NetPeer peer,
            int networkId,
            Vector3 position,
            bool isLocalPlayer)
        {
            replication.Send(
                Opcodes.SpawnPlayer,
                new W2CSpawnPlayerPacket
                {
                    NetworkId     = networkId,
                    x             = position.X,
                    y             = position.Y,
                    z             = position.Z,
                    IsLocalPlayer = isLocalPlayer
                },
                peer);
        }
    }
}