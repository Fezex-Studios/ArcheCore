-- Same change as the UniqueCharacterNames migration, for databases updated by
-- hand instead of `dotnet ef database update`. Run AFTER add_save_seq.sql.

-- Rename duplicates: the oldest character keeps the name.
UPDATE characters c
JOIN characters d ON LOWER(c.name) = LOWER(d.name) AND c.character_id > d.character_id
SET c.name = CONCAT(LEFT(c.name, 40), '_', c.character_id);

ALTER TABLE characters MODIFY COLUMN name VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL;
CREATE UNIQUE INDEX IX_characters_name ON characters (name);

INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
VALUES ('20260925180000_UniqueCharacterNames', '9.0.0');
