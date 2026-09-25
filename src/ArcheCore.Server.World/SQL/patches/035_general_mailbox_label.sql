-- The two mailboxes became one general mailbox. Rename the interaction to match.
-- (034 already inserted these rows on servers that have booted with it, and
-- INSERT OR IGNORE won't overwrite them.)
-- Safe to re-run.

UPDATE InteractableActions SET Label = 'Mailbox' WHERE ActionType = 8 AND Label = 'Mailboxes';
