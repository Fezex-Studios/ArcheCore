-- Same change as the AddCharacterSaveSeq migration, for databases that are
-- updated by hand instead of with `dotnet ef database update`.
ALTER TABLE characters ADD COLUMN save_seq BIGINT NOT NULL DEFAULT 0;

INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
VALUES ('20260925120000_AddCharacterSaveSeq', '9.0.0');
