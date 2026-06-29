using System.Collections.Generic;
using System.Numerics;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Core.Entities;
using ArcheCore.Server.World.GameData.Npcs;
using ArcheCore.Server.World.Managers;
using LiteNetLib;

namespace ArcheCore.Server.World.GameData.World.Spawners;

public class NpcSpawner(ReplicationManager replication)
{
    private readonly Dictionary<int, NpcEntity> _live = new();

    public void SpawnFromTemplate(
        int networkId,
        NpcTemplate template,
        Vector3 position)
    {
        _live[networkId] = new NpcEntity
        {
            NetworkId  = networkId,
            TemplateId = template.Id,
            Name       = template.Name,
            Level      = template.Level,
            ModelType  = template.ModelType,
            Position   = position
        };
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
                Z          = npc.Position.Z
            }, peer);
    }
}