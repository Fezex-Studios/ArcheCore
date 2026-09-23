-- Respawn points and PvP safe zones.
-- Needs the AddSpawnPointOptions migration (SpawnPoints.IsRespawnPoint,
-- SafeRadius). Safe to re-run.
--
-- Dead players come back at the NEAREST row with IsRespawnPoint = 1. With
-- none marked, everyone returns to the default spawn, exactly as before.
--
-- SafeRadius stops player-vs-player fighting within that many units of the
-- point (both attacker and victim are checked). It does nothing to NPCs -
-- orcs still fight wherever they are.

-- The starting meadow: where you log in, where you respawn, and a 40-unit
-- no-PvP bubble so new characters can't be camped at the spawn.
UPDATE SpawnPointTables SET IsRespawnPoint = 1, SafeRadius = 40.0 WHERE IsDefault = 1;

-- A second graveyard out by the orc camp, so dying there doesn't mean a
-- long walk back. Move it to suit your world.
INSERT OR IGNORE INTO SpawnPointTables (Id, Name, X, Y, Z, IsDefault, IsRespawnPoint, SafeRadius) VALUES
    (90, 'Orc Camp Graveyard', -300.0, 0.0, 2560.0, 0, 1, 0.0);
