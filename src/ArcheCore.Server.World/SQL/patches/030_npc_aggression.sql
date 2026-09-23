-- Roadmap J: make orcs fight back.
-- Needs the AddNpcAggression migration (NpcTemplates.AggroRadius,
-- AttackRange, AttackCooldownMs, AttackDamageMin/Max). Safe to re-run.
--
-- AggroRadius 0 = never attacks, which is what the migration gives every
-- NPC. Hilda (10) stays peaceful because her row isn't touched.
--
-- An Orc Grunt: notices you at 10m, hits for 4-8 every 2s from 2.5m.
-- A level-1 player has 100 HP, so that's ~20 swings to kill you, while you
-- need ~5 to kill it (8-14 damage vs its 60 HP). Fair odds one-on-one, and
-- losing takes long enough to run away from.
UPDATE NpcTemplates
SET AggroRadius = 10.0, AttackRange = 2.5, AttackCooldownMs = 2000,
    AttackDamageMin = 4, AttackDamageMax = 8
WHERE Id = 1;
