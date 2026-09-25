using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// The general mailbox. Every character has one, it lives on the
    /// persistence server, and EVERYTHING that gives a player something ends
    /// up in it: the auction house (sale proceeds, purchases, unsold and
    /// cancelled listings), the cash shop, refunds, and admin gifts.
    ///
    /// This class is only the front door: it asks the persistence server what
    /// is waiting, and hands the contents to the player when they claim it.
    /// Posting mail is not done here - each sender posts to the persistence
    /// server itself.
    ///
    /// CLAIMING IS ONE TRANSACTION. The contents go into the character in
    /// memory, and then ONE save (/characters/save-full with ClaimMailId)
    /// writes the character holding them AND deletes the mail, together. So
    /// the database can never hold both the mail and its contents (a dupe),
    /// nor neither (a loss) - not even if the server dies half way. The
    /// earlier version deleted the mail first and gave the contents to an
    /// unsaved character; a crash in between lost them.
    ///
    /// Something that doesn't fit isn't claimed at all - it simply stays in
    /// the mailbox, since nothing has happened to it yet.
    /// </summary>
    public class MailManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly PersistenceClient _persistence;
        private readonly PlayerManager _players;
        private readonly ItemManager _items;
        private readonly Action<Action> _enqueueOnTickThread;

        public MailManager(
            PersistenceClient persistence,
            PlayerManager players,
            ItemManager items,
            Action<Action> enqueueOnTickThread)
        {
            _persistence = persistence;
            _players = players;
            _items = items;
            _enqueueOnTickThread = enqueueOnTickThread;
        }

        /// <summary>Show the mailbox.</summary>
        public async Task SendMailboxAsync(NetPeer peer, PlayerSession session)
        {
            if (session.CharacterId <= 0)
                return;

            try
            {
                var response = await _persistence.W2PMail.List(session.CharacterId, session.AccountId);

                var mail = response?.Mail ?? Array.Empty<MailDto>();
                var entries = new MailEntryData[mail.Length];

                for (int i = 0; i < mail.Length; i++)
                {
                    entries[i] = new MailEntryData
                    {
                        Id             = mail[i].Id,
                        Sender         = mail[i].Sender,
                        Subject        = mail[i].Subject,
                        Gold           = mail[i].Gold,
                        ItemTemplateId = mail[i].ItemTemplateId,
                        ItemQuantity   = mail[i].ItemQuantity,
                        ItemName       = mail[i].ItemTemplateId != 0 ? ItemName(mail[i].ItemTemplateId) : ""
                    };
                }

                _enqueueOnTickThread(() => W2CMailListPacketSender.Send(peer, entries));
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Mail] Could not read the mailbox for character {session.CharacterId}: {ex.Message}");
                _enqueueOnTickThread(() => W2CMarketResultPacketSender.Send(peer, false, "Your mailbox is unavailable right now."));
            }
        }

        /// <summary>
        /// Claim a specific id, 0 for everything that fits, or a NEGATIVE id
        /// for "just show me" - which is how the window refreshes without
        /// taking anything.
        /// </summary>
        public async Task ClaimAsync(NetPeer peer, PlayerSession session, long mailId)
        {
            if (session.CharacterId <= 0)
                return;

            if (mailId < 0)
            {
                await SendMailboxAsync(peer, session);
                return;
            }

            try
            {
                var response = await _persistence.W2PMail.List(session.CharacterId, session.AccountId);
                var mailbox = response?.Mail ?? Array.Empty<MailDto>();

                if (mailId != 0)
                {
                    var mail = Array.Find(mailbox, m => m.Id == mailId);

                    // Already claimed, or never existed. Give nothing.
                    if (mail != null && await ClaimOneAsync(peer, session, mail) == ClaimResult.DidntFit)
                        Tell(peer, "Your inventory is full - that stayed in your mailbox.");
                }
                else
                {
                    int didntFit = 0;

                    // One at a time, in order: each claim's fit check runs on
                    // the tick thread after the previous claim's contents were
                    // added, so it sees the bag as it really is by then.
                    foreach (var mail in mailbox)
                    {
                        if (await ClaimOneAsync(peer, session, mail) == ClaimResult.DidntFit)
                            didntFit++;
                    }

                    if (didntFit > 0)
                        Tell(peer, $"Your inventory is full - {didntFit} piece(s) of mail stayed in your mailbox.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Mail] Claim failed for character {session.CharacterId}: {ex.Message}");
            }

            await SendMailboxAsync(peer, session);
        }

        private enum ClaimResult { Claimed, DidntFit, NotClaimed }

        private async Task<ClaimResult> ClaimOneAsync(NetPeer peer, PlayerSession session, MailDto mail)
        {
            bool hasItem = mail.ItemTemplateId != 0 && mail.ItemQuantity > 0;

            // 1. On the tick thread: does it fit? Then put it in the
            //    character and snapshot a save that also deletes the mail.
            var (result, save) = await OnTickThreadAsync<(ClaimResult, Task<SaveOutcome>)>(() =>
            {
                // Left, or this very mail is already being claimed (a double click).
                if (peer.Tag != session || session.NetworkId == null || !session.ClaimingMail.Add(mail.Id))
                    return (ClaimResult.NotClaimed, null);

                bool fits = (long)session.Gold + Math.Max(0, mail.Gold) <= int.MaxValue &&
                            (!hasItem || _players.CanAddItem(peer, mail.ItemTemplateId, mail.ItemQuantity));

                if (!fits)
                {
                    session.ClaimingMail.Remove(mail.Id);
                    return (ClaimResult.DidntFit, null);
                }

                if (mail.Gold > 0)
                    _players.TryAddGold(peer, mail.Gold);

                if (hasItem)
                    _players.TryAddItem(peer, mail.ItemTemplateId, mail.ItemQuantity);

                return (ClaimResult.Claimed, _players.SaveClaimingMailAsync(session, mail.Id));
            });

            if (save == null)
                return result;

            // 2. Wait for the database. The save chain retries until it has a
            //    definite answer, so this is either "saved and deleted" or
            //    "definitely nothing written".
            var outcome = await save;

            await OnTickThreadAsync(() =>
            {
                session.ClaimingMail.Remove(mail.Id);

                if (outcome == SaveOutcome.Saved)
                {
                    string taken = Describe(mail);
                    if (peer.Tag == session && taken.Length > 0)
                        W2CInteractLootPacketSender.Send(peer, taken);
                    return true;
                }

                // Nothing was written, so the mail is still there (or was
                // never there). Take the contents back out of the character,
                // or the next ordinary save would write them in for free.
                TakeBack(peer, session, mail);
                return true;
            });

            if (outcome != SaveOutcome.Saved)
            {
                Logger.Warn($"[Mail] Claim of mail {mail.Id} for character {session.CharacterId} was not saved " +
                            $"({outcome}); its contents were taken back.");
                return ClaimResult.NotClaimed;
            }

            return ClaimResult.Claimed;
        }

        /// <summary>
        /// Undo a claim that didn't save. Tick thread only. Works on the
        /// session itself (not through the peer), because the player may
        /// have logged out while the save was running - and then a fresh
        /// save is queued, since the logout save already captured the
        /// contents.
        /// </summary>
        private void TakeBack(NetPeer peer, PlayerSession session, MailDto mail)
        {
            bool online = peer.Tag == session && session.NetworkId != null;

            int gold = Math.Min(Math.Max(0, mail.Gold), session.Gold);
            if (gold > 0)
            {
                if (online) _players.TryAddGold(peer, -gold);
                else session.Gold -= gold;
            }

            int toRemove = mail.ItemTemplateId != 0 ? Math.Max(0, mail.ItemQuantity) : 0;

            for (int i = 0; i < session.Inventory.Length && toRemove > 0; i++)
            {
                if (session.Inventory[i].ItemTemplateId != mail.ItemTemplateId)
                    continue;

                int take = Math.Min(toRemove, session.Inventory[i].Quantity);

                if (online)
                {
                    if (_players.TryTakeFromSlot(peer, i, take, out _, out int removed))
                        toRemove -= removed;
                }
                else
                {
                    session.Inventory[i].Quantity -= take;
                    if (session.Inventory[i].Quantity <= 0)
                        session.Inventory[i] = default;
                    session.InventoryDirty = true;
                    toRemove -= take;
                }
            }

            if (gold < mail.Gold || toRemove > 0)
                Logger.Error($"[Mail] Could not fully take back unsaved mail {mail.Id} from character {session.CharacterId} " +
                             $"(missing {mail.Gold - gold}g, {toRemove}x item {mail.ItemTemplateId}) - already spent.");

            if (!online)
                _players.SaveInBackground(session);
        }

        private string Describe(MailDto mail)
        {
            string taken = mail.Gold > 0 ? $"{mail.Gold}g" : "";

            if (mail.ItemTemplateId != 0)
                taken = string.IsNullOrEmpty(taken)
                    ? $"{mail.ItemQuantity}x {ItemName(mail.ItemTemplateId)}"
                    : $"{taken}, {mail.ItemQuantity}x {ItemName(mail.ItemTemplateId)}";

            return taken;
        }

        private void Tell(NetPeer peer, string message) =>
            _enqueueOnTickThread(() =>
            {
                if (peer.ConnectionState == ConnectionState.Connected)
                    W2CMarketResultPacketSender.Send(peer, false, message, refresh: 3);
            });

        /// <summary>Run something on the tick thread and wait for its answer, without blocking that thread.</summary>
        private Task<T> OnTickThreadAsync<T>(Func<T> work)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            _enqueueOnTickThread(() =>
            {
                try { completion.SetResult(work()); }
                catch (Exception ex) { completion.SetException(ex); }
            });

            return completion.Task;
        }

        private string ItemName(int itemTemplateId) => _items.GetById(itemTemplateId)?.name ?? $"#{itemTemplateId}";
    }
}
