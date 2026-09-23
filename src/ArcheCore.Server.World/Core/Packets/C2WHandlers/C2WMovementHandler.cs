using ArcheCore.Network.Shared;
using ArcheCore.Library.Net.Worldserver;
using System.Numerics;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Replication;
using LiteNetLib;
using MessagePack;


namespace ArcheCore.Server.World.Networking.C2W
{
    [PacketOpcode(Opcodes.PlayerMove)]
    public class C2WMovementHandler : IPacketHandler
    {
        private readonly PlayerManager playerManager;
        private readonly JumpEventBroadcaster jumps;

        public C2WMovementHandler(
            PlayerManager playerManager,
            JumpEventBroadcaster jumps)
        {
            this.playerManager = playerManager;
            this.jumps = jumps;
        }

        public void Handle(
            NetPeer peer,
            NetPacketReader reader)
        {
            // Session rather than just the network id now - the validator
            // keeps its per-player state on the session, so it needs the
            // object, not the number.
            if (!playerManager.TryGetSession(peer, out var session) ||
                session.NetworkId is not int networkId)
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

            Vector3 velocity = new Vector3(
                packet.vx,
                packet.vy,
                packet.vz);

            // THE GATE. Everything downstream of this line treats the
            // position as true: it goes into the interest grid (which
            // buckets by coordinate), into the snapshot transform store,
            // into session.Position, and from there into the character's
            // saved row at the next autosave. A position reaching any of
            // those is one the server has committed to, so the check
            // belongs here, before the first of them, not further in.
            //
            // A rejection is not an error and isn't logged here -
            // MovementValidator logs the ones that matter. Ordinary
            // rejections happen to honest clients on bad connections and
            // just mean this packet isn't applied; the next one usually is.
            // Checked = the same validation, plus "not while dead" (roadmap J).
            if (!playerManager.TryAcceptMovementChecked(peer, session, position, velocity))
                return;

            // Jump detection, AFTER the gate. A rejected position must not
            // author a jump event, or a client that gets snapped back for
            // speed hacking still gets every observer to render an arc
            // starting from a position the server refused to believe.
            //
            // Cheap in the common case: one dictionary lookup and a bit
            // test. Only the rising edge of MovementState.Jumping produces
            // a packet, so holding the bit set for the whole arc - which is
            // what the client does - costs nothing after the first tick.
            jumps.OnMovement(networkId, position, velocity, packet.state);

            // Rotation and state are NOT validated and are relayed as sent.
            // That is deliberate, and safe only for as long as nothing on
            // the server reads them: they exist so other clients can face
            // and animate this character, and a client lying about them
            // makes itself look wrong to other people and nothing more. The
            // moment any server logic branches on state - a speed limit
            // that relaxes for MovementState.Gliding, an interrupt that
            // checks InCombat - the claim has to be verified against what
            // the server knows the character actually has and is doing,
            // because at that point the byte becomes worth lying about.
            //
            // The Jumping bit is the first partial exception. Nothing here
            // trusts the client's vertical velocity - JumpEventBroadcaster
            // substitutes the server's own MovementConstants.JumpVelocity -
            // precisely because observers SIMULATE that number for most of
            // a second, which is the one case where a lie about state stops
            // being self-inflicted.
            playerManager.BroadcastPosition(
                peer,
                networkId,
                position,
                velocity,
                packet.yaw,
                packet.pitch,
                packet.roll,
                packet.state);
        }
    }
}