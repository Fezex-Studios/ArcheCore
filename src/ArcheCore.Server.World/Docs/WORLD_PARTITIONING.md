# World partitioning and streaming

One shard = one seamless world = one WorldServer process (Kyrios is one
WorldServer). This is how that world is cut up so it can be as big as an
ArcheAge continent without the client loading all of it, and without the
server losing track of where the ground is.

## Three grids, three jobs

| Grid | Size | Lives in | Decides |
|---|---|---|---|
| **World tiles** (`WorldGrid`) | 512m | shared `ArcheCore.Movement` | which *static world* (terrain, props) is loaded on the client, and which heightmaps the server has in memory |
| **Interest cells** (`SpatialGrid`) | 50m | server | which *dynamic entities* (players, NPCs, nodes) each client hears about, within 75/85m |
| **Snapshot LOD** (`SnapshotDispatcher`) | 30/85m | server | how often each visible entity's movement is sent |

They're independent on purpose: retuning view distance must never force
re-cutting the world into new scenes.

## Coordinates

- **World space**: the server, the database, every packet, every heightmap.
  Valid anywhere in the shard, forever.
- **Local space**: Unity transforms. World minus `WorldOrigin.Offset` (X/Z
  only, always whole tiles). Identical to world space until the floating
  origin first shifts (6144m from the origin by default).

**The rule:** every position arriving from the network goes through
`WorldOrigin.ToLocal` (inside the `RunWhenReady` lambda if the handler
queues), and every position going out comes from `WorldOrigin.ToWorld`.
Anything caching a local position outside a transform subscribes to
`WorldOrigin.Shifted`.

## Client

```
main_world.unity          PERSISTENT: camera, lighting, sky, registries, UI,
                          spawn/NPC markers. Players/NPCs spawn here.
World/Tiles/tile_x_z.unity one per 512m tile: terrain + static scenery at
                          real world coordinates. Streamed.
```

- `WorldStreamer` keeps the 3x3 tiles around the player loaded, unloads
  beyond 2 tiles (hysteresis), nearest first, 2 operations at a time.
  Created automatically by `WorldLoader` if main_world doesn't have one.
- `LocalCharacterMotor` holds the player still (no gravity) until
  `WorldStreamer.IsAreaReady`, so spawning/respawning onto a tile that's
  still loading can't drop you through the world.
- `FloatingOrigin` shifts the world by whole tiles when the player is
  more than 6144m from Unity's origin.
- A tile with no scene in Build Settings is just empty. With no tile
  scenes at all, everything behaves exactly as before.

**Authoring rule:** no single Terrain may be larger than one tile
(512m). That's what guarantees the 3x3 block always contains the ground
under the player, even for terrains that aren't grid-aligned.

## Server

- `WorldServerConfig.TerrainDirectory` (default `Data/terrain_data`):
  every `*.achtmap` in it is indexed at boot (headers only) into a
  `TiledHeightField`. Heights load when first needed and unload after
  `TerrainIdleUnloadMinutes` unused (or set `PreloadAllTerrain`).
- `MovementValidator` now actually gets the terrain (it never did before -
  `HeightmapTerrainPath` was configured but never loaded) and rejects
  positions more than 1.5m below the ground.
- NPCs walk on the terrain (`NpcGroundSnap`), except where the ground is
  more than 4m from their current height (bridges, upper floors).
- `PlayerManager.TeleportPlayer` is the one way to move a player
  server-side: prefetches terrain, tells the validator, and moves them
  through the interest grid. Respawn uses it.
- `W2CEnterWorldPacket.World` tells the client the shard name, tile size
  and interest radii. The client refuses to stream if tile sizes differ.

## Workflow (Dev Tools > 🧱 World Tiles)

1. **Create tiles** for the area you're building: aligned 512m terrains that
   auto-connect at the seams. Or **Split** an existing scene's scenery
   (Terrain or Static-flagged roots) into tile scenes.
2. Edit tiles by opening several of them additively alongside main_world.
3. **Add all tile scenes to Build Settings.**
4. **Export All Terrain** into the server's `Data/terrain_data` after every
   sculpting pass. Stale exports = "server thinks you're underground".
5. **Validate** before committing.

## Migrating the current world

Your current terrain (`Terrain_(-529.54, 0.00, 1898.03)`) is 1000x1000m
and completely flat in its export (every height is 0), so nothing sculpted
is lost by replacing it:

1. Rebuild `ArcheCore.Movement` and `ArcheCore.Network`, and copy both DLLs
   into `Assets/Plugins` (the client needs `WorldGrid`, `WorldSettingsData`).
2. In main_world, note the area the old terrain covers: tiles -2..0 on X,
   3..5 on Z. World Tiles > Create tiles, from (-2, 3) to (0, 5) with terrain.
3. Delete (or disable) the old 1000m terrain in main_world.
4. Add tile scenes to Build Settings; Export All Terrain; delete the old
   `Terrain_(-529.54...).achtmap` from `Data/terrain_data`.
5. Turn off Static Batching (Project Settings > Player) before relying on
   the floating origin. Unity 6's GPU Resident Drawer replaces it.
6. Play. The console shows `[WorldStreamer] Loading tile_-1_4` etc., and the
   world server logs the shard name and heightmap count at startup.

## What this does not do yet

- **Server-side collision for buildings/props.** Terrain only. Walls need
  a collision mesh export (the "mesh BVH later" in `ICollisionWorld`).
- **Multiple processes per shard.** One WorldServer holds the whole world.
  That's the right call until a single shard outgrows one machine.
- **Instances/dungeons.** They'll be separate scenes with no heightmap,
  entered via teleport. Covered in Phase 8.
- **Water per tile** on the server. `SampleWaterLevel` still returns "none".
