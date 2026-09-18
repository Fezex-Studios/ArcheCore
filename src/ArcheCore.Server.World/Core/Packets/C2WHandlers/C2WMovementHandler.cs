using ArcheCore.Network.Shared;
using ArcheCore.Library.Net.Worldserver;
using System.Numerics;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using MessagePack;


namespace ArcheCore.Server.World.Networking.C2W
{
    [PacketOpcode(Opcodes.PlayerMove)]
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

            // Velocity and yaw are presentation state for OTHER clients -
            // they are what lets an observer extrapolate through a dropped
            // snapshot and face the character correctly while it turns on
            // the spot. Nothing on the server simulates from them.
            //
            // NOTE ON TRUST: this handler takes the client's word for all
            // of it, exactly as it did for position before. That is a known
            // gap, not an oversight - movement validation (max speed
            // between reports, terrain/collision sanity, teleport
            // detection) belongs here and needs the previous accepted
            // position plus a timestamp to work against. Worth doing before
            // this is exposed to untrusted players; a fabricated velocity
            // today only makes the sender look wrong on other people's
            // screens, but a fabricated POSITION is a straightforward
            // teleport hack.
            Vector3 velocity = new Vector3(
                packet.vx,
                packet.vy,
                packet.vz);

            // BroadcastPosition updates the session's stored position
            // itself now, so there's no separate dictionary write needed
            // here (the old direct write to playerManager.Positions was
            // redundant with what BroadcastPosition already did).
            playerManager.BroadcastPosition(
                peer,
                networkId,
                position,
                velocity,
                packet.yaw);
        }
    }
}