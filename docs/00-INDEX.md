# ArcheCore Documentation

This is a from-the-code documentation set for ArcheCore, written by reading
the actual `ArcheCore.Network`, `ArcheCore.Server.World`,
`ArcheCore.Server.Persistence`, and Unity client (`ArcheCore.Client`)
projects — not a spec of what it should do, a description of what it
currently does, including the rough edges.

You already have `ArcheCore_Server_World/Docs/ADDING_PACKETSv2.md`, which is
the definitive, excellent guide to the C2W/W2C/W2P/P2W packet system. These
docs don't repeat it — they link to it — and instead cover everything
*around* it: the process topology, the three-and-a-half database systems,
the Lua event pipeline, the session/spawn/movement/interaction managers, the
end-to-end login flow, the Unity client's own architecture, and — in the
last doc — a punch list of real inconsistencies in the current code that
you should know about before building the shop system or anything else on
top of this foundation.

## Reading order

1. **[01-ARCHITECTURE.md](01-ARCHITECTURE.md)** — the four processes, how
   they talk to each other, tech stack per component, directory map. Start
   here if you're context-switching back into the project after time away.
2. **[02-NETWORKING-AND-PACKETS.md](02-NETWORKING-AND-PACKETS.md)** — wire
   formats for both transports (LiteNetLib and raw TCP), how the two opcode
   systems differ, and how they relate to `ADDING_PACKETSv2.md`.
3. **[03-DATABASE-AND-PERSISTENCE.md](03-DATABASE-AND-PERSISTENCE.md)** —
   the four SQLite databases in this project, what each one is for, and a
   real schema collision between two of them you need to resolve.
4. **[04-LUA-SCRIPTING.md](04-LUA-SCRIPTING.md)** — the Eluna-style
   register-once/fire-by-id scripting engine, the full `LuaPlayer` /
   `Server` API surface, and how to add a new hookable event.
5. **[05-GAMEPLAY-SYSTEMS.md](05-GAMEPLAY-SYSTEMS.md)** — session tracking,
   the spatial grid / interest management, player spawn & movement
   replication, and the interaction system NPCs use today.
6. **[06-AUTH-AND-SESSION-FLOW.md](06-AUTH-AND-SESSION-FLOW.md)** — the
   full login-to-spawned-in-world sequence across all four processes, with
   the actual packets exchanged at each step.
7. **[07-BUILDING-A-FEATURE-END-TO-END.md](07-BUILDING-A-FEATURE-END-TO-END.md)** —
   the doc you want if you're asking "okay, but how do I actually add
   something new that touches everything?" Walks a full shop/purchase
   feature client → world server → persistence server and back, both the
   fast (no-database) path and the slow (durable-write) path, file by file.
8. **[08-CLIENT-ARCHITECTURE.md](08-CLIENT-ARCHITECTURE.md)** — the Unity
   project layout, `ClientNetwork`, and the local encrypted `gamedata.db`
   pipeline.
9. **[09-KNOWN-GAPS-AND-NEXT-STEPS.md](09-KNOWN-GAPS-AND-NEXT-STEPS.md)** —
   concrete bugs/inconsistencies found while reading the code (not
   hypothetical ones), plus a suggested order of operations for fixing them
   before you build much more on top.

## `code/` — the EF migration cutover, implemented

Everything needed to actually retire the SQL patch runner in favor of EF
Core migrations, worked out in full rather than just described:

- **`MIGRATION-STEPS.md`** — exact before/after diffs for
  `ServerBootstrap.cs`/`WorldServer.cs`, the one-time step for reconciling
  existing dev databases, which files to delete, and a verification
  checklist.
- **`GameData/README.md`** + **`WorldDataDbContext.cs`** +
  **`GameData/Items/`, `Shop/`, `Quests/`** — a worked 15-table example
  (Items, Shop, Quests domains) showing the add-a-table pattern at scale,
  including which kinds of data belong in `worldserver.db` versus
  `persistence.db`.

Note: seeding row data (NPC templates, spawners, items, etc.) into these
tables is intentionally **not** covered by anything in `code/` — that's
left to whatever tooling you're already using for it. Migrations only
ever manage schema.

## Scope note

These docs describe: `ArcheCore.Network` (shared C# contracts),
`ArcheCore.Server.World` (the C# WorldServer), `ArcheCore.Server.Persistence`
(the Bun/TypeScript persistence server), and the Unity client project
(`Assets/ArcheCore.Client`). The separate TypeScript **AuthServer**
mentioned throughout (it issues the session tokens `AuthService.ValidateToken`
checks, and serves `gamedata.db` over HTTP) was not part of the uploaded
projects, so it's referenced here only by its two known HTTP contracts —
`POST /validate-session` and the `gamedata.db` download endpoint — not
documented in its own right.
