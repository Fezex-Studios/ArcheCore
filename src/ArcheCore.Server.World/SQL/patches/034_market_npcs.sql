-- The market NPCs: an auctioneer and a cash shop clerk.
-- No migration needed - these are rows in tables that already exist.
-- (Listings live in the auction service's own MySQL database; the cash shop
-- catalogue lives in the persistence database. See the README for seeding.)
-- Safe to re-run.

INSERT OR IGNORE INTO NpcTemplates
    (Id, Name, Level, ModelType, InteractRange, IsStationary, MaxHealth, LootTableId, RespawnSeconds, Title, Greeting,
     AggroRadius, AttackRange, AttackCooldownMs, AttackDamageMin, AttackDamageMax) VALUES
    (13, 'Auctioneer Marlow', 20, 'NPC_ORC', 4.0, 1, 0, 0, 0, 'Auctioneer',
     'Buying or selling? Either way, I take my cut.', 0, 2.5, 2000, 0, 0),
    (14, 'Quartermistress Vell', 20, 'NPC_ORC', 4.0, 1, 0, 0, 0, 'Cash Shop',
     'Only the finest, and only for coin of the realm above.', 0, 2.5, 2000, 0, 0);

INSERT OR IGNORE INTO NpcSpawners (Id, TemplateId, X, Y, Z, Count, Radius) VALUES
    (13, 13, -346.0, 0.0, 2526.0, 1, 0.0),
    (14, 14, -340.0, 0.0, 2520.0, 1, 0.0);

-- ActionType: 8 Mailbox, 9 Auction, 10 CashShop.
INSERT OR IGNORE INTO InteractableActions (Id, TargetKind, TemplateId, Slot, ActionType, Label, IconName, CursorName, IsEnabled) VALUES
    (12, 1, 13, 0, 9,  'Auction House', 'action_trade', 'talk', 1),
    (13, 1, 13, 1, 8,  'Mailbox',      'action_loot',  'talk', 1),
    (14, 1, 14, 0, 10, 'Cash Shop',     'action_trade', 'talk', 1),
    (15, 1, 14, 1, 8,  'Mailbox',      'action_loot',  'talk', 1);
