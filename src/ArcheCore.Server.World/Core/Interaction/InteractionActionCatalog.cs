using System;
using System.Collections.Generic;
using System.Linq;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.GameData.Interaction;
using ArcheCore.Server.World.Utils.Database.SQLite;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace ArcheCore.Server.World.Core.Interaction
{
    /// <summary>
    /// Every interactable's F/G actions, loaded once at boot from the
    /// InteractableActions table. See GameData/Interaction/InteractableAction.
    ///
    /// Lookup order for (kind, template): that template's own rows, else the
    /// kind's TemplateId-0 defaults, else nothing.
    ///
    /// Used in two places, which is the point: spawn packets send the list to
    /// the client (so it can draw the F/G prompts), and C2WInteractHandler
    /// checks every request against the SAME list (so a client can't ask a
    /// rock to trade, or climb a tree that only offers Chop).
    ///
    /// </summary>
    public class InteractionActionCatalog
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static readonly InteractionActionData[] None = Array.Empty<InteractionActionData>();

        private readonly IDbContextFactory<WorldDataDbContext> _dbFactory;
        private Dictionary<(int Kind, int Template), InteractionActionData[]> _byTarget = new();

        public InteractionActionCatalog(IDbContextFactory<WorldDataDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public void LoadFromDatabase()
        {
            using var db = _dbFactory.CreateDbContext();

            var valid = new List<InteractableAction>();
            foreach (var row in db.InteractableActions.ToList())
            {
                string problem =
                    !Enum.IsDefined(typeof(InteractableKind), row.TargetKind)           ? $"TargetKind {row.TargetKind} is not a known kind" :
                    !Enum.IsDefined(typeof(InteractionActionType), row.ActionType) ||
                    row.ActionType == (int)InteractionActionType.None                   ? $"ActionType {row.ActionType} is not a known action" :
                    row.Slot < 0 || row.Slot > 1                                        ? $"Slot {row.Slot} must be 0 (F) or 1 (G)" :
                    string.IsNullOrWhiteSpace(row.Label)                                ? "Label is empty" :
                    null;

                if (problem != null)
                {
                    Logger.Warn("[Actions] Row {Id} skipped: {Problem}", row.Id, problem);
                    continue;
                }

                valid.Add(row);
            }

            _byTarget = new Dictionary<(int, int), InteractionActionData[]>();
            foreach (var group in valid.GroupBy(r => (r.TargetKind, r.TemplateId)))
            {
                var inSlotOrder = group.OrderBy(r => r.Slot).ThenBy(r => r.Id).ToList();

                if (inSlotOrder.Select(r => r.Slot).Distinct().Count() != inSlotOrder.Count)
                    Logger.Warn("[Actions] Kind {Kind} template {Template} has two actions on one key - keeping the first per slot",
                        group.Key.TargetKind, group.Key.TemplateId);

                _byTarget[group.Key] = inSlotOrder
                    .GroupBy(r => r.Slot).Select(g => g.First())
                    .Select(r => new InteractionActionData
                    {
                        ActionType = r.ActionType,
                        Label      = r.Label,
                        IconName   = r.IconName ?? string.Empty,
                        CursorName = string.IsNullOrWhiteSpace(r.CursorName) ? "interact" : r.CursorName,
                        IsEnabled  = r.IsEnabled
                    })
                    .ToArray();
            }

            Logger.Info("[Actions] Loaded {Rows} action(s) for {Targets} target(s).", valid.Count, _byTarget.Count);
        }

        /// <summary>The object's actions in key order (0 = F, 1 = G). Never null.</summary>
        public InteractionActionData[] For(InteractableKind kind, int templateId)
        {
            if (_byTarget.TryGetValue(((int)kind, templateId), out var own)) return own;
            if (_byTarget.TryGetValue(((int)kind, 0), out var defaults)) return defaults;
            return None;
        }

        /// <summary>Does this object offer this action? (Enabled or not - the caller decides what "disabled" says.)</summary>
        public bool TryGet(InteractableKind kind, int templateId, int actionType, out InteractionActionData action)
        {
            foreach (var a in For(kind, templateId))
            {
                if (a.ActionType == actionType) { action = a; return true; }
            }
            action = null;
            return false;
        }

        /// <summary>The F action - what an action-less (older) client's interact means. 0 if the object has none.</summary>
        public int DefaultActionType(InteractableKind kind, int templateId)
        {
            var list = For(kind, templateId);
            return list.Length > 0 ? list[0].ActionType : 0;
        }
    }
}
