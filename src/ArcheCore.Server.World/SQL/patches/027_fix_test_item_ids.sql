-- Forces test items 1-9 to their correct names/icons/descriptions.
--
-- Why: an earlier fix inserted items with different ids (4 = Major Health
-- Potion). The corrected SQL used INSERT OR IGNORE, which never overwrites
-- an existing row, so those old names stayed. This UPDATEs every one
-- explicitly, then inserts any that are missing. Safe to re-run.

INSERT OR IGNORE INTO Items (item_id, name, description, icon_name) VALUES
    (1,'longspear','',''),(2,'nodachi','',''),(3,'sword','',''),
    (4,'Minor Health Potion','',''),(5,'Lucky Trinket','',''),(6,'Major Health Potion','',''),
    (7,'Iron Ore','',''),(8,'Silverleaf','',''),(9,'Oak Log','','');

UPDATE Items SET name = 'longspear',           icon_name = 'als_si',          description = 'A long ash-wood spear with an iron head. Keeps enemies at a distance.' WHERE item_id = 1;
UPDATE Items SET name = 'nodachi',             icon_name = 'Nodachi_ico',     description = 'A great curved blade, taller than most who wield it.'                  WHERE item_id = 2;
UPDATE Items SET name = 'sword',               icon_name = 'sword_icon',      description = 'A dependable one-handed blade of plain steel.'                          WHERE item_id = 3;
UPDATE Items SET name = 'Minor Health Potion', icon_name = 'potion_minor',    description = 'Restores 50 health.'                                                   WHERE item_id = 4;
UPDATE Items SET name = 'Lucky Trinket',       icon_name = 'trinket_lucky',   description = 'Grants a short blessing. Reusable.'                                     WHERE item_id = 5;
UPDATE Items SET name = 'Major Health Potion', icon_name = 'potion_major',    description = 'Restores 150 health.'                                                  WHERE item_id = 6;
UPDATE Items SET name = 'Iron Ore',            icon_name = 'ore_iron',        description = 'A lump of raw iron.'                                                   WHERE item_id = 7;
UPDATE Items SET name = 'Silverleaf',          icon_name = 'herb_silverleaf', description = 'A common herb with silvery leaves.'                                     WHERE item_id = 8;
UPDATE Items SET name = 'Oak Log',             icon_name = 'wood_oak',        description = 'A sturdy length of oak.'                                               WHERE item_id = 9;

-- The item-use rows, restated for the same reason (4/6 consumed, 5 reusable).
DELETE FROM ItemUses WHERE Id IN (1, 2, 3);
INSERT INTO ItemUses (Id, ItemId, ConsumeOnUse, EffectType, EffectValue, CooldownMs, CooldownGroup) VALUES
    (1, 4, 1, 1,  50, 10000, 1),
    (2, 5, 0, 2,   7, 20000, 0),
    (3, 6, 1, 1, 150, 10000, 1);
