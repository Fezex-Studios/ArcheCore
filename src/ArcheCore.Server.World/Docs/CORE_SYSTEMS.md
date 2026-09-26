# Core systems

The five pieces the roadmap's **fix-first** list asked for before Phase 3
(September 2026). Each one replaces several hand-rolled copies of the same
idea, so the next feature is built on one pattern instead of copying a
sixth. None of them changed the client or the wire protocol, and gameplay is
the same as before.

| # | Piece | Where | Replaces |
|---|---|---|---|
| 1 | Effect system | `Core/Effects/`, `Effects` table | Potion code in PlayerManager, three damage rolls in CombatManager |
| 2 | Scheduler | `Core/Scheduler.cs` | Five timer lists with their own per-tick sweeps |
| 3 | Session components | `Core/Managers/Components/` | ~40 loose fields on PlayerSession |
| 4 | Two-phase start-up | `Core/Services/` | Four static `Current` singletons, setter injection |
| 5 | Saved components | `IPersistentComponent` | Save code that knew every field by name |

---

## 1. Effects

An **effect** is one thing that happens: *heal self 50*, *damage target
8-14*, *mount #1*. Items, skills and NPC swings are all **lists** of effects,
and `EffectApplier` is the only code that carries them out.

```
item used / skill hits / NPC swings
        |
EffectCatalog.ForItem / ForSkill / ForNpcSwing   -> Effect[]  (loaded once)
        |
EffectApplier.Check   -> can every effect land? (changes nothing)
EffectApplier.Apply   -> do them in order, return what happened
        |
caller sends the packets, handles death, loot and Lua
```

**Check before Apply** is what keeps the old rules: a potion at full health
is refused *before* it's consumed; an attack that can't land is refused
*before* the cooldown starts.

### The data

`Effects` table in `worldserver.db`, one row per effect:

| Column | Meaning |
|---|---|
| `OwnerType` | 1 = item (`OwnerId` = `Items.item_id`), 2 = skill (`OwnerId` = `Skills.Id`) |
| `Sort` | Run order within the owner, lowest first |
| `Kind` | 0 Script, 1 Heal, 2 ApplyStatus, 3 CastSkill, 4 Mount, 5 SummonPet, 6 Damage |
| `Target` | 0 Self (the user / attacker), 1 Target (what the attack was aimed at) |
| `Min`, `Max` | Heal / damage range, rolled inclusive. Equal for a fixed amount |
| `RefId` | Mount id, pet template id, status id, skill id |
| `DurationMs` | How long a status lasts |
| `Script` | Optional Lua hook name for Script effects |

Patch `036_effects_from_items_and_skills.sql` converted every existing
`ItemUses` row and skill into rows. **From now on, edit `Effects`** - once an
item or skill has rows, `ItemUses.EffectType/EffectValue` and
`Skills.MinDamage/MaxDamage` are ignored for it. `ItemUses` still decides
consume-on-use and the cooldown.

An item or skill with **no** rows falls back to those old columns, so a row
added the old way still works.

### Examples

A skill that hits for 20-30 and applies status 3 for five seconds:

```sql
INSERT INTO Effects (OwnerType, OwnerId, Sort, Kind, Target, Min, Max, RefId, DurationMs) VALUES
    (2, 4, 0, 6, 1, 20, 30, 0, 0),      -- damage the target
    (2, 4, 1, 2, 1,  0,  0, 3, 5000);   -- apply status 3 (logged stub until Phase 3)
```

A potion that heals 40-60 (it also needs its `ItemUses` row for
consume/cooldown):

```sql
INSERT INTO Effects (OwnerType, OwnerId, Sort, Kind, Target, Min, Max, RefId, DurationMs) VALUES
    (1, 20, 0, 1, 0, 40, 60, 0, 0);
```

Bad rows (unknown kind, `Max < Min`, a heal of 0...) are skipped at boot with
a warning naming the row.

### Adding a new kind of effect

1. Add it to `EffectKind` (`GameData/Effects/EffectRow.cs`). Never renumber.
2. `EffectApplier.CheckOne`: when can it **not** land?
3. `EffectApplier.ApplyOne`: what it does.

`ApplyStatus` and `CastSkill` are logged stubs today. Phase 3 status effects
fill in `ApplyStatus` and use the Scheduler for durations and ticks.

---

## 2. Scheduler

One timer system for the world server, run once per tick on the tick thread.

```csharp
var h = _scheduler.In(12_000, () => Despawn(corpse), "corpse expiry");
_scheduler.Cancel(h);                                    // looted early
_scheduler.Every(60_000, SweepExpired, "auction sweep"); // repeating
```

- Times are `ServerClock` milliseconds (monotonic). Resolution is one tick
  (50ms): a callback never runs early.
- Callbacks run **on the tick thread**, so they can touch sessions, the grid
  and anything else tick-owned without locks. Code on another thread hops
  over with `PlayerManager.EnqueueAction` first.
- A callback that throws is logged and doesn't stop the others.
- Keep the `Handle` if you might cancel. Cancel is cheap and safe to call
  twice.
- **Cooldowns are not timers.** A cooldown is a "ready at" timestamp checked
  when the key is pressed; nothing has to happen when it runs out.
- Not saved across restarts. Something that must survive a reboot (a siege
  at 8pm Saturday) stores its due time and re-schedules on boot.

Now on the scheduler: corpse expiry, NPC respawns, harvest completion,
harvest node respawns, the auction expiry sweep.

---

## 3. Session components

`PlayerSession` holds identity, position, zone and save bookkeeping. Each
system's state is its own component:

| Component | Holds | Saved |
|---|---|---|
| `session.Inventory` | `Gold`, `Slots`, item `Cooldowns`, `Dirty` | yes |
| `session.Quests` | `Progress`, `UnknownRows`, `Dirty`, `Loaded` | yes |
| `session.Combat` | `Health`, `MaxHealth`, `IsDead`, `SkillCooldowns`, `DiedAt` | no |
| `session.Mount` | `MountId`, `Model`, `SpeedMultiplier`, `PetNetworkId` | no |
| `session.Movement` | MovementValidator's budgets and violations | no |
| `session.Market` | `TargetId` (auction/mail NPC), `ClaimingMail` | no |

Rule of thumb: a manager touches its own component. When one needs another's
state, it reads it (`session.Combat.IsDead`) and changes it through the
owning manager (`Mounts.Dismount(...)`).

### Adding a component

1. A class in `Core/Managers/Components/`.
2. A `public readonly XComponent X = new();` on `PlayerSession`.
3. If it's saved, see 5.

---

## 4. Two-phase start-up

`WorldServer.StartAsync` does this:

1. **Construct** every manager. Constructors take only what the manager
   needs to *exist*.
2. **Register** them all in the `ServiceContainer`.
3. **Initialize**: every `IInitializable` gets `Initialize(services)` and
   looks up what it *talks to*. Everything already exists, so two managers
   that need each other (combat and NPC AI) just work, and the order of the
   constructor lines no longer matters.
4. **Load data**. The order matters here and only here (items before
   quests/shops/loot).
5. **Packet handlers** are built from the same container.

```csharp
public class MyManager : IInitializable
{
    private CombatManager _combat;
    public void Initialize(ServiceContainer services) => _combat = services.Get<CombatManager>();
}
```

Then add `services.Register(_myManager);` in `WorldServer.BuildServiceContainer`.

There are no static `Current` singletons any more, and a test checks that
none come back. `services.Get<T>()` for something never registered fails at
start-up with the type's name, not mid-game with a null.

---

## 5. Saved components (`IPersistentComponent`)

```csharp
public interface IPersistentComponent
{
    bool IsDirty { get; }
    void WriteTo(W2PCharacterSaveFullRequest snapshot);
    void MarkSaved();
}
```

`CharacterPersistence.Capture` builds the snapshot by asking every component
in `PlayerSession.PersistentComponents` to `WriteTo` it, and
`PlayerSession.IsDirty` asks each one. The autosave and save code don't know
which systems exist.

**Components never save themselves.** A character save is one snapshot in
one transaction (see [SAVES.md](SAVES.md)). Separate saves per component
would bring back the half-applied saves C1-C3 removed: a crash between "quest
turned in" and "reward items saved".

### Adding a saved system (skills, reputation, housing...)

1. A component implementing `IPersistentComponent`; add it to
   `PersistentComponents` in the `PlayerSession` constructor.
2. A field on `W2PCharacterSaveFullRequest` (null = "leave it alone", like
   `Quests`).
3. The persistence server writes it inside the same `/characters/save-full`
   transaction, and returns it in the load response.
4. Fill the component on spawn (`PlayerSpawnManager`), then `MarkSaved`.

---

## Tests

`ArcheCore.Tests` covers all five: scheduler ordering, cancellation and
exception isolation; start-up wiring and "no static Current"; component
dirty tracking and the snapshot; effect rolls, clamping, kills and refusals;
and the `AddEffects` migration plus patch 036 run on a **copy** of
`worldserver.db`, checking that the converted rows behave exactly like the
old columns.
