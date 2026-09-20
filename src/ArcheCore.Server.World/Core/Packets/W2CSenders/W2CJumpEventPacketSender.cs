using System.Collections.Generic;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using Shared;

namespace ArcheCore.Server.World.Networking.W2C
{
    /// <summary>
    /// Sends W2CJumpEvent to a set of observers.
    ///
    /// Goes out through ReplicationManager.Broadcast, which is the RELIABLE
    /// path - the same one spawn and leave use. That is the important
    /// difference from a snapshot: snapshots are unreliable because a lost
    /// one is replaced 50ms later, but a lost jump event is never replaced.
    /// The observer would simply never start the arc, and watch the
    /// character skate along the ground until a positional update dragged
    /// it upward.
    /// </summary>
    public static class W2CJumpEventPacketSender
    {
        public static void Send(
            ReplicationManager replication,
            IEnumerable<NetPeer> peers,
            W2CJumpEventPacket packet)
        {
            replication.Broadcast(Opcodes.W2CJumpEvent, packet, peers);
        }
    }
}