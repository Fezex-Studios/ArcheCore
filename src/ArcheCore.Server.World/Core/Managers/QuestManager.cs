using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Core.Entities;
using ArcheCore.Server.World.Core.Interaction;
using ArcheCore.Server.World.GameData.Quests;
using ArcheCore.Server.World.Lua.Scripting;
using ArcheCore.Server.World.Lua.Scripting.Bindings;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Networking.W2C;
using ArcheCore.Server.World.Utils.Database.SQLite;
using LiteNetLib;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;

/// <summary>
/// Quests: the definitions, each character's state, and everything that moves
/// one forward. Roadmap K (persistence), L (events) and M (givers).
///
/// HOW PROGRESS HAPPENS
///
/// Nothing here polls and nothing here is wired to combat or harvesting
/// directly. QuestManager listens to LuaEngine.EventFired - the SAME events
/// Lua scripts hook - so killing an orc advances a quest for exactly the
/// reason it runs a script: PlayerManager fired OnKill. One event system,
/// two listeners.
///
///   OnKill     -> Kill objectives for that NPC template
///   OnInteract -> Talk objectives for that NPC template
///   OnHarvest  -> recount Collect objectives (the item just landed in the bag)
///
/// Collect objectives are COUNTED FROM THE BAG rather than accumulated, which
/// is what makes them behave: buy the ore, loot it, mine it, or drop it and
/// they follow, because the quest asks "do you have five" rather than "did
/// five ever pass through your hands".
///
/// WHAT THE CLIENT KNOWS
///
/// The whole quest catalogue goes out once on entering the world, then only
/// state changes. The client can then draw the log, the tracker and the "!"
/// markers on its own.
///
/// EVERY REQUEST IS RE-CHECKED. Accepting re-checks the giver, range, level,
/// prerequisite and that it isn't already taken; handing in re-checks the
/// objectives and takes the Collect items. The client's buttons are a
/// convenience, never the authority.
/// </summary>
public class QuestManager : ArcheCore.Server.World.Core.Services.IInitializable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>Same slack as every other interaction range check.</summary>
    private const float RangeTolerance = 1.0f;

    private readonly IServiceScopeFactory _scopeFactory;

    // Set by Initialize, which WorldServer calls once everything exists.
    private PlayerManager _players;
    private ItemManager _items;
    private SpawnManager _spawnManager;

    private sealed class Definition
    {
        public QuestTable Record;
        public QuestObjectiveTable[] Objectives;
        public QuestDefinitionData ClientData;
    }

    private readonly Dictionary<int, Definition> _quests = new();
    private QuestDefinitionData[] _catalog = Array.Empty<QuestDefinitionData>();

    public QuestManager(IServiceScopeFactory scopeFactory, ILogger<QuestManager> logger)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Two-phase start-up (IInitializable): everything exists by now.
    /// Subscribing to the Lua engine here is what wires quests to kills,
    /// talks and harvests.
    /// </summary>
    public void Initialize(ArcheCore.Server.World.Core.Services.ServiceContainer services)
    {
        _players = services.Get<PlayerManager>();
        _items = services.Get<ItemManager>();
        _spawnManager = services.Get<SpawnManager>();

        var luaEngine = _players.LuaEngine;
        if (luaEngine != null)
            luaEngine.EventFired += OnGameEvent;
    }

    // ── Definitions ──────────────────────────────────────────────────

    public void LoadFromDatabase()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDataDbContext>();

        var objectivesByQuest = db.QuestObjectives.ToList()
            .GroupBy(o => o.QuestId)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.SortOrder).ThenBy(o => o.Id).ToArray());

        _quests.Clear();

        foreach (var quest in db.Quests.ToList())
        {
            objectivesByQuest.TryGetValue(quest.Id, out var objectives);
            objectives ??= Array.Empty<QuestObjectiveTable>();

            if (objectives.Length == 0)
            {
                // A quest with nothing to do can never be finished, so it
                // would sit in the log forever - refuse it at boot instead.
                Logger.Warn("[Quests] Quest {Id} '{Name}' has no objectives - skipped", quest.Id, quest.Name);
                continue;
            }

            bool bad = false;
            foreach (var o in objectives)
            {
                string problem =
                    !Enum.IsDefined(typeof(QuestObjectiveType), o.ObjectiveType) ||
                    o.ObjectiveType == (int)QuestObjectiveType.None ? $"objective {o.Id}: unknown type {o.ObjectiveType}" :
                    o.RequiredCount < 1                             ? $"objective {o.Id}: RequiredCount must be at least 1" :
                    o.ObjectiveType == (int)QuestObjectiveType.Collect && !_items.Exists(o.TargetId)
                                                                    ? $"objective {o.Id}: item {o.TargetId} is not in Items" :
                    null;

                if (problem != null)
                {
                    Logger.Warn("[Quests] Quest {Id} '{Name}' skipped - {Problem}", quest.Id, quest.Name, problem);
                    bad = true;
                    break;
                }
            }

            if (bad)
                continue;

            if (quest.RewardItemId != 0 && !_items.Exists(quest.RewardItemId))
            {
                Logger.Warn("[Quests] Quest {Id} '{Name}' rewards item {Item}, which is not in Items - skipped",
                    quest.Id, quest.Name, quest.RewardItemId);
                continue;
            }

            _quests[quest.Id] = new Definition
            {
                Record = quest,
                Objectives = objectives,
                ClientData = BuildClientData(quest, objectives)
            };
        }

        _catalog = _quests.Values.Select(d => d.ClientData).ToArray();
        Logger.Info("[Quests] Loaded {Count} quest(s).", _quests.Count);
    }

    private QuestDefinitionData BuildClientData(QuestTable quest, QuestObjectiveTable[] objectives)
    {
        var data = new QuestDefinitionData
        {
            Id                  = quest.Id,
            Name                = quest.Name ?? "",
            Description         = quest.Description ?? "",
            AcceptText          = quest.AcceptText ?? "",
            CompleteText        = quest.CompleteText ?? "",
            MinLevel            = quest.MinLevel,
            GiverNpcTemplateId  = quest.GiverNpcTemplateId,
            TurnInNpcTemplateId = quest.TurnInNpcTemplateId != 0 ? quest.TurnInNpcTemplateId : quest.GiverNpcTemplateId,
            RewardGold          = quest.RewardGold,
            RewardItemId        = quest.RewardItemId,
            RewardItemQuantity  = quest.RewardItemQuantity,
            RewardItemName      = quest.RewardItemId != 0 ? ItemName(quest.RewardItemId) : "",
            Objectives          = new QuestObjectiveData[objectives.Length]
        };

        for (int i = 0; i < objectives.Length; i++)
        {
            var o = objectives[i];
            data.Objectives[i] = new QuestObjectiveData
            {
                ObjectiveType = o.ObjectiveType,
                TargetId      = o.TargetId,
                RequiredCount = o.RequiredCount,
                Text          = string.IsNullOrWhiteSpace(o.Text) ? GenerateText(o) : o.Text
            };
        }

        return data;
    }

    /// <summary>A readable line for an objective row that didn't write its own.</summary>
    private string GenerateText(QuestObjectiveTable o) => (QuestObjectiveType)o.ObjectiveType switch
    {
        QuestObjectiveType.Kill    => $"Slay {o.RequiredCount}",
        QuestObjectiveType.Collect => $"Collect {o.RequiredCount}x {ItemName(o.TargetId)}",
        QuestObjectiveType.Talk    => "Speak to them",
        _ => "Do the thing"
    };

    private string ItemName(int itemId) => _items?.GetById(itemId)?.name ?? $"#{itemId}";

    public QuestDefinitionData[] Catalog => _catalog;

    // ── Loading and saving a character ───────────────────────────────

    /// <summary>Rebuild a character's quest state from storage, on spawn.</summary>
    public void LoadInto(PlayerSession session, QuestStateDto[] stored)
    {
        // From here on the saves own the quest rows (QuestComponent.Loaded):
        // every snapshot sends the whole log, carried-along unknown rows
        // included. Saving is QuestComponent.WriteTo.
        session.Quests.Clear();
        session.Quests.Loaded = true;

        if (stored == null)
            return;

        List<QuestStateDto> unknown = null;

        foreach (var row in stored)
        {
            if (!_quests.TryGetValue(row.QuestId, out var definition))
            {
                // The quest was deleted from the data since they played.
                // Nothing to show - but saves replace the whole quest log,
                // so the row is carried along untouched rather than erased.
                Logger.Debug("[Quests] Character has unknown quest {Id} - kept, not shown", row.QuestId);
                (unknown ??= new List<QuestStateDto>()).Add(row);
                continue;
            }

            session.Quests.Progress[row.QuestId] = new QuestProgress
            {
                QuestId = row.QuestId,
                Status  = (QuestStatus)row.Status,
                Counts  = QuestProgress.ParseCounts(row.Progress, definition.Objectives.Length)
            };
        }

        session.Quests.UnknownRows = unknown?.ToArray();
    }

    /// <summary>Catalogue then state, right after W2CEnterWorld.</summary>
    public void SendOnEnterWorld(NetPeer peer, PlayerSession session)
    {
        W2CQuestCatalogPacketSender.Send(peer, _catalog);
        RecountCollectObjectives(peer, session, announce: false);
        W2CQuestLogPacketSender.Send(peer, BuildLog(session));
    }

    private QuestProgressData[] BuildLog(PlayerSession session) =>
        session.Quests.Progress.Values.Select(ToClient).ToArray();

    private static QuestProgressData ToClient(QuestProgress quest) => new()
    {
        QuestId = quest.QuestId,
        Status  = (int)quest.Status,
        Counts  = quest.Counts
    };

    // ── Quest givers (roadmap M) ─────────────────────────────────────

    /// <summary>The Quests action on an NPC: what they can give or take right now.</summary>
    public void SendOffers(NetPeer peer, PlayerSession session, NpcEntity npc)
    {
        RecountCollectObjectives(peer, session, announce: true);

        var available = new List<int>();
        var completable = new List<int>();
        var inProgress = new List<int>();

        foreach (var definition in _quests.Values)
        {
            var quest = definition.ClientData;
            bool gives = quest.GiverNpcTemplateId == npc.TemplateId;
            bool takes = quest.TurnInNpcTemplateId == npc.TemplateId;

            session.Quests.Progress.TryGetValue(quest.Id, out var state);

            if (state == null || state.Status == QuestStatus.None)
            {
                if (gives && CanAccept(session, definition, out _))
                    available.Add(quest.Id);

                continue;
            }

            if (state.Status == QuestStatus.TurnedIn)
                continue;

            if (!takes)
                continue;

            if (state.Status == QuestStatus.Complete) completable.Add(quest.Id);
            else inProgress.Add(quest.Id);
        }

        W2CQuestOffersPacketSender.Send(peer, npc.NetworkId, npc.Name, available.ToArray(), completable.ToArray(), inProgress.ToArray());
    }

    private bool CanAccept(PlayerSession session, Definition definition, out string reason)
    {
        var quest = definition.Record;

        if (session.Quests.Progress.TryGetValue(quest.Id, out var existing) && existing.Status != QuestStatus.None)
        {
            reason = existing.Status == QuestStatus.TurnedIn ? "You've already done that." : "You already have that quest.";
            return false;
        }

        if (session.Level < quest.MinLevel)
        {
            reason = $"You must be level {quest.MinLevel}.";
            return false;
        }

        if (quest.RequiredQuestId != 0 &&
            (!session.Quests.Progress.TryGetValue(quest.RequiredQuestId, out var prerequisite) || prerequisite.Status != QuestStatus.TurnedIn))
        {
            reason = "You're not ready for that yet.";
            return false;
        }

        reason = null;
        return true;
    }

    public void TryAccept(NetPeer peer, int npcNetworkId, int questId)
    {
        if (!Resolve(peer, npcNetworkId, questId, out var session, out var npc, out var definition))
            return;

        if (definition.ClientData.GiverNpcTemplateId != npc.TemplateId)
        {
            W2CInteractDeniedPacketSender.Send(peer, $"{npc.Name} doesn't have that for you.");
            return;
        }

        if (!CanAccept(session, definition, out string reason))
        {
            W2CInteractDeniedPacketSender.Send(peer, reason);
            return;
        }

        var progress = new QuestProgress
        {
            QuestId = questId,
            Status  = QuestStatus.Active,
            Counts  = new int[definition.Objectives.Length]
        };

        session.Quests.Progress[questId] = progress;
        session.Quests.Dirty = true;

        // Collect objectives may already be satisfied by what's in the bag.
        RecountCollectObjectives(peer, session, announce: false);
        CheckCompletion(peer, session, progress, definition);

        string message = !string.IsNullOrWhiteSpace(definition.Record.AcceptText)
            ? definition.Record.AcceptText
            : $"Quest accepted: {definition.Record.Name}";

        W2CQuestUpdatePacketSender.Send(peer, ToClient(progress), message, removed: false);
        Logger.Info("[Quests] Account {Account} accepted '{Quest}'", session.AccountId, definition.Record.Name);
    }

    public void TryComplete(NetPeer peer, int npcNetworkId, int questId)
    {
        if (!Resolve(peer, npcNetworkId, questId, out var session, out var npc, out var definition))
            return;

        if (definition.ClientData.TurnInNpcTemplateId != npc.TemplateId)
        {
            W2CInteractDeniedPacketSender.Send(peer, $"{npc.Name} isn't who you report to.");
            return;
        }

        if (!session.Quests.Progress.TryGetValue(questId, out var progress) || progress.Status == QuestStatus.None)
        {
            W2CInteractDeniedPacketSender.Send(peer, "You don't have that quest.");
            return;
        }

        if (progress.Status == QuestStatus.TurnedIn)
        {
            W2CInteractDeniedPacketSender.Send(peer, "You've already done that.");
            return;
        }

        // Re-check rather than trust the stored status: the bag may have
        // changed since the objectives were last counted.
        RecountCollectObjectives(peer, session, announce: false);

        if (!ObjectivesMet(progress, definition))
        {
            W2CInteractDeniedPacketSender.Send(peer, "You haven't finished that yet.");
            return;
        }

        // Room for the reward BEFORE anything is taken - otherwise a full bag
        // eats the quest items and gives nothing back.
        var reward = definition.Record;
        if (reward.RewardItemId != 0 && reward.RewardItemQuantity > 0 &&
            !_players.CanAddItem(peer, reward.RewardItemId, reward.RewardItemQuantity))
        {
            W2CInteractDeniedPacketSender.Send(peer, "Your inventory is full.");
            return;
        }

        if (!TakeCollectItems(peer, session, definition))
        {
            W2CInteractDeniedPacketSender.Send(peer, "You're missing something you need to hand in.");
            return;
        }

        if (reward.RewardGold > 0)
            _players.TryAddGold(peer, reward.RewardGold);

        if (reward.RewardItemId != 0 && reward.RewardItemQuantity > 0)
            _players.TryAddItem(peer, reward.RewardItemId, reward.RewardItemQuantity);

        progress.Status = QuestStatus.TurnedIn;
        session.Quests.Dirty = true;

        string message = !string.IsNullOrWhiteSpace(reward.CompleteText)
            ? reward.CompleteText
            : $"Quest complete: {reward.Name}";

        W2CQuestUpdatePacketSender.Send(peer, ToClient(progress), message, removed: false);

        // Anything the quest granted is a normal reward line too.
        var earned = new List<string>();
        if (reward.RewardGold > 0) earned.Add($"{reward.RewardGold}g");
        if (reward.RewardItemId != 0 && reward.RewardItemQuantity > 0)
            earned.Add($"{reward.RewardItemQuantity}x {ItemName(reward.RewardItemId)}");
        if (earned.Count > 0)
            W2CInteractLootPacketSender.Send(peer, string.Join(", ", earned));

        Logger.Info("[Quests] Account {Account} completed '{Quest}'", session.AccountId, reward.Name);

        // A hand-in can open the next quest in a chain - refresh the giver.
        SendOffers(peer, session, npc);
    }

    public void TryAbandon(NetPeer peer, int questId)
    {
        if (!_players.TryGetSession(peer, out var session) || session.NetworkId == null)
            return;

        if (!session.Quests.Progress.TryGetValue(questId, out var progress))
            return;

        if (progress.Status == QuestStatus.TurnedIn)
        {
            W2CInteractDeniedPacketSender.Send(peer, "That one's already done.");
            return;
        }

        // Forgotten entirely, so it can be taken again - the save turns a
        // status of 0 into a deleted row.
        progress.Status = QuestStatus.None;
        session.Quests.Progress.Remove(questId);
        session.Quests.Dirty = true;

        string name = _quests.TryGetValue(questId, out var definition) ? definition.Record.Name : "Quest";
        W2CQuestUpdatePacketSender.Send(peer, ToClient(progress), $"Abandoned: {name}", removed: true);
    }

    private bool Resolve(NetPeer peer, int npcNetworkId, int questId,
                         out PlayerSession session, out NpcEntity npc, out Definition definition)
    {
        npc = null;
        definition = null;

        if (!_players.TryGetSession(peer, out session) || session.NetworkId == null)
            return false;

        if (session.Combat.IsDead)
        {
            W2CInteractDeniedPacketSender.Send(peer, "You can't do that while dead.");
            return false;
        }

        if (!_quests.TryGetValue(questId, out definition))
            return false;

        if (!_spawnManager.TryGetNpc(npcNetworkId, out npc))
        {
            W2CInteractDeniedPacketSender.Send(peer, "They're no longer here.");
            return false;
        }

        if (Vector3.Distance(session.Position, npc.Position) > npc.InteractRange + RangeTolerance)
        {
            W2CInteractDeniedPacketSender.Send(peer, "Too far away.");
            return false;
        }

        return true;
    }

    // ── Progress (roadmap L) ─────────────────────────────────────────

    /// <summary>
    /// Every fired game event, Lua's and ours alike. Cheap on purpose: it
    /// runs on the tick thread for every kill, harvest and conversation.
    /// </summary>
    private void OnGameEvent(PlayerEvent evt, object[] args)
    {
        if (args == null || args.Length == 0 || args[0] is not LuaPlayer player)
            return;

        if (!_players.TryGetPeer(player.NetworkId, out var peer) ||
            !_players.TryGetSession(peer, out var session) ||
            session.Quests.Progress.Count == 0)
            return;

        switch (evt)
        {
            case PlayerEvent.OnKill when args.Length >= 2 && args[1] is int npcTemplateId:
                Advance(peer, session, QuestObjectiveType.Kill, npcTemplateId, 1);
                break;

            case PlayerEvent.OnInteract when args.Length >= 3 && args[1] is int templateId && args[2] is int kind:
                if (kind == (int)InteractableKind.Npc)
                    Advance(peer, session, QuestObjectiveType.Talk, templateId, 1);
                break;

            case PlayerEvent.OnHarvest:
                // The item is already in the bag; Collect objectives are
                // counted from there rather than added up.
                RecountCollectObjectives(peer, session, announce: true);
                break;
        }
    }

    /// <summary>Tick a counting objective on every active quest that wants it.</summary>
    private void Advance(NetPeer peer, PlayerSession session, QuestObjectiveType type, int targetId, int amount)
    {
        foreach (var progress in session.Quests.Progress.Values.ToList())
        {
            if (progress.Status != QuestStatus.Active) continue;
            if (!_quests.TryGetValue(progress.QuestId, out var definition)) continue;

            bool changed = false;

            for (int i = 0; i < definition.Objectives.Length && i < progress.Counts.Length; i++)
            {
                var objective = definition.Objectives[i];
                if (objective.ObjectiveType != (int)type || objective.TargetId != targetId) continue;
                if (progress.Counts[i] >= objective.RequiredCount) continue;

                progress.Counts[i] = Math.Min(objective.RequiredCount, progress.Counts[i] + amount);
                changed = true;
            }

            if (!changed) continue;

            session.Quests.Dirty = true;
            Announce(peer, progress, definition);
            CheckCompletion(peer, session, progress, definition);
        }
    }

    /// <summary>
    /// Collect objectives always mirror the bag, so they go up when you pick
    /// something up and back down when you sell or drop it.
    /// </summary>
    private void RecountCollectObjectives(NetPeer peer, PlayerSession session, bool announce)
    {
        if (session.Quests.Progress.Count == 0) return;

        foreach (var progress in session.Quests.Progress.Values.ToList())
        {
            if (progress.Status is not (QuestStatus.Active or QuestStatus.Complete)) continue;
            if (!_quests.TryGetValue(progress.QuestId, out var definition)) continue;

            bool changed = false;

            for (int i = 0; i < definition.Objectives.Length && i < progress.Counts.Length; i++)
            {
                var objective = definition.Objectives[i];
                if (objective.ObjectiveType != (int)QuestObjectiveType.Collect) continue;

                int have = Math.Min(objective.RequiredCount, CountInBag(session, objective.TargetId));
                if (have == progress.Counts[i]) continue;

                progress.Counts[i] = have;
                changed = true;
            }

            if (!changed) continue;

            session.Quests.Dirty = true;

            if (announce)
                Announce(peer, progress, definition);

            // Losing an item can un-complete a quest, so re-check both ways.
            bool met = ObjectivesMet(progress, definition);
            var wanted = met ? QuestStatus.Complete : QuestStatus.Active;

            if (progress.Status != wanted)
            {
                progress.Status = wanted;
                if (announce && met)
                    W2CQuestUpdatePacketSender.Send(peer, ToClient(progress), $"{definition.Record.Name} - ready to hand in.", removed: false);
            }
        }
    }

    private static int CountInBag(PlayerSession session, int itemTemplateId)
    {
        int total = 0;
        foreach (var slot in session.Inventory.Slots)
            if (slot.ItemTemplateId == itemTemplateId)
                total += slot.Quantity;

        return total;
    }

    private static bool ObjectivesMet(QuestProgress progress, Definition definition)
    {
        for (int i = 0; i < definition.Objectives.Length; i++)
        {
            if (i >= progress.Counts.Length) return false;
            if (progress.Counts[i] < definition.Objectives[i].RequiredCount) return false;
        }

        return true;
    }

    private void CheckCompletion(NetPeer peer, PlayerSession session, QuestProgress progress, Definition definition)
    {
        if (progress.Status != QuestStatus.Active || !ObjectivesMet(progress, definition))
            return;

        progress.Status = QuestStatus.Complete;
        session.Quests.Dirty = true;

        W2CQuestUpdatePacketSender.Send(peer, ToClient(progress), $"{definition.Record.Name} - ready to hand in.", removed: false);
    }

    private void Announce(NetPeer peer, QuestProgress progress, Definition definition) =>
        W2CQuestUpdatePacketSender.Send(peer, ToClient(progress), null, removed: false);

    /// <summary>
    /// Takes the Collect items at hand-in. Counted again here because the
    /// stored progress could be stale - and if anything is short, nothing at
    /// all is taken.
    /// </summary>
    private bool TakeCollectItems(NetPeer peer, PlayerSession session, Definition definition)
    {
        var needed = new List<(int ItemId, int Count)>();

        foreach (var objective in definition.Objectives)
        {
            if (objective.ObjectiveType != (int)QuestObjectiveType.Collect) continue;
            if (CountInBag(session, objective.TargetId) < objective.RequiredCount) return false;

            needed.Add((objective.TargetId, objective.RequiredCount));
        }

        foreach (var (itemId, count) in needed)
        {
            int left = count;

            for (int slot = 0; slot < session.Inventory.Slots.Length && left > 0; slot++)
            {
                if (session.Inventory.Slots[slot].ItemTemplateId != itemId) continue;

                int take = Math.Min(left, session.Inventory.Slots[slot].Quantity);
                _players.TryTakeFromSlot(peer, slot, take, out _, out int removed);
                left -= removed;

                if (removed == 0) break; // nothing moved - stop rather than spin
            }
        }

        return true;
    }
}
