using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Core.Entities;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Core.Interaction;
using LiteNetLib;
using Shared;

namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CSpawnNpcPacketSender
    {
        public static void Send(
            ReplicationManager replication,
            NetPeer peer,
            NpcEntity npc,
            InteractionActionData[] actions)
        {
            replication.Send(
                Opcodes.SpawnNpc,
                new W2CSpawnNpcPacket
                {
                    NetworkId     = npc.NetworkId,
                    TemplateId    = npc.TemplateId,
                    Name          = npc.Name,
                    Level         = npc.Level,
                    ModelType     = npc.ModelType,
                    X             = npc.Position.X,
                    Y             = npc.Position.Y,
                    Z             = npc.Position.Z,
                    InteractRange = npc.InteractRange,
                    Health        = npc.Health,
                    MaxHealth     = npc.MaxHealth,
                    Title         = npc.Title,
                    Actions       = actions
                },
                peer);
        }
    }
}
