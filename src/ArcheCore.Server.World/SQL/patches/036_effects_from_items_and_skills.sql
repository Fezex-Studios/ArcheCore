-- Effects table (roadmap fix-first #1): copy what items and skills do into
-- it, one row each. Needs the AddEffects migration.
--
-- After this, an item's or skill's Effects rows are the truth:
--   editing ItemUses.EffectType/EffectValue or Skills.MinDamage/MaxDamage
--   no longer changes anything for an owner that has rows. Edit Effects.
-- ItemUses still decides consume-on-use and the cooldown - only "what
-- happens" moved.
--
-- OwnerType: 1 = item (OwnerId = Items.item_id), 2 = skill (OwnerId = Skills.Id)
-- Kind:      0 Script, 1 Heal, 2 ApplyStatus, 3 CastSkill, 4 Mount, 5 SummonPet, 6 Damage
--            (0-5 are the ItemUses.EffectType numbers, so they copy across)
-- Target:    0 Self, 1 Target
--
-- Safe to re-run: owners that already have rows are skipped.

-- ── Items ──
-- Heal: Min = Max = the old EffectValue. Everything else: RefId = EffectValue.
-- One row per item: if an item has two ItemUses rows, the server has always
-- used the first (lowest Id), so that's the one converted.
INSERT INTO Effects (OwnerType, OwnerId, Sort, Kind, Target, Min, Max, RefId, DurationMs, Script)
SELECT 1, u.ItemId, 0, u.EffectType, 0,
       CASE WHEN u.EffectType = 1 THEN u.EffectValue ELSE 0 END,
       CASE WHEN u.EffectType = 1 THEN u.EffectValue ELSE 0 END,
       CASE WHEN u.EffectType IN (2, 3, 4, 5) THEN u.EffectValue ELSE 0 END,
       0, NULL
FROM ItemUses u
WHERE u.EffectType BETWEEN 0 AND 5
  AND NOT (u.EffectType = 1 AND u.EffectValue <= 0)
  AND u.Id = (SELECT MIN(u2.Id) FROM ItemUses u2 WHERE u2.ItemId = u.ItemId)
  AND EXISTS (SELECT 1 FROM Items i WHERE i.item_id = u.ItemId)
  AND NOT EXISTS (SELECT 1 FROM Effects e WHERE e.OwnerType = 1 AND e.OwnerId = u.ItemId);

-- ── Skills ──
-- Damage to the target, MinDamage..MaxDamage.
INSERT INTO Effects (OwnerType, OwnerId, Sort, Kind, Target, Min, Max, RefId, DurationMs, Script)
SELECT 2, s.Id, 0, 6, 1, s.MinDamage, s.MaxDamage, 0, 0, NULL
FROM Skills s
WHERE s.MinDamage >= 0
  AND s.MaxDamage >= s.MinDamage
  AND s.MaxDamage > 0
  AND NOT EXISTS (SELECT 1 FROM Effects e WHERE e.OwnerType = 2 AND e.OwnerId = s.Id);
