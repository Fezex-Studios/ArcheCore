# Character saves

How a character gets from the world server's memory into the database, and
why it can't be duplicated or lost on the way. This replaced the old
three-request save in September 2026 (audit items C1, C2, C3).

## The one save

Every save is a **full snapshot**: level, position, gold, every inventory
slot and the whole quest log, sent as one request to
`/characters/save-full` and written in **one transaction**.

| Field | Meaning |
|---|---|
| `SaveSeq` | Strictly increasing per character. The row's `save_seq` column holds the last one written. |
| `Inventory` | Every occupied slot. Anything not listed is empty afterwards. |
| `Quests` | The whole log, or `null` to leave quests alone. |
| `ClaimMailId` | Non-zero: delete this mail in the same transaction. |

The persistence server locks the character row and writes **only if
`SaveSeq` is higher than `save_seq`**. Answers:

| Result | Meaning |
|---|---|
| `Saved` | Written. |
| `Stale` | Not written; the database already has this seq or a newer one. If `CurrentSeq` equals the request's seq, that exact request landed earlier - a success. |
| `NotFound` | No such character for that account. |
| `MailGone` | The claimed mail doesn't exist. Nothing written. |
| `Invalid` | Malformed (negative gold, a slot twice...). Never retried; see the persistence log. |
| HTTP 500 | Rolled back. Safe to retry. |

The old `/characters/save`, `/characters/inventory/save` and
`/characters/quests/save` routes still exist but the world server no longer
calls them.

## Where the snapshot comes from

`CharacterPersistence.Capture` fills in identity, level and position, then
asks every **saved session component** (`IPersistentComponent`, listed in
`PlayerSession.PersistentComponents`) to write its part: `InventoryComponent`
writes gold and the bag, `QuestComponent` writes the quest log (or `null`
until the log has been loaded). Components never save themselves - one
snapshot, one transaction. See [CORE_SYSTEMS.md](CORE_SYSTEMS.md).

## The save chain (world server)

`CharacterSaveChain`, one per character, owned by `CharacterPersistence`:

1. **One request in flight** per character. The next starts only when the
   previous has a definite answer.
2. **Seq assigned at send time**, so the database sees saves in the order
   the snapshots were taken.
3. **Unknown outcome = ask again with the same seq.** A timeout or dropped
   connection is retried (250ms, 500ms ... 30s, repeating) until the
   database answers. Nothing is ever guessed.
4. **Queued saves coalesce.** Fifty saves while one is in flight become one
   more request carrying the newest state. Mail claims never coalesce.
5. **Chains outlive sessions.** A logout save keeps running after the player
   is gone; the next login of that character waits for it.

The session is marked saved when the snapshot is taken. If the chain gets a
definite failure (`Invalid`, `NotFound`, 20 server errors in a row), it sets
`HasBeenSaved = false` and the next autosave sends everything again.

### Ways in (all tick thread)

| Call | Use |
|---|---|
| `SaveInBackground(session)` | autosave, level-up, logout. Fire and forget. |
| `SaveNowAsync(session)` | **write-through**: wait for the database before doing something irreversible elsewhere. |
| `SaveClaimingMailAsync(session, mailId)` | the session already holds the mail's contents; saves them and deletes the mail together. |

## Login

1. `PlayerManager.PrepareLoginAsync` kicks the account's other in-world
   connection (queueing its logout save), then waits up to 20s for every
   save of the selected character to settle. Not settled = login refused.
2. Load. The response carries `SaveSeq`.
3. `PlayerSpawnManager.HandlePlayerConnected` refuses the spawn if a save of
   that character is running or a newer one was already confirmed (two
   logins racing past step 1). Otherwise saves continue from the loaded seq.

A refused login is just a disconnect; logging in again a moment later works.

## Economy write-through

Anything that talks to another service waits until the character's row
agrees with memory first:

| Action | Order |
|---|---|
| Auction: list | take item + deposit -> **save** -> create listing. Save not confirmed in 8s -> both go back (by mail if the player left or the bag is full). |
| Auction: buy | charge gold -> **save** -> take the listing. Save not confirmed in 8s -> refunded, not charged. |
| Mail: claim | check it fits -> add to character -> **save + delete mail in one transaction**. `MailGone` or failure -> contents taken back out. Doesn't fit -> nothing happens, it stays in the mailbox. |
| Cash shop | unchanged: already one transaction in the persistence database. |

## Deploying

The persistence database needs the `save_seq` column:

```
cd src/ArcheCore.Server.Persistence
dotnet ef database update
```

or run `SQL/add_save_seq.sql` by hand. Existing characters start at
`save_seq = 0`. Deploy persistence and world server together: the new
world server calls `/characters/save-full`, which an old persistence server
doesn't have.

## Tested

An integration run against MariaDB 10.11 and the real persistence server
process covers: the old path losing to a late save, out-of-order and
repeated saves refused, 50 saves coalescing into 2 requests, a lost answer
resolved by retry and written once, relog waiting for the logout save,
logins refused while persistence is down and recovering after, mail claims
atomic (including a failure injected half way through the transaction),
invalid saves not retried, abandoned quests staying abandoned, and
another account unable to write the character.
