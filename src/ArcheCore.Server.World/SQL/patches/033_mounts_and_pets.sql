-- A mount and a pet, both summoned by using an item.
-- Needs the AddMountsAndPets migration (Mounts table). Safe to re-run.

-- ── The mount ──
INSERT OR IGNORE INTO Mounts (Id, Name, ModelType, SpeedMultiplier) VALUES
    (1, 'Field Horse', 'Mount_Horse', 1.8);

-- The item that summons it. ItemUse EffectType 4 = Mount, EffectValue = Mounts.Id.
-- Not consumed (you keep the whistle), 2s cooldown so it can't be spammed.
INSERT OR IGNORE INTO Items (item_id, name, description, icon_name, category_id, rarity_id, required_level) VALUES
    (11, 'Horse Whistle', 'Calls a field horse. Use again to dismount.', 'trinket_lucky', 5, 2, 0);

INSERT OR IGNORE INTO ItemUses (Id, ItemId, ConsumeOnUse, EffectType, EffectValue, CooldownMs, CooldownGroup) VALUES
    (4, 11, 0, 4, 1, 2000, 0);

-- ── The pet ──
-- An ordinary NPC template with MaxHealth 0, so it can't be attacked, and
-- IsStationary 0 (it follows rather than stands).
INSERT OR IGNORE INTO NpcTemplates
    (Id, Name, Level, ModelType, InteractRange, IsStationary, MaxHealth, LootTableId, RespawnSeconds, Title, Greeting,
     AggroRadius, AttackRange, AttackCooldownMs, AttackDamageMin, AttackDamageMax) VALUES
    (12, 'Camp Dog', 1, 'NPC_ORC', 3.0, 0, 0, 0, 0, 'Companion', '', 0, 2.5, 2000, 0, 0);

-- ItemUse EffectType 5 = SummonPet, EffectValue = NpcTemplates.Id.
INSERT OR IGNORE INTO Items (item_id, name, description, icon_name, category_id, rarity_id, required_level) VALUES
    (12, 'Dog Whistle', 'Calls a camp dog to follow you. Use again to send it home.', 'trinket_lucky', 5, 2, 0);

INSERT OR IGNORE INTO ItemUses (Id, ItemId, ConsumeOnUse, EffectType, EffectValue, CooldownMs, CooldownGroup) VALUES
    (5, 12, 0, 5, 12, 2000, 0);

-- Hilda sells both, so there's a way to get them in game.
INSERT OR IGNORE INTO ShopItems (Id, ShopId, ItemId, BuyPrice, SellPrice, SortOrder) VALUES
    (7, 1, 11, 250, 50, 5),
    (8, 1, 12, 150, 30, 6);
