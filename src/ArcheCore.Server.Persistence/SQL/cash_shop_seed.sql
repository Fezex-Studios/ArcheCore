-- Cash shop catalogue and some test credits. Run against the PERSISTENCE
-- database (MySQL) after `dotnet ef database update`.
--
-- ItemTemplateId is Items.item_id on the world server - what the buyer
-- actually receives, delivered to their mailbox.

-- is_giftable: 1 = players can buy it FOR another character (a Gift button
-- appears), 0 = only for themselves. A new row that leaves it out is 0.
INSERT IGNORE INTO cash_shop_items
    (id, display_name, category, item_template_id, quantity, price_credits, sort_order, is_enabled, is_giftable) VALUES
    (1, 'Field Horse',            'Mounts',      11, 1, 1200, 10, 1, 1),
    (2, 'Camp Dog',               'Companions',  12, 1,  800, 20, 1, 1),
    (3, 'Major Health Potion x5', 'Consumables',  6, 5,  150, 30, 1, 1),
    (4, 'Lucky Trinket',          'Trinkets',     5, 1,  400, 40, 1, 0);

-- INSERT IGNORE skips rows that already exist, so if you seeded before gifting
-- existed, set the flag on them here. Flip any item at any time, no restart:
--   UPDATE cash_shop_items SET is_giftable = 1 WHERE id = 4;   -- allow
--   UPDATE cash_shop_items SET is_giftable = 0 WHERE id = 1;   -- forbid
UPDATE cash_shop_items SET is_giftable = 1 WHERE id IN (1, 2, 3);
UPDATE cash_shop_items SET is_giftable = 0 WHERE id = 4;

-- Give an account some credits to test with. Replace 1 with your account id.
INSERT INTO account_credits (account_id, balance, updated_at_ticks)
VALUES (1, 5000, 0)
ON DUPLICATE KEY UPDATE balance = 5000;
