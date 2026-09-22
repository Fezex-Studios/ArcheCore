using System.Collections.Generic;
using System.Numerics;
using ArcheCore.Server.World.Core.Entities;
using ArcheCore.Server.World.GameData.Npcs;
using ArcheCore.Server.World.Managers;

namespace ArcheCore.Server.World.GameData.World.Spawners;

/// <summary>
/// Pure in-memory store of currently-live NPCs plus the interaction
/// registry glue. Deliberately knows nothing about networking or
/// interest management any more - SpawnManager owns when NPCs get
/// created/destroyed (spawner-radius gating) and NpcAiManager owns
/// telling nearby players about it (interest-driven replication).
/// This used to also broadcast a full-world NPC dump to every
/// connecting peer via SendToPeer(); that's gone, since it's exactly
/// the "spawn everything, tell everyone about it" pattern that doesn't
/// scale past a few hundred NPCs/players. See PlayerSpawnManager and
/// NpcAiManager for how connect-time NPC visibility works now.
/// </summary>
public class NpcSpawner(InteractionRegistry interactions)
{
    private readonly Dictionary<int, NpcEntity> _live = new();

    public NpcEntity SpawnFromTemplate(
        int networkId,
        int spawnerId,
        NpcTemplate template,
        Vector3 position)
    {
        var npc = new NpcEntity
        {
            NetworkId     = networkId,
            SpawnerId     = spawnerId,
            TemplateId    = template.Id,
            Name          = template.Name,
            Level         = template.Level,
            ModelType     = template.ModelType,
            InteractRange = template.InteractRange,
            IsStationary  = template.IsStationary,
            MaxHealth     = template.MaxHealth,
            Health        = template.MaxHealth,
            LootTableId   = template.LootTableId,
            RespawnSeconds = template.RespawnSeconds,
            Title         = template.Title ?? string.Empty,
            Greeting      = template.Greeting ?? string.Empty,
            Position      = position,
            SpawnOrigin   = position
        };

        _live[networkId] = npc;

        // Makes this NPC a valid C2WInteractPacket target.
        interactions.Register(networkId, npc);

        return npc;
    }

    public void DespawnNpc(int networkId)
    {
        _live.Remove(networkId);
        interactions.Unregister(networkId);
    }

    public bool TryGet(int networkId, out NpcEntity npc) =>
        _live.TryGetValue(networkId, out npc);

    public IEnumerable<NpcEntity> AllLive => _live.Values;

    public int LiveCount => _live.Count;
}
