# First Table Batch: Items, Shop, Quests (15 new tables)

Drop the classes in `GameData/Items/`, `GameData/Shop/`, `GameData/Quests/`
into the matching folders in `ArcheCore.Server.World/`, replace
`Utils/Database/SQLite/WorldDataDbContext.cs` with the version here, then:

```bash
dotnet ef migrations add AddItemsShopAndQuestTables --project ArcheCore.Server.World
```

One migration, all 15 tables + 2 existing ones' new indexes. `MigrateAsync()`
(already wired up per the earlier change) applies it automatically next
boot — no further steps needed on any machine.

## Relationship map

Every FK here is FK-by-convention (a plain `int` column, no navigation
property) — consistent with how `NpcSpawnerTable.TemplateId` already
relates to `NpcTemplate.Id` today. Arrows read "references":

```
ItemCategory  <┐
ItemRarity    <┼── (not yet wired to ItemTable — add a CategoryId/RarityId
               │    column to the EXISTING ItemTable when you're ready;
               │    left out here so this batch doesn't touch a table
               │    another feature might already be relying on)
               │
ItemTable ────┼── ItemStats.ItemId              (1 : 0-or-1)
               ├── CraftingRecipe.ResultItemId   (1 : many)
               ├── CraftingRecipeIngredient.ItemId (1 : many)
               ├── ShopEntry.ItemId              (1 : many)
               └── QuestReward.ItemId            (1 : many, 0 = none)

CraftingRecipe.Id ──< CraftingRecipeIngredient.RecipeId   (1 : many)

CurrencyTable.Id ──< ShopVendorTable.CurrencyId   (1 : many)
                 └─< QuestReward.CurrencyId       (1 : many, 0 = none)

NpcTemplate.Id ──< ShopVendorTable.NpcTemplateId  (1 : 0-or-1)
               └─< QuestGiverTable.NpcTemplateId  (1 : many)
               └─< QuestObjective.TargetId        (when ObjectiveType = KillTarget/InteractWith)

ShopVendorTable.Id ──< ShopEntry.VendorId         (1 : many)

QuestTable.Id ──< QuestStage.QuestId              (1 : many)
              ├─< QuestReward.QuestId             (1 : many)
              ├─< QuestGiverTable.QuestId         (1 : many)
              ├─< QuestPrerequisite.QuestId          (self-referencing, "this quest requires...")
              └─< QuestPrerequisite.RequiredQuestId  ("...this other quest")

QuestStage.Id ──< QuestObjective.QuestStageId     (1 : many)
```

## What's deliberately NOT in this batch

- **No inventory or currency-balance table.** "How much gold does
  character X have" and "what items does character X own" are
  player-owned mutable state — per the project's existing split (see
  `03-DATABASE-AND-PERSISTENCE.md`), that belongs in `persistence.db` on
  the Node/Bun side, not here. `CurrencyTable` and `ItemTable` describe
  *what exists*; a character's actual balance/inventory rows are a
  separate table in the other database, following the same pattern as
  `W2PCharacterSaveHandler`'s `characters` table today.
- **No `CategoryId`/`RarityId` columns added to `ItemTable` yet.** Deferred
  on purpose — that's a migration *touching* an existing table rather
  than only adding new ones, worth doing as its own small, reviewable
  migration once you're ready, rather than folding it into this batch.

## Seeding

Not covered here — these classes only define schema (via
`WorldDataDbContext`'s `DbSet`s and the resulting migration). Getting
actual rows into them is up to whatever seeding approach you're using.
Two things worth keeping in mind regardless of that approach:

- `ItemCategory`, `ItemRarity`, `CurrencyTable` are natural-key lookup
  tables (`Name` is the intended lookup key) — whatever seeds them should
  treat "does a row with this name already exist" as the update-vs-insert
  check, not blindly insert every run. No `UNIQUE` constraint is enforced
  at the DB level, so this is on the seeding logic to get right.
- The rest (`ShopEntry`, `QuestStage`, `QuestObjective`, etc.) have real
  interdependencies — a `ShopEntry` needs a `ShopVendorTable.Id` and an
  `ItemTable.Id` to already exist — so whatever seeds them needs to run in
  dependency order: categories/currencies first, then items, then
  shop/quests.

