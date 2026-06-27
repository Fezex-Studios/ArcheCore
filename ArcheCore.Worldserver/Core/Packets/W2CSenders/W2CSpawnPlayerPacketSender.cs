using System.Numerics;
using LiteNetLib;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.WorldServer.Managers;
using Shared;
using Shared.Components;

namespace ArcheCore.WorldServer.Networking.W2C
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