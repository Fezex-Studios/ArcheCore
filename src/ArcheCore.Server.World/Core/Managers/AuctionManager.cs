using System;
using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.AuctionServer;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// The auction house, as the world server sees it. The listings live in
    /// the auction service's own database; this owns players, so it does the
    /// two things that service can't: take an item out of a bag, and take
    /// gold out of a purse. Everything a player RECEIVES - sale proceeds, a
    /// purchase, an unsold or cancelled listing, a refund - arrives by MAIL,
    /// in the general mailbox on the persistence server.
    ///
    /// THE ORDER THAT MAKES IT SAFE
    ///
    ///   List    take the item and the deposit HERE, then write the row
    ///           there. If the row fails, the item is mailed straight back,
    ///           so the worst case is a round trip, never a lost stack.
    ///
    ///   Buy     look at the listing (its price), CHARGE THE BUYER here, then
    ///           take the listing there under a fresh PURCHASE KEY. Taking it
    ///           also queues the seller's gold and the buyer's item as mail,
    ///           in the same transaction that removes the listing. If the
    ///           take is refused, the buyer is refunded by mail. The buyer is
    ///           charged first so that a sale can never pay a seller for a
    ///           purchase nobody could afford.
    ///
    ///           If the answer never arrives, the SAME key is asked again -
    ///           the service answers "taken" for a purchase that already
    ///           happened and "not taken" for one that didn't, so a timeout
    ///           is resolved by asking, never by guessing. If the service is
    ///           still silent, that keeps going in the background for a
    ///           while; the buyer is either given the item or refunded, and
    ///           can't be both.
    ///
    ///   Cancel  take the listing there, seller-checked; the service mails
    ///           the item home itself.
    ///
    /// Because purchases are mailed, a full bag can never stop or lose one.
    /// </summary>
    public class AuctionManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>Matches the service's own fee; shown in the sell panel.</summary>
        public const int FeePercent = 5;

        /// <summary>Deposit to list, as a percentage of the asking price (minimum 1g). Not refunded.</summary>
        private const int DepositPercent = 2;

        // How hard to ask again when the auction service doesn't answer a purchase. The first set is
        // tried before telling the buyer anything; the second carries on quietly in the background.
        private static readonly TimeSpan[] QuickRetries = { TimeSpan.Zero, TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(1.5) };
        private static readonly TimeSpan[] SlowRetries =
            { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1),
              TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10) };

        private static readonly TimeSpan ListingLifetime = TimeSpan.FromHours(24);
        private const int MaxPrice = 1_000_000;

        private readonly AuctionClient _auction;
        private readonly PersistenceClient _persistence;
        private readonly PlayerManager _players;
        private readonly ItemManager _items;
        private readonly Action<Action> _enqueueOnTickThread;

        private DateTime _nextSweep = DateTime.MinValue;

        public AuctionManager(AuctionClient auction, PersistenceClient persistence, PlayerManager players,
                              ItemManager items, Action<Action> enqueueOnTickThread)
        {
            _auction = auction;
            _persistence = persistence;
            _players = players;
            _items = items;
            _enqueueOnTickThread = enqueueOnTickThread;
        }

        // ── Browsing ─────────────────────────────────────────────────

        public async Task BrowseAsync(NetPeer peer, PlayerSession session, string search, bool mineOnly)
        {
            try
            {
                var response = await _auction.Browse(search, mineOnly ? session.CharacterId : 0);
                var listings = response?.Listings ?? Array.Empty<AuctionListingDto>();

                var entries = new AuctionEntryData[listings.Length];
                long now = DateTime.UtcNow.Ticks;

                for (int i = 0; i < listings.Length; i++)
                {
                    var listing = listings[i];
                    entries[i] = new AuctionEntryData
                    {
                        Id             = listing.Id,
                        SellerName     = listing.SellerName,
                        ItemTemplateId = listing.ItemTemplateId,
                        ItemName       = ItemName(listing.ItemTemplateId),
                        Quantity       = listing.Quantity,
                        Price          = listing.Price,
                        MinutesLeft    = (int)Math.Max(0, TimeSpan.FromTicks(Math.Max(0, listing.ExpiresAtTicks - now)).TotalMinutes),
                        Mine           = listing.SellerCharacterId == session.CharacterId
                    };
                }

                _enqueueOnTickThread(() => W2CAuctionListPacketSender.Send(peer, entries, search, mineOnly, FeePercent));
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Auction] Browse failed for character {session.CharacterId}: {ex.Message}");
                Fail(peer, "The auction house is unavailable right now.");
            }
        }

        // ── Listing ──────────────────────────────────────────────────

        public void Create(NetPeer peer, PlayerSession session, int slot, int quantity, int price)
        {
            if (session.CharacterId <= 0)
                return;

            if (price < 1 || price > MaxPrice)
            {
                Fail(peer, $"Name a price between 1 and {MaxPrice:N0}g.");
                return;
            }

            if (!_players.TryPeekSlot(peer, slot, out var contents) || contents.ItemTemplateId == 0)
            {
                Fail(peer, "There's nothing in that slot.");
                return;
            }

            int count = quantity <= 0 || quantity > contents.Quantity ? contents.Quantity : quantity;
            int deposit = Math.Max(1, price * DepositPercent / 100);

            if (session.Gold < deposit)
            {
                Fail(peer, $"The deposit is {deposit}g and you have {session.Gold}g.");
                return;
            }

            if (!_players.TryTakeFromSlot(peer, slot, count, out int itemTemplateId, out int removed) || removed != count)
            {
                Fail(peer, "That couldn't be listed.");
                return;
            }

            // Deposit after the item, so a failure between the two can only
            // ever cost the deposit - never the stack.
            _players.TryAddGold(peer, -deposit);

            _ = CreateOnServerAsync(peer, session.CharacterId, session.Name ?? "",
                                    itemTemplateId, ItemName(itemTemplateId), count, price,
                                    DateTime.UtcNow.Add(ListingLifetime).Ticks);
        }

        private async Task CreateOnServerAsync(NetPeer peer, long sellerId, string sellerName,
                                               int itemTemplateId, string itemName, int quantity, int price, long expires)
        {
            bool created = false;

            try
            {
                var response = await _auction.Create(sellerId, sellerName, itemTemplateId, itemName, quantity, price, expires);
                created = response is { Created: true };
            }
            catch (Exception ex)
            {
                Logger.Error($"[Auction] Creating a listing failed for character {sellerId}: {ex.Message}");
            }

            if (!created)
            {
                // Out of the bag and not on the board: it exists only here.
                await PostMailAsync(sellerId, "Listing failed", 0, itemTemplateId, quantity);
                Fail(peer, "That couldn't be listed - it's waiting in your mailbox.");
                return;
            }

            Logger.Info($"[Auction] Character {sellerId} listed {quantity}x {itemName} for {price}g");
            _enqueueOnTickThread(() => W2CMarketResultPacketSender.Send(peer, true, $"Listed {quantity}x {itemName} for {price}g.", refresh: 1));
        }

        // ── Buying ───────────────────────────────────────────────────

        public async Task BuyAsync(NetPeer peer, PlayerSession session, long auctionId)
        {
            if (session.CharacterId <= 0)
                return;

            long buyerId = session.CharacterId;

            // 1. What does it cost? Asking changes nothing.
            AuctionListingDto listing;

            try
            {
                var found = await _auction.Get(auctionId);

                if (found is not { Found: true, Listing: not null })
                {
                    Fail(peer, "That's already gone.", refresh: 1);
                    return;
                }

                listing = found.Listing;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Auction] Buy failed for character {buyerId}: {ex.Message}");
                Fail(peer, "The auction house is unavailable right now.");
                return;
            }

            if (listing.SellerCharacterId == buyerId)
            {
                Fail(peer, "That's your own listing - cancel it instead.");
                return;
            }

            // 2. Charge the buyer. Gold is tick-thread only, and checking and
            //    deducting happen in the same tick-thread action, so two
            //    quick clicks can't both spend the same gold.
            bool charged;

            try
            {
                charged = await OnTickThreadAsync(() =>
                {
                    if (session.Gold < listing.Price)
                    {
                        W2CMarketResultPacketSender.Send(peer, false,
                            $"That costs {listing.Price}g and you have {session.Gold}g.", refresh: 1);
                        return false;
                    }

                    return _players.TryAddGold(peer, -listing.Price);
                });
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Auction] Could not charge character {buyerId}: {ex.Message}");
                Fail(peer, "That purchase couldn't be completed.");
                return;
            }

            if (!charged)
                return;

            // 3. Take the listing, under a key that makes asking again safe.
            string purchaseKey = Guid.NewGuid().ToString("N");

            var response = await TryBuyAsync(auctionId, buyerId, purchaseKey, QuickRetries);

            if (response is null)
            {
                // Still can't tell whether it happened. Guessing either way could cost someone,
                // so keep asking the same question in the background until it's answered.
                Logger.Warn($"[Auction] No answer buying listing {auctionId} for character {buyerId} " +
                            $"(key {purchaseKey}); will keep asking.");

                _ = FinishLaterAsync(peer, buyerId, listing, purchaseKey);

                Fail(peer, "The auction house is slow to answer. Your purchase will finish - or your gold will be " +
                           "refunded - on its own. Check your mailbox in a minute.", refresh: 1);
                return;
            }

            await SettleAsync(peer, buyerId, listing, purchaseKey, response);
        }

        /// <summary>
        /// Ask the auction service to complete a purchase, and ask again if it
        /// doesn't answer. Repeating is safe because every attempt carries the
        /// same purchase key. Returns null if there was never an answer.
        /// </summary>
        private async Task<P2WAuctionTakeResponse> TryBuyAsync(long auctionId, long buyerId, string purchaseKey, TimeSpan[] delays)
        {
            foreach (var delay in delays)
            {
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay);

                try
                {
                    var answer = await _auction.Buy(auctionId, buyerId, purchaseKey);
                    if (answer is not null)
                        return answer;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[Auction] Buying listing {auctionId} (key {purchaseKey}) got no answer: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>The service was silent. Keep asking, patiently, until it answers.</summary>
        private async Task FinishLaterAsync(NetPeer peer, long buyerId, AuctionListingDto listing, string purchaseKey)
        {
            foreach (var delay in SlowRetries)
            {
                var response = await TryBuyAsync(listing.Id, buyerId, purchaseKey, new[] { delay });

                if (response is null)
                    continue;

                Logger.Info($"[Auction] Purchase {purchaseKey} was settled late: " +
                            $"{(response.Taken ? "it went through" : "it did not happen")}.");

                await SettleAsync(peer, buyerId, listing, purchaseKey, response);
                return;
            }

            // The one case that still needs a human. The key can be looked up in the auction
            // database's auction_purchases table: a row there means the buyer WAS sold the item
            // (their mail is queued or delivered); no row means they were charged for nothing.
            Logger.Error($"[Auction] UNSETTLED PURCHASE: character {buyerId} was charged {listing.Price}g for listing " +
                         $"{listing.Id} ({listing.Quantity}x item {listing.ItemTemplateId}) and the auction service " +
                         $"never answered. Purchase key {purchaseKey}. Look it up in auction_purchases before refunding.");
        }

        /// <summary>
        /// Act on a definite answer: tell the buyer, or refund them. Safe to
        /// call once per purchase - the refund carries a delivery key, so even
        /// a repeat can't pay the buyer twice.
        /// </summary>
        private async Task SettleAsync(NetPeer peer, long buyerId, AuctionListingDto listing, string purchaseKey,
                                       P2WAuctionTakeResponse response)
        {
            if (response is { Taken: true })
            {
                Logger.Info($"[Auction] Character {buyerId} bought listing {listing.Id} " +
                            $"({listing.Quantity}x {listing.ItemTemplateId}) for {listing.Price}g");

                string arrives = response.MailDelivered
                    ? "it's in your mailbox."
                    : "it will arrive in your mailbox in a moment.";

                Tell(peer, true,
                     $"Bought {listing.Quantity}x {ItemName(listing.ItemTemplateId)} for {listing.Price}g - {arrives}",
                     refresh: 1);
                return;
            }

            // Definitely not bought (someone else was first, or it expired). Give the gold back.
            await PostMailAsync(buyerId, "Purchase refunded", listing.Price, 0, 0, deliveryKey: "refund-" + purchaseKey);

            Tell(peer, false, $"{response?.Reason ?? "That's already gone."} Your gold is in your mailbox.", refresh: 1);
        }

        // ── Cancelling ───────────────────────────────────────────────

        public async Task CancelAsync(NetPeer peer, PlayerSession session, long auctionId)
        {
            if (session.CharacterId <= 0)
                return;

            try
            {
                // Seller-checked there, not here: a hidden button is not a
                // permission check. The service mails the item home itself.
                var response = await _auction.Cancel(auctionId, session.CharacterId);

                if (response is not { Taken: true, Listing: not null })
                {
                    Fail(peer, response?.Reason ?? "That listing is no longer yours to cancel.", refresh: 1);
                    return;
                }

                var listing = response.Listing;

                _enqueueOnTickThread(() => W2CMarketResultPacketSender.Send(
                    peer, true,
                    $"Cancelled - {listing.Quantity}x {ItemName(listing.ItemTemplateId)} is on its way to your mailbox.",
                    refresh: 1));
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Auction] Cancel failed for character {session.CharacterId}: {ex.Message}");
                Fail(peer, "The auction house is unavailable right now.");
            }
        }

        // ── Expiry ───────────────────────────────────────────────────

        /// <summary>
        /// From the tick loop, about once a minute. The service deletes the
        /// listing and queues it home by mail in one transaction, so this
        /// only has to ask. Anything that doesn't sell comes back to its
        /// seller in their mailbox.
        /// </summary>
        public void Tick()
        {
            if (DateTime.UtcNow < _nextSweep)
                return;

            _nextSweep = DateTime.UtcNow.AddMinutes(1);
            _ = SweepExpiredAsync();
        }

        private async Task SweepExpiredAsync()
        {
            try
            {
                var response = await _auction.Expire(DateTime.UtcNow.Ticks, limit: 50);
                int count = response?.Expired?.Length ?? 0;

                if (count > 0)
                    Logger.Info($"[Auction] {count} listing(s) expired and were mailed back to their sellers.");
            }
            catch (Exception ex)
            {
                // Nothing is lost: the rows are only deleted when the call
                // succeeds, so the next sweep tries again.
                Logger.Warn($"[Auction] Expiry sweep failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Post something to a character's mailbox. Must not throw - it IS
        /// the recovery path. A failure here is the one thing in this system
        /// worth alerting on: an item or some gold that left somebody and
        /// reached nobody.
        /// </summary>
        private async Task PostMailAsync(long characterId, string subject, int gold, int itemTemplateId, int quantity,
                                         string deliveryKey = null)
        {
            try
            {
                var result = await _persistence.W2PMail.Send(characterId, "Auction House", subject, gold, itemTemplateId, quantity, deliveryKey);

                if (result is not { Sent: true })
                    Logger.Error($"[Auction] LOST GOODS: the mailbox refused {quantity}x item {itemTemplateId} " +
                                 $"(+{gold}g) for character {characterId}: {result?.Reason}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[Auction] LOST GOODS: could not post {quantity}x item {itemTemplateId} " +
                             $"(+{gold}g) to character {characterId}: {ex.Message}");
            }
        }

        /// <summary>Run something on the tick thread and wait for what it returns, without blocking that thread.</summary>
        private Task<T> OnTickThreadAsync<T>(Func<T> work)
        {
            // RunContinuationsAsynchronously: the code after the await must NOT run inline on the tick thread.
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            _enqueueOnTickThread(() =>
            {
                try { completion.SetResult(work()); }
                catch (Exception ex) { completion.SetException(ex); }
            });

            return completion.Task;
        }

        private void Fail(NetPeer peer, string message, int refresh = 0) => Tell(peer, false, message, refresh);

        /// <summary>
        /// Say something to a player - if they're still there. A late purchase
        /// result can arrive long after they've left, and there's nobody to tell.
        /// </summary>
        private void Tell(NetPeer peer, bool success, string message, int refresh = 0) =>
            _enqueueOnTickThread(() =>
            {
                if (peer.ConnectionState == ConnectionState.Connected)
                    W2CMarketResultPacketSender.Send(peer, success, message, refresh);
            });

        private string ItemName(int itemTemplateId) => _items.GetById(itemTemplateId)?.name ?? $"#{itemTemplateId}";
    }
}
