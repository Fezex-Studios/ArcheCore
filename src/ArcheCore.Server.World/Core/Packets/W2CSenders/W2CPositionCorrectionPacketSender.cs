using System.Numerics;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using Shared;

namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CPositionCorrectionPacketSender
    {
        /// <summary>
        /// RELIABLE, unlike every other packet on the movement path.
        /// Dropping a snapshot costs one frame of smoothness; dropping a
        /// correction leaves the offending client desynced until something
        /// else happens to resync it, which is the exact condition the
        /// correction exists to end.
        /// </summary>
        public static void Send(ReplicationManager replication, NetPeer peer, Vector3 position)
        {
            // ReplicationManager.Send is the single-peer path and uses
            // WorldserverPacketSender's default delivery method, which is
            // reliable - unlike SendUnreliable, which everything else on
            // the movement path uses.
            replication.Send(
                Opcodes.W2CPositionCorrection,
                new W2CPositionCorrectionPacket
                {
                    x = position.X,
                    y = position.Y,
                    z = position.Z
                },
                peer);
        }
    }
}