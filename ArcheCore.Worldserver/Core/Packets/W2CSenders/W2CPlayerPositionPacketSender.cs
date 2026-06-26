using System.Collections.Generic;
using ArcheCore.WorldServer.Managers;
using LiteNetLib;
using Shared;
using Shared.Components;
using UnityEngine;

namespace ArcheCore.WorldServer.Networking.W2C
{
    public static class W2CPlayerPositionPacketSender
    {
        public static void SendUnreliable(
            ReplicationManager replication,
            IEnumerable<NetPeer> peers,
            NetPeer except,
            int networkId,
            Vector3 position)
        {
            replication.SendUnreliable(
                Opcode.PlayerPosition,
                new W2CPlayerPositionPacket
                {
                    NetworkId = networkId,
                    x         = position.x,
                    y         = position.y,
                    z         = position.z
                },
                peers,
                except);
        }
    }
}