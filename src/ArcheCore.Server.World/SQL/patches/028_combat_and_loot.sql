-- Roadmap G/H/I test data: one melee skill, attackable orcs, a loot table.
-- Needs the AddCombatAndLoot migration (Skills, LootTables, LootTableEntries,
-- and NpcTemplates.MaxHealth / LootTableId / RespawnSeconds). Safe to re-run.

-- ── The one skill ──
-- Range 3.5 (a bit over melee reach), 1.5s cooldown, 8-14 damage:
-- an orc with 60 HP takes about 5-6 swings.
INSERT OR IGNORE INTO Skills (Id, Name, Range, CooldownMs, MinDamage, MaxDamage) VALUES
    (1, 'Strike', 3.5, 1500, 8, 14);

-- ── What orcs drop ──
INSERT OR IGNORE INTO LootTables (Id, Name, MinGold, MaxGold) VALUES
    (1, 'Orc Grunt', 5, 15);

-- Chance is 0-1, each rolled on every kill.
INSERT OR IGNORE INTO LootTableEntries (Id, LootTableId, ItemId, Chance, MinQuantity, MaxQuantity) VALUES
    (1, 1, 7, 0.60, 1, 2),   -- Iron Ore, 60%
    (2, 1, 4, 0.25, 1, 1),   -- Minor Health Potion, 25%
    (3, 1, 1, 0.05, 1, 1);   -- longspear, 5%

-- ── Make orcs attackable ──
-- MaxHealth 0 = can't be attacked, which is what every NPC got from the
-- migration - Hilda (template 10) stays that way on purpose.
UPDATE NpcTemplates SET MaxHealth = 60, LootTableId = 1, RespawnSeconds = 30 WHERE Id = 1;   -- Orc Grunt
