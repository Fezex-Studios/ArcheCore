using System.Collections.Generic;
using System.Numerics;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Core.Entities;
using ArcheCore.Server.World.GameData.Npcs;
using ArcheCore.Server.World.Managers;
using LiteNetLib;

namespace ArcheCore.Server.World.GameData.World.Spawners;

public class NpcSpawner(ReplicationManager replication, InteractionRegistry interactions)
{
    private readonly Dictionary<int, NpcEntity> _live = new();

    public void SpawnFromTemplate(
        int networkId,
        NpcTemplate template,
        Vector3 position)
    {
        var npc = new NpcEntity
        {
            NetworkId     = networkId,
            TemplateId    = template.Id,
            Name          = template.Name,
            Level         = template.Level,
            ModelType     = template.ModelType,
            InteractRange = template.InteractRange,
            Position      = position
        };

        _live[networkId] = npc;

        // Makes this NPC a valid C2WInteractPacket target.
        interactions.Register(networkId, npc);
    }

    public void DespawnNpc(int networkId)
    {
        _live.Remove(networkId);
        interactions.Unregister(networkId);
    }

    public void SendToPeer(NetPeer peer)
    {
        foreach (var (_, npc) in _live)
            SendNpc(peer, npc);
    }

    private void SendNpc(NetPeer peer, NpcEntity npc)
    {
        replication.Send(Opcodes.SpawnNpc,
            new W2CSpawnNpcPacket
            {
                NetworkId  = npc.NetworkId,
                TemplateId = npc.TemplateId,
                Name       = npc.Name,
                Level      = npc.Level,
                ModelType  = npc.ModelType,
                X          = npc.Position.X,
                Y          = npc.Position.Y,
                Z          = npc.Position.Z,
                InteractRange = npc.InteractRange
            }, peer);
    }
}