using ArcheCore.WorldServer.Managers;
using LiteNetLib;
using MessagePack;
using MMO.Shared.Packets;
using UnityEngine;

namespace ArcheCore.WorldServer.Networking.C2W
{
    public class C2WMovementHandler : IPacketHandler
    {
        private readonly PlayerManager playerManager;

        public C2WMovementHandler(
            PlayerManager playerManager)
        {
            this.playerManager = playerManager;
        }

        public void Handle(
            NetPeer peer,
            NetPacketReader reader)
        {
            if (!playerManager.PeerToId.TryGetValue(
                    peer,
                    out int networkId))
            {
                return;
            }

            C2WPlayerMovePacket packet =
                MessagePackSerializer
                    .Deserialize<C2WPlayerMovePacket>(
                        reader.GetRemainingBytes());

            Vector3 position = new Vector3(
                packet.x,
                packet.y,
                packet.z);

            playerManager.Positions[networkId] = position;

            playerManager.BroadcastPosition(
                peer,
                networkId,
                position);
        }
    }
}