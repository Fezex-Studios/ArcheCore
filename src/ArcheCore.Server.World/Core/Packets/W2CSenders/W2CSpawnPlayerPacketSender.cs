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
            bool isLocalPlayer,
            PlayerSession? subject = null)   // nullable: older call sites pass nothing
        {
            replication.Send(
                Opcodes.SpawnPlayer,
                new W2CSpawnPlayerPacket
                {
                    NetworkId     = networkId,
                    x             = position.X,
                    y             = position.Y,
                    z             = position.Z,
                    IsLocalPlayer = isLocalPlayer,

                    // The spawned player's own session, when the caller has
                    // it: their name for the nameplate and their health for
                    // the target frame. Null (an older call site) just means
                    // the client shows a nameless plate with no bar.
                    Name          = subject?.Name ?? string.Empty,
                    Health        = subject?.Health ?? 0,
                    MaxHealth     = subject?.MaxHealth ?? 0
                },
                peer);
        }
    }
}