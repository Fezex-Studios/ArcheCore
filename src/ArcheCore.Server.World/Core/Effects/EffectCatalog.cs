using System;
using System.Collections.Generic;
using System.Linq;
using ArcheCore.Server.World.GameData.Combat;
using ArcheCore.Server.World.GameData.Effects;
using ArcheCore.Server.World.GameData.Items;
using ArcheCore.Server.World.Utils.Database.SQLite;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace ArcheCore.Server.World.Core.Effects
{
    /// <summary>
    /// Every item's and skill's effect list, loaded once from the Effects
    /// table. Lookups are dictionary hits on the tick thread, and the lists
    /// are shared (Effect is immutable).
    ///
    /// An item or skill with no Effects rows falls back to its old columns -
    /// ItemUses.EffectType/EffectValue, Skills.MinDamage/MaxDamage - so data
    /// written before the Effects table still works. SQL patch 036 converts
    /// the shipped rows; the fallback is for anything added by hand since.
    /// </summary>
    public sealed class EffectCatalog
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IDbContextFactory<WorldDataDbContext> _dbFactory;

        private Dictionary<int, Effect[]> _itemRows = new();
        private Dictionary<int, Effect[]> _skillRows = new();

        // Fallbacks built on first use and kept - tick thread only, so no lock.
        private readonly Dictionary<int, Effect[]> _itemFallback = new();
        private readonly Dictionary<int, Effect[]> _skillFallback = new();
        private readonly Dictionary<(int, int), Effect[]> _npcSwings = new();

        public EffectCatalog(IDbContextFactory<WorldDataDbContext> dbFactory) => _dbFactory = dbFactory;

        /// <summary>For tests: no database, fallbacks only (plus whatever Add puts in).</summary>
        public EffectCatalog() { }

        public void LoadFromDatabase()
        {
            using var db = _dbFactory.CreateDbContext();
            Load(db.Effects.AsNoTracking().ToList());
        }

        /// <summary>Validate and index rows. Public so tests can feed rows without a database.</summary>
        public void Load(IEnumerable<EffectRow> rows)
        {
            var items = new Dictionary<int, List<EffectRow>>();
            var skills = new Dictionary<int, List<EffectRow>>();
            int skipped = 0;

            foreach (var row in rows)
            {
                string problem = Validate(row);
                if (problem != null)
                {
                    Logger.Warn("[Effects] Effects row {Id} ({Owner} {OwnerId}) skipped: {Problem}", row.Id, row.OwnerType, row.OwnerId, problem);
                    skipped++;
                    continue;
                }

                var byOwner = row.OwnerType == EffectOwner.Item ? items : skills;
                if (!byOwner.TryGetValue(row.OwnerId, out var list))
                    byOwner[row.OwnerId] = list = new List<EffectRow>();
                list.Add(row);
            }

            _itemRows  = items.ToDictionary(kv => kv.Key, kv => ToEffects(kv.Value));
            _skillRows = skills.ToDictionary(kv => kv.Key, kv => ToEffects(kv.Value));
            _itemFallback.Clear();
            _skillFallback.Clear();

            Logger.Info("[Effects] Loaded effects for {Items} item(s) and {Skills} skill(s){Skipped}.",
                _itemRows.Count, _skillRows.Count, skipped > 0 ? $", {skipped} row(s) skipped" : "");
        }

        private static string Validate(EffectRow row) =>
            row.OwnerType is not (EffectOwner.Item or EffectOwner.Skill) ? $"unknown OwnerType {(int)row.OwnerType}" :
            !Enum.IsDefined(row.Kind)                                    ? $"unknown Kind {(int)row.Kind}" :
            !Enum.IsDefined(row.Target)                                  ? $"unknown Target {(int)row.Target}" :
            row.Min < 0                                                  ? "Min is negative" :
            row.Max < row.Min                                            ? "Max is below Min" :
            row.DurationMs < 0                                           ? "DurationMs is negative" :
            (row.Kind is EffectKind.Heal or EffectKind.Damage) && row.Max == 0 ? $"{row.Kind} of 0" :
            null;

        private static Effect[] ToEffects(List<EffectRow> rows) =>
            rows.OrderBy(r => r.Sort).ThenBy(r => r.Id)
                .Select(r => new Effect(r.Kind, r.Target, r.Min, r.Max, r.RefId, r.DurationMs,
                                        string.IsNullOrWhiteSpace(r.Script) ? null : r.Script))
                .ToArray();

        // ── Lookups ──────────────────────────────────────────────────

        /// <summary>
        /// What using this item does. Null = the item's data is broken
        /// (an EffectType this server doesn't know) - the use is refused.
        /// </summary>
        public Effect[] ForItem(ItemUse use)
        {
            if (_itemRows.TryGetValue(use.ItemId, out var rows))
                return rows;

            if (_itemFallback.TryGetValue(use.ItemId, out var cached))
                return cached;

            Effect legacy = use.EffectType switch
            {
                ItemEffectType.ScriptOnly => new Effect(EffectKind.Script, EffectTarget.Self),
                ItemEffectType.Heal       => Effect.Heal(Math.Max(0, use.EffectValue), Math.Max(0, use.EffectValue)),
                ItemEffectType.ApplyBuff  => new Effect(EffectKind.ApplyStatus, EffectTarget.Self, RefId: use.EffectValue),
                ItemEffectType.CastSkill  => new Effect(EffectKind.CastSkill, EffectTarget.Self, RefId: use.EffectValue),
                ItemEffectType.Mount      => new Effect(EffectKind.Mount, EffectTarget.Self, RefId: use.EffectValue),
                ItemEffectType.SummonPet  => new Effect(EffectKind.SummonPet, EffectTarget.Self, RefId: use.EffectValue),
                _ => null
            };

            if (legacy == null)
            {
                Logger.Warn("[Effects] Item {ItemId} has unknown EffectType {Type} and no Effects rows - it can't be used",
                    use.ItemId, (int)use.EffectType);
                return null;
            }

            return _itemFallback[use.ItemId] = new[] { legacy };
        }

        /// <summary>What a hit with this skill does. Never null.</summary>
        public Effect[] ForSkill(SkillTemplate skill)
        {
            if (_skillRows.TryGetValue(skill.Id, out var rows))
                return rows;

            if (_skillFallback.TryGetValue(skill.Id, out var cached))
                return cached;

            return _skillFallback[skill.Id] = new[] { Effect.Damage(skill.MinDamage, skill.MaxDamage) };
        }

        /// <summary>An NPC's auto-attack: damage from its template. Shared per damage range.</summary>
        public Effect[] ForNpcSwing(int minDamage, int maxDamage)
        {
            var key = (minDamage, maxDamage);
            if (!_npcSwings.TryGetValue(key, out var swing))
                _npcSwings[key] = swing = new[] { Effect.Damage(minDamage, Math.Max(minDamage, maxDamage)) };
            return swing;
        }

        /// <summary>Does this skill/item have its own Effects rows (rather than the fallback)?</summary>
        public bool HasRows(EffectOwner owner, int ownerId) =>
            owner == EffectOwner.Item ? _itemRows.ContainsKey(ownerId) : _skillRows.ContainsKey(ownerId);
    }
}
