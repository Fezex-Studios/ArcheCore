-- F/G actions for every interactable, plus data the new tooltips show.
-- Needs the AddInteractionActions migration (InteractableActions table,
-- NpcTemplates.Title / Greeting). Safe to re-run.
--
-- TargetKind: 1 = NPC, 2 = corpse, 4 = harvest node
-- TemplateId: that kind's template id, 0 = default for every object of that kind
-- Slot:       0 = F, 1 = G
-- ActionType: 1 Talk, 2 Trade, 3 Harvest, 4 LootAll, 5 OpenLoot, 6 Climb
-- IconName:   client Resources/Icons/Actions/<name>.png
-- CursorName: client Resources/Cursors/<name>.png  (interact, loot, talk; enemies use "attack")

INSERT OR IGNORE INTO InteractableActions (Id, TargetKind, TemplateId, Slot, ActionType, Label, IconName, CursorName, IsEnabled) VALUES
    -- Harvest nodes: same Harvest action, different words
    (1, 4, 1, 0, 3, 'Mine',           'action_mine',     'interact', 1),   -- Iron Vein
    (2, 4, 2, 0, 3, 'Gather',         'action_gather',   'interact', 1),   -- Silverleaf Bush
    (3, 4, 3, 0, 3, 'Chop',           'action_chop',     'interact', 1),   -- Oak Tree: chop only
    (4, 4, 4, 0, 3, 'Chop',           'action_chop',     'interact', 1),   -- Larch Tree: chop...
    (5, 4, 4, 1, 6, 'Climb',          'action_climb',    'interact', 0),   -- ...and climb (greyed out until built)

    -- Every corpse (TemplateId 0 = all of them)
    (6, 2, 0, 0, 4, 'Take all items', 'action_loot_all', 'loot',     1),
    (7, 2, 0, 1, 5, 'Obtain loot',    'action_loot',     'loot',     1),

    -- Hilda (NPC template 10)
    (8, 1, 10, 0, 1, 'Talk',          'action_talk',     'talk',     1),
    (9, 1, 10, 1, 2, 'Trade',         'action_trade',    'talk',     1);

-- Hilda's title (above her name) and what Talk makes her say.
UPDATE NpcTemplates SET Title = 'Merchant', Greeting = 'Welcome, traveler! Have a look at my wares.' WHERE Id = 10;

-- ── Larch Tree: the chop + climb example ──
-- Reuses the OakTree model and the oak-log icon so it works with no new art.
INSERT OR IGNORE INTO Items (item_id, name, description, icon_name, category_id, rarity_id, required_level) VALUES
    (10, 'Larch Log', 'A straight, resinous log.', 'wood_oak', 4, 1, 0);

INSERT OR IGNORE INTO HarvestNodeTemplates
    (Id, Name, ModelType, ItemId, MinQuantity, MaxQuantity, HarvestTimeMs, RespawnSeconds, InteractRange) VALUES
    (4, 'Larch Tree', 'OakTree', 10, 1, 2, 4000, 45, 5.0);

INSERT OR IGNORE INTO HarvestNodeSpawns (Id, TemplateId, X, Y, Z, Yaw) VALUES
    (6, 4, -386.0, 0.0, 2538.0, 120.0);
