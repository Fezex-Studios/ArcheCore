# Hardening pass: audit High + Medium items (2026-09-25)

What changed for audit items H3–H5, M1–M5, M7 and M8, and what to know about each. C1–C3 are covered in `SAVES.md`. H1, H2 and M6 were fixed earlier.

## Deploy checklist

1. **Rebuild the solution and copy `ArcheCore.Network.dll` into `Assets/Plugins`.** This pass adds a new opcode, packet channels, name rules and the velocity codec, and the client uses all of them. With an old DLL the client won't compile.
2. Persistence migration: `dotnet ef database update` in `ArcheCore.Server.Persistence`, or run `SQL/unique_character_names.sql` by hand (after `add_save_seq.sql`).
3. Restart persistence, then the world server, then run the client. A client and server from different builds disagree on channels (M4) and velocity (M8).

## H3 — packet rate limiting

`Core/Networking/PacketRateLimiter.cs`. The world server keeps one token bucket per (peer, opcode) and checks it in `WorldServer.OnNetworkReceive` before any handler runs.

- **Over the limit:** the packet is dropped silently and the handler never runs. Log lines are capped at one per peer per 5s.
- **Flooding** (200 drops within 10s): the peer is disconnected.
- **Limits:** set per opcode in the `Rules` table. Movement is 40/s (the client sends 20). Market packets are 1/s, because each one becomes an HTTP call. Chat is 2/s with a burst of 6. Any opcode not in the table gets 10/s with a burst of 20.
- **If a new feature sends faster legitimately,** raise its line in the table.

## H4 — market proximity

`Core/Managers/MarketAccess.cs`.

- **Opening the auction house or mailbox** through Interact records which NPC it was (`PlayerSession.MarketTargetId`).
- **Every auction packet** (browse, create, buy, cancel) and **every mail claim** is checked against that NPC. It must still exist, still offer that action, and the player must be within its InteractRange + 2m.
- **The cash shop is not gated.** It's the HUD button, usable anywhere as in ArcheAge. Its purchases arrive by mail, and mail is gated.

## H5 — character names and rich text

- **Rules** (`ArcheCore.Network/Shared/CharacterNameRules.cs`, shared by client and server): 3–16 characters, letters A–Z and digits, starting with a letter, and a small reserved list (admin, gm, system…). They're checked in three places: the client create screen, the world server, and the persistence server.
- **Unique, case-insensitive:** the name column uses `utf8mb4_unicode_ci` with a unique index. The migration renames existing duplicates first: the oldest character keeps the name and later ones become `Name_<id>`.
- **A refused name no longer disconnects you.** The new `W2CCreateCharacterFailed` (82) packet carries the reason, the create screen shows it, and you can try again.
- **Chat:** the server strips control characters and bidi overrides.
- **Client:** player-typed text (chat, names in nameplates, target frame and player frame, death screen, mail subject and sender, auction seller) goes through `RichText.Safe`, which wraps it in `<noparse>`. A `<size=999>` typed in chat shows up as plain text.

## M1 — only players observe

`InterestManager`: NPCs, harvest nodes and corpses are observed but never observe anything themselves. A non-player's narrow phase searches a second, player-only grid. A camp of 80 mobs and nodes with no players nearby now does no interest work between them. Before, that was 80×79 links, rebuilt every time something wandered.

## M2 — no closure per NPC per tick

NPC moves and NPC attacks are applied inline in `NpcAiManager` instead of through `EnqueueAction`. That removes one closure allocation per moving NPC per tick and one tick of latency.

## M3 — serialise once per broadcast

`WorldserverPacketSender.Encode` produces the wire bytes. `SendToAll` / `ReplicationManager.Broadcast*` serialise once and send the same array to every peer. The bytes are identical to the old format.

## M4 — channels

`ArcheCore.Network/Shared/PacketChannels.cs`. Channel 0 carries world traffic (spawns, combat, corrections, gold, inventory, enter-world, loot window). Channel 1 carries bulk UI (auction, mail, cash shop, shop, quest catalogue, log and updates, item data, character list). Channel 2 carries chat, MOTD and announcements. A big auction page can no longer hold up a combat hit.

Both NetManagers use `ChannelsCount = PacketChannels.Count`.

## M5 — one clock

`Core/ServerClock.cs` is a monotonic Stopwatch clock used for cooldowns, harvest and corpse timers, NPC AI, respawns, spawner grace, the movement validator and the auth throttle. Before, these mixed `DateTime.UtcNow`, `Environment.TickCount64` and Stopwatch. Auction listing expiry stays on wall-clock UTC, because the auction service has to agree on it.

## M7 — Lua sandbox

`LuaEngine` runs scripts with `CoreModules.Preset_SoftSandbox`, so there's no `io`, no `os.execute`/`os.remove`, and no `load`/`dofile`/`require`. Every call into Lua runs as a coroutine with `AutoYieldCounter`:

- A hook gets 200k instructions (a few ms); a script's top level at load gets 5M.
- A hook that overruns is stopped. After 3 overruns it's switched off until restart, and that's logged as an error.

Scripts that need `os.date` or file access will now fail. None of the current ones use them.

## M8 — velocity range

`ArcheCore.Network/Shared/VelocityCodec.cs` is shared by the server's writer and the client's `SnapshotReader`. It still uses one signed byte per axis, but on a square-root curve that goes up to 100 u/s (the old cap was 31.75). Precision is about 0.35 u/s at run speed and about 1.6 u/s at 100 u/s.

## Tests

The integration run against MariaDB 10.11 and the real persistence process has 66 checks. It covers the save fixes plus:

- The name migration renaming duplicates.
- Case-insensitive uniqueness, including 8 simultaneous creates of the same name, of which only 1 succeeded.
- Rich-text, reserved and short names refused.
- Token buckets: burst, refill, separate buckets per peer and per opcode, 20Hz movement never dropped, a flood gets disconnected.
- The Lua sandbox: no io, os.execute or load; a forever-loop at load and a runaway hook are both stopped; the runaway hook is switched off after 3 overruns while the good hook keeps running.
- Interest: 80 packed NPCs with no links between them, and a player seeing and leaving them.
- Encoded bytes identical to the old writer.
- Channel mapping.
- Velocity round-trips, including 80 u/s.
