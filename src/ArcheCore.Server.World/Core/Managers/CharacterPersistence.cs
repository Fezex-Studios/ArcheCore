using System;
using System.Numerics;
using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// The single call-site for "save this character".
    ///
    /// Two different save shapes live here, and they are handled
    /// differently on purpose:
    ///
    ///   - Gold/level/position are a full-value UPDATE - every save sends
    ///     the character's CURRENT value, not a delta. That makes it safe
    ///     to mark them saved optimistically, before the HTTP call even
    ///     starts: if the call fails, HasBeenSaved is rolled back to
    ///     false, IsDirty goes true again, and the NEXT autosave just
    ///     resends whatever the current value is by then - which is
    ///     always correct, because there was never a "diff" to lose.
    ///
    ///   - Inventory is a DIFF - only the slots that changed since the
    ///     last CONFIRMED save are sent. That makes optimistic marking
    ///     actively wrong: if SavedInventory were updated before the send
    ///     is confirmed and the send then failed, the next autosave would
    ///     compare Inventory to a SavedInventory that already (falsely)
    ///     matches it, compute an EMPTY diff, and the lost write would
    ///     never be retried. So SavedInventory/InventoryDirty are only
    ///     touched after the persistence server confirms the write - see
    ///     SaveAndReportAsync below.
    ///
    /// SaveInBackground is what gameplay code uses: fire the save without
    /// blocking the tick thread. SaveAsync is for shutdown, where the
    /// caller needs to wait.
    /// </summary>
    public class CharacterPersistence
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly PersistenceClient _persistence;
        private readonly Action<Action> _enqueueOnTickThread;

        public CharacterPersistence(PersistenceClient persistence, Action<Action> enqueueOnTickThread)
        {
            _persistence = persistence;
            _enqueueOnTickThread = enqueueOnTickThread;
        }

        /// <summary>
        /// Compares Inventory to SavedInventory and returns only the
        /// slots that differ. ItemTemplateId/Quantity 0 in the result
        /// means "this slot is now empty" - the persistence server
        /// deletes that row rather than storing a zeroed one.
        /// </summary>
        public static InventorySlotDto[] ComputeInventoryDiff(PlayerSession session)
        {
            var diffs = new System.Collections.Generic.List<InventorySlotDto>();

            for (int i = 0; i < InventoryConstants.SlotCount; i++)
            {
                var cur   = session.Inventory[i];
                var saved = session.SavedInventory[i];

                if (cur.ItemTemplateId != saved.ItemTemplateId || cur.Quantity != saved.Quantity)
                {
                    diffs.Add(new InventorySlotDto
                    {
                        Slot           = i,
                        ItemTemplateId = cur.ItemTemplateId,
                        Quantity       = cur.Quantity
                    });
                }
            }

            return diffs.ToArray();
        }

        /// <summary>Tick thread only.</summary>
        public void SaveInBackground(PlayerSession session)
        {
            long characterId = session.CharacterId;
            int accountId    = session.AccountId;
            string name      = session.Name;
            int level        = session.Level;
            var pos          = session.Position;
            int gold         = session.Gold;

            // Gold/level/position: safe to mark saved now (see class doc).
            session.HasBeenSaved  = true;
            session.SavedPosition = pos;
            session.SavedLevel    = level;
            session.SavedGold     = gold;

            // Inventory: snapshot the diff and the target state, but do
            // NOT touch SavedInventory/InventoryDirty yet - that happens
            // only once SaveAndReportAsync hears the write was confirmed.
            // Snapshotting Inventory here (not re-reading it later) also
            // means a TryAddItem/TryMoveItem that happens WHILE this send
            // is in flight is naturally excluded from this diff and will
            // show up correctly in the NEXT one instead of racing it.
            InventorySlotDto[] inventoryDiff = Array.Empty<InventorySlotDto>();
            InventorySlot[] targetInventory = null;

            if (session.InventoryDirty)
            {
                inventoryDiff = ComputeInventoryDiff(session);
                targetInventory = (InventorySlot[])session.Inventory.Clone();
            }

            // Quests ride the same pass. They're sent whole rather than as a
            // diff: a character has a handful of quest rows, so working out
            // which changed would cost more than sending them.
            QuestStateDto[] quests = null;

            var questManager = QuestManager.Current;

            if (session.QuestsDirty && questManager != null)
            {
                quests = questManager.BuildSaveSet(session);
                session.QuestsDirty = false;   // set again below if the save fails
            }

            _ = SaveAndReportAsync(
                session, characterId, accountId, name, level, pos, gold,
                inventoryDiff, targetInventory);

            if (quests is { Length: > 0 })
                _ = SaveQuestsAndReportAsync(session, characterId, accountId, quests);
        }

        private async Task SaveAndReportAsync(
            PlayerSession session, long characterId, int accountId, string name,
            int level, Vector3 pos, int gold,
            InventorySlotDto[] inventoryDiff, InventorySlot[] targetInventory)
        {
            var (baseOk, inventoryOk) =
                await SaveAsync(characterId, accountId, name, level, pos, gold, inventoryDiff);

            // Back on the tick thread - these fields are tick-thread-only.
            _enqueueOnTickThread(() =>
            {
                if (!baseOk)
                {
                    // Force the next autosave to resend gold/level/position
                    // with whatever the CURRENT values are by then.
                    session.HasBeenSaved = false;
                }

                if (targetInventory != null)
                {
                    if (inventoryOk)
                    {
                        // Confirmed - SavedInventory can now advance to the
                        // state we sent. Not to session.Inventory's CURRENT
                        // value, which may have moved on since we started.
                        session.SavedInventory = targetInventory;
                        // Only clear the flag if nothing has changed the
                        // live inventory since we snapshotted it - if it
                        // has, InventoryDirty must stay true so the newer
                        // change still gets picked up next time.
                        session.InventoryDirty = !InventoryEquals(session.Inventory, targetInventory);
                    }
                    else
                    {
                        // Not confirmed - leave SavedInventory untouched
                        // and make sure the flag is still set, so the next
                        // autosave recomputes and resends a diff against
                        // the (still-stale) SavedInventory.
                        session.InventoryDirty = true;
                    }
                }
            });
        }

        private async Task SaveQuestsAndReportAsync(
            PlayerSession session, long characterId, int accountId, QuestStateDto[] quests)
        {
            bool ok = false;

            try
            {
                ok = await _persistence.W2PQuestSave.Send(characterId, accountId, quests);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[CharacterPersistence] Quest save threw for character {characterId}: {ex.Message}");
            }

            if (ok)
                return;

            // Not confirmed: mark it dirty again so the next autosave retries
            // with whatever the state is by then.
            _enqueueOnTickThread(() => session.QuestsDirty = true);
        }

        private static bool InventoryEquals(InventorySlot[] a, InventorySlot[] b)
        {
            for (int i = 0; i < InventoryConstants.SlotCount; i++)
                if (a[i].ItemTemplateId != b[i].ItemTemplateId || a[i].Quantity != b[i].Quantity)
                    return false;
            return true;
        }

        /// <summary>
        /// Safe from any thread. Never throws. Sends the base character
        /// save and (if there's a non-empty diff) the inventory save in
        /// parallel, and reports each outcome separately - a persistence
        /// server that's up for one route and briefly failing on another
        /// is a real scenario this needs to report correctly, not collapse
        /// into a single bool.
        /// </summary>
        public async Task<(bool baseOk, bool inventoryOk)> SaveAsync(
            long characterId, int accountId, string name, int level,
            Vector3 pos, int gold, InventorySlotDto[] inventoryDiff)
        {
            var baseTask = SaveBaseAsync(characterId, accountId, name, level, pos, gold);

            Task<bool> inventoryTask = (inventoryDiff != null && inventoryDiff.Length > 0)
                ? SaveInventoryAsync(characterId, accountId, inventoryDiff)
                : Task.FromResult(true); // nothing to save = trivially ok

            await Task.WhenAll(baseTask, inventoryTask);

            return (baseTask.Result, inventoryTask.Result);
        }

        private async Task<bool> SaveBaseAsync(
            long characterId, int accountId, string name, int level, Vector3 pos, int gold)
        {
            try
            {
                bool ok = await _persistence.W2PCharacterSave.Send(
                    characterId, accountId, name, level, pos.X, pos.Y, pos.Z, gold);

                if (ok)
                    Logger.Debug($"[Save] CharacterId={characterId} saved.");
                else
                    Logger.Error($"[Save] FAILED CharacterId={characterId} - persistence server returned an error.");

                return ok;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[Save] FAILED CharacterId={characterId} - persistence server unreachable.");
                return false;
            }
        }

        private async Task<bool> SaveInventoryAsync(
            long characterId, int accountId, InventorySlotDto[] changes)
        {
            try
            {
                bool ok = await _persistence.W2PInventorySave.Send(characterId, accountId, changes);

                if (ok)
                    Logger.Debug($"[Save] CharacterId={characterId} inventory: {changes.Length} slot(s) saved.");
                else
                    Logger.Error($"[Save] FAILED CharacterId={characterId} inventory - persistence server returned an error.");

                return ok;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[Save] FAILED CharacterId={characterId} inventory - persistence server unreachable.");
                return false;
            }
        }
    }
}
