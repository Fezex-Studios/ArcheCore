# Zones

Zones are named regions of the world (Solzreed, Gweonid Forest) with rules
attached: display name, subtitle, level range, PvP mode. They are painted
onto the world, independent of tiles.

| | Tiles | Zones |
|---|---|---|
| What | 512m streaming squares | design regions, any shape |
| Stored as | one Unity scene each | one file: `zones.aczmap` |
| Resolution | 512m | 32m cells (16x16 per tile) |
| Used for | what the client loads | banner, PvP rules, Lua events, later music/weather |

A zone spans many tiles; a border tile holds parts of two zones. Moving a
border never touches a scene.

## The file

`zones.aczmap` holds the zone list AND the painted cells, so the map can
never reference a zone the list doesn't have. Two copies:

- **Client:** `Assets/StreamingAssets/World/zones.aczmap` (ships in the build)
- **Server:** `WorldServerConfig.ZoneMapPath`, default `Data/world/zones.aczmap`

Dev Tools > Zones > **Save** writes both. The server sends its copy's hash on
entering the world; a client with a different copy logs an error.

## Zone fields

| Field | Meaning |
|---|---|
| Id | 1-65535, assigned by the tool. 0 = "no zone". |
| Key | `solzreed`, `gweonid_forest_3` - stable, lowercase. Lua and folder names use it. |
| Display name | What the banner says. |
| Subtitle | Optional smaller line (continent, region). |
| PvP | Safe / Peaceful = no player attacks. Contested / War = open PvP. |
| Level range | Shown on the banner; later used for level-gating content. |

## What uses zones

- **Client:** `ZoneTracker` looks up the local player's zone 4x/second from
  its own copy (no network traffic) and shows `ZoneBannerUI` on entry -
  name tinted green (safe), white (peaceful), orange (contested), red (war).
- **Server:** `PlayerSession.ZoneId` is updated on every accepted move,
  teleport and login. Changes fire Lua `PlayerEvent.OnEnterZone`
  `(player, zoneId, zoneKey, previousZoneId)` - see
  `Lua/Server/on_enter_zone_example.lua`. The same event reaches C# listeners
  through `LuaEngine.EventFired`, for "enter zone" quest objectives.
- **Combat:** player attacks are refused if either player is in a Safe or
  Peaceful zone. Unzoned ground keeps the old rules
  (`AllowPlayerVersusPlayer` + spawn point safe radii).

## Workflow (Dev Tools > 🧭 Zones)

1. **Add zone** for each region; fill in key, name, PvP, levels, colour.
2. **Paint** on the map panel: select a zone, left-drag. Ctrl+click fills a
   whole 512m tile. Erase and Pick tools; scroll zooms, right-drag pans.
   Optional background image (your world map art) aligned to world coords.
3. Or turn on **Paint in Scene view** to paint directly on the terrain.
4. **Save** (client + server). Rebuild/restart the server.
5. **Open tiles** / **Only these** next to a zone opens every tile scene it
   touches with main_world - editing "Solzreed" in one click.
6. **Organize tile scenes into zone folders** moves each tile scene into
   `Tiles/<zone key>/` by which zone covers most of it (`_unzoned` if none).
   Build Settings are updated. Folders are for people; the game loads tiles
   by name.
7. **Validate** before committing.
