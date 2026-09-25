-- Handing out free items and gold by MAIL. Run against the PERSISTENCE
-- database (MySQL). Nothing here needs the game running: players find it in
-- their mailbox the next time they open it, online or off.
--
-- A piece of mail can carry gold, one item stack, or both, plus a subject.
-- Claiming it deletes the row, so a gift can only be taken once. Anything that
-- doesn't fit in a bag stays in the mailbox until there's room.
--
--   sender            who it says it's from
--   subject           the bold line in the mailbox
--   gold              0 for none
--   item_template_id  Items.item_id on the world server; 0 for no item
--   item_quantity     how many (ignored when item_template_id is 0)
--   created_at_ticks  .NET ticks. The expression below is "now".

-- Set this to Iron Ore's item id (Items.item_id on the world server). Left at 0
-- the mail is sent with no item in it, so change it before running.
SET @ore = 0;

-- .NET ticks for right now: unix seconds -> 100ns ticks, offset from 0001-01-01.
SET @now_ticks = CAST(UNIX_TIMESTAMP() AS UNSIGNED) * 10000000 + 621355968000000000;

-- ── 1. One character, by id: 500 Iron Ore ────────────────────────────
INSERT INTO mail (character_id, sender, subject, gold, item_template_id, item_quantity, created_at_ticks)
VALUES (1, 'Game Master', 'A gift from the team', 0, @ore, 500, @now_ticks);

-- ── 2. One character, by name: 250 gold ──────────────────────────────
INSERT INTO mail (character_id, sender, subject, gold, item_template_id, item_quantity, created_at_ticks)
SELECT character_id, 'Game Master', 'Sorry about the downtime', 250, 0, 0, @now_ticks
FROM characters
WHERE name = 'ReplaceWithCharacterName';

-- ── 3. EVERY character: an event reward (gold and an item together) ──
INSERT INTO mail (character_id, sender, subject, gold, item_template_id, item_quantity, created_at_ticks)
SELECT character_id, 'Game Master', 'Launch week reward', 100, @ore, 50, @now_ticks
FROM characters;

-- ── Housekeeping ─────────────────────────────────────────────────────
-- See what's waiting for someone:
--   SELECT * FROM mail WHERE character_id = 1 ORDER BY created_at_ticks;
--
-- Take a mistaken gift back (only works if they haven't claimed it yet):
--   DELETE FROM mail WHERE sender = 'Game Master' AND subject = 'A gift from the team';
--
-- mail_receipts records which auction deliveries have been posted, so a
-- retry can't deliver twice. Rows older than a few days serve no purpose:
--   DELETE FROM mail_receipts
--   WHERE created_at_ticks < (CAST(UNIX_TIMESTAMP() AS UNSIGNED) - 7 * 86400) * 10000000 + 621355968000000000;
