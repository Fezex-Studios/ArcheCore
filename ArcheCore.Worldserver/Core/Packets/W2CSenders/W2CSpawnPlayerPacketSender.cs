using ArcheCore.WorldServer.Managers;
using LiteNetLib;
using Shared;
using Shared.Components;
using UnityEngine;

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
                Opcode.SpawnPlayer,
                new W2CSpawnPlayerPacket
                {
                    NetworkId     = networkId,
                    x             = position.x,
                    y             = position.y,
                    z             = position.z,
                    IsLocalPlayer = isLocalPlayer
                },
                peer);
        }
    }
}