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
    /// CLAIMING IS DELETE-AND-RETURN. The persistence server deletes the mail
    /// and returns its contents in one call; the player is only given
    /// something when that came back true, so two clicks can't pay out
    /// twice. If it turns out not to fit (a full bag), it's POSTED BACK
    /// rather than lost - losing it would be the one unforgivable bug here.
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
                if (mailId != 0)
                {
                    await ClaimOneAsync(peer, session, mailId);
                }
                else
                {
                    var response = await _persistence.W2PMail.List(session.CharacterId, session.AccountId);
                    int didntFit = 0;

                    foreach (var mail in response?.Mail ?? Array.Empty<MailDto>())
                    {
                        // Skip anything that won't fit instead of claiming it and posting it straight
                        // back. The tick thread runs these checks in order, after the adds queued by the
                        // claims before them, so each check sees the bag as it really is by then.
                        if (mail.ItemTemplateId != 0 && mail.ItemQuantity > 0)
                        {
                            var (itemId, quantity) = (mail.ItemTemplateId, mail.ItemQuantity);

                            if (!await OnTickThreadAsync(() => _players.CanAddItem(peer, itemId, quantity)))
                            {
                                didntFit++;
                                continue;
                            }
                        }

                        await ClaimOneAsync(peer, session, mail.Id);
                    }

                    if (didntFit > 0)
                        _enqueueOnTickThread(() => W2CMarketResultPacketSender.Send(peer, false,
                            $"Your inventory is full - {didntFit} piece(s) of mail stayed in your mailbox."));
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Mail] Claim failed for character {session.CharacterId}: {ex.Message}");
            }

            await SendMailboxAsync(peer, session);
        }

        private async Task ClaimOneAsync(NetPeer peer, PlayerSession session, long mailId)
        {
            var response = await _persistence.W2PMail.Claim(session.CharacterId, session.AccountId, mailId);

            // Someone else got it, or it never existed. Give nothing.
            if (response is not { Claimed: true, Mail: not null })
                return;

            var mail = response.Mail;

            _enqueueOnTickThread(() =>
            {
                // Gold and inventory are tick-thread only.
                if (mail.Gold > 0 && !_players.TryAddGold(peer, mail.Gold))
                {
                    // The row is already deleted, so this exists only here: post ALL of it back.
                    _ = PostBackAsync(session, mail.Sender, mail.Subject, mail.Gold, mail.ItemTemplateId, mail.ItemQuantity,
                                      "gold would overflow");
                    return;
                }

                if (mail.ItemTemplateId != 0 && mail.ItemQuantity > 0 &&
                    !_players.TryAddItem(peer, mail.ItemTemplateId, mail.ItemQuantity))
                {
                    // Gold (if any) is already in the purse, so only the item goes back.
                    _ = PostBackAsync(session, mail.Sender, mail.Subject, 0, mail.ItemTemplateId, mail.ItemQuantity,
                                      "inventory full");
                    W2CMarketResultPacketSender.Send(peer, false, "Your inventory is full - that stayed in your mailbox.", refresh: 3);
                    return;
                }

                string taken = mail.Gold > 0 ? $"{mail.Gold}g" : "";
                if (mail.ItemTemplateId != 0)
                    taken = string.IsNullOrEmpty(taken)
                        ? $"{mail.ItemQuantity}x {ItemName(mail.ItemTemplateId)}"
                        : $"{taken}, {mail.ItemQuantity}x {ItemName(mail.ItemTemplateId)}";

                if (!string.IsNullOrEmpty(taken))
                    W2CInteractLootPacketSender.Send(peer, taken);
            });
        }

        /// <summary>
        /// Back into the mailbox it came from. The last line of defence
        /// against losing anything, so it must not throw - and if it can't
        /// do its job, it says so loudly.
        /// </summary>
        private async Task PostBackAsync(PlayerSession session, string sender, string subject,
                                         int gold, int itemTemplateId, int quantity, string why)
        {
            Logger.Info($"[Mail] Returning mail to character {session.CharacterId} ({why})");

            try
            {
                var result = await _persistence.W2PMail.Send(session.CharacterId, sender, subject, gold, itemTemplateId, quantity);

                if (result is not { Sent: true })
                    Logger.Error($"[Mail] LOST GOODS: the mailbox refused {quantity}x item {itemTemplateId} " +
                                 $"(+{gold}g) for character {session.CharacterId}: {result?.Reason}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[Mail] LOST GOODS: could not return {quantity}x item {itemTemplateId} " +
                             $"(+{gold}g) to character {session.CharacterId}: {ex.Message}");
            }
        }

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
