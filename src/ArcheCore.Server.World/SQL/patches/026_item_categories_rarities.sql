-- Categories, rarities, and tooltip data for the test items.
-- Needs the AddItemTooltipFields migration (Items.category_id / rarity_id /
-- required_level). Content only - safe to re-run.

INSERT OR IGNORE INTO ItemCategories (Id, Name) VALUES
    (1, 'Weapon'),
    (2, 'Armor'),
    (3, 'Consumable'),
    (4, 'Material'),
    (5, 'Accessory'),
    (6, 'Quest Item');

-- ColorHex colours the item's name in the tooltip.
INSERT OR IGNORE INTO ItemRarities (Id, Name, ColorHex) VALUES
    (1, 'Common',    '#D8D2C4'),
    (2, 'Uncommon',  '#6CCB5F'),
    (3, 'Rare',      '#4F9BE8'),
    (4, 'Epic',      '#B36BE8'),
    (5, 'Legendary', '#F0A134');

--                     category  rarity  level
UPDATE Items SET category_id = 1, rarity_id = 1, required_level = 1  WHERE item_id = 1;  -- longspear
UPDATE Items SET category_id = 1, rarity_id = 3, required_level = 10 WHERE item_id = 2;  -- nodachi
UPDATE Items SET category_id = 1, rarity_id = 2, required_level = 5  WHERE item_id = 3;  -- sword
UPDATE Items SET category_id = 3, rarity_id = 1, required_level = 0  WHERE item_id = 4;  -- Minor Health Potion
UPDATE Items SET category_id = 5, rarity_id = 4, required_level = 15 WHERE item_id = 5;  -- Lucky Trinket
UPDATE Items SET category_id = 3, rarity_id = 2, required_level = 10 WHERE item_id = 6;  -- Major Health Potion
UPDATE Items SET category_id = 4, rarity_id = 1, required_level = 0  WHERE item_id = 7;  -- Iron Ore
UPDATE Items SET category_id = 4, rarity_id = 1, required_level = 0  WHERE item_id = 8;  -- Silverleaf
UPDATE Items SET category_id = 4, rarity_id = 1, required_level = 0  WHERE item_id = 9;  -- Oak Log

-- Descriptions the tooltip shows (the originals were placeholders).
UPDATE Items SET description = 'A long ash-wood spear with an iron head. Keeps enemies at a distance.' WHERE item_id = 1;
UPDATE Items SET description = 'A great curved blade, taller than most who wield it.'                  WHERE item_id = 2;
UPDATE Items SET description = 'A dependable one-handed blade of plain steel.'                          WHERE item_id = 3;
