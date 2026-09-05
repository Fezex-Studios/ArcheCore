using System.Numerics;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using MessagePack;


namespace ArcheCore.Server.World.Networking.C2W
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
            if (!playerManager.TryGetNetworkId(
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

            // BroadcastPosition updates the session's stored position
            // itself now, so there's no separate dictionary write needed
            // here (the old direct write to playerManager.Positions was
            // redundant with what BroadcastPosition already did).
            playerManager.BroadcastPosition(
                peer,
                networkId,
                position);
        }
    }
}