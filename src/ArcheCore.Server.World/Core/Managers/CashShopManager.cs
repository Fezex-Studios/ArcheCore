using System;
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
    /// The cash shop. The catalogue, the credit balances and the general
    /// mailbox all live in the persistence database, so a purchase is ONE
    /// transaction there: check the balance, deduct it, post the goods.
    /// There is no moment where an account has been charged and nothing was
    /// sent.
    ///
    /// This server does almost nothing: it asks, and it tells the player.
    /// The goods arrive through the general mailbox like everything else,
    /// which also means buying with a full bag is never a problem.
    ///
    /// Credits are per account and have nothing to do with gold. Nothing here can
    /// mint it - balances are topped up outside the game.
    /// </summary>
    public class CashShopManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly PersistenceClient _persistence;
        private readonly Action<Action> _enqueueOnTickThread;

        public CashShopManager(PersistenceClient persistence, Action<Action> enqueueOnTickThread)
        {
            _persistence = persistence;
            _enqueueOnTickThread = enqueueOnTickThread;
        }

        public async Task BrowseAsync(NetPeer peer, PlayerSession session)
        {
            try
            {
                var response = await _persistence.W2PCashShop.Catalog(session.AccountId);
                var items = response?.Items ?? Array.Empty<CashShopItemDto>();

                var entries = new CashShopEntryData[items.Length];
                for (int i = 0; i < items.Length; i++)
                {
                    entries[i] = new CashShopEntryData
                    {
                        Id             = items[i].Id,
                        DisplayName    = items[i].DisplayName,
                        Category       = items[i].Category,
                        ItemTemplateId = items[i].ItemTemplateId,
                        Quantity       = items[i].Quantity,
                        PriceCredits   = items[i].PriceCredits,
                        IsGiftable     = items[i].IsGiftable
                    };
                }

                int balance = response?.Balance ?? 0;
                _enqueueOnTickThread(() => W2CCashShopListPacketSender.Send(peer, entries, balance));
            }
            catch (Exception ex)
            {
                Logger.Warn($"[CashShop] Catalogue failed for account {session.AccountId}: {ex.Message}");
                _enqueueOnTickThread(() => W2CMarketResultPacketSender.Send(peer, false, "The cash shop is unavailable right now."));
            }
        }

        public async Task BuyAsync(NetPeer peer, PlayerSession session, int cashShopItemId)
        {
            if (session.CharacterId <= 0)
                return;

            try
            {
                var response = await _persistence.W2PCashShop.Buy(session.AccountId, session.CharacterId, cashShopItemId);

                if (response is not { Bought: true })
                {
                    string reason = response?.Reason ?? "That purchase didn't go through.";
                    _enqueueOnTickThread(() => W2CMarketResultPacketSender.Send(peer, false, reason, refresh: 2));
                    return;
                }

                Logger.Info($"[CashShop] Account {session.AccountId} bought '{response.DeliveredName}' " +
                            $"for character {session.CharacterId}; {response.Balance} credits left");

                _enqueueOnTickThread(() => W2CMarketResultPacketSender.Send(
                    peer, true,
                    $"Bought {response.DeliveredName} - it's in your mailbox.",
                    refresh: 2));
            }
            catch (Exception ex)
            {
                // A failed call means the transaction never committed, so
                // nothing was charged.
                Logger.Warn($"[CashShop] Purchase failed for account {session.AccountId}: {ex.Message}");
                _enqueueOnTickThread(() => W2CMarketResultPacketSender.Send(peer, false, "The cash shop is unavailable right now."));
            }
        }

        /// <summary>
        /// Buy an item FOR another character. Charged to this player's
        /// credits, delivered to the recipient's mailbox from this player's
        /// name. Whether the item can be gifted at all is the persistence
        /// server's call (the is_giftable flag), not this server's and not
        /// the client's.
        /// </summary>
        public async Task GiftAsync(NetPeer peer, PlayerSession session, int cashShopItemId, string recipientName)
        {
            if (session.CharacterId <= 0)
                return;

            recipientName = (recipientName ?? "").Trim();

            if (recipientName.Length == 0 || recipientName.Length > 64)
            {
                _enqueueOnTickThread(() => W2CMarketResultPacketSender.Send(peer, false, "Type the name of the character to send it to."));
                return;
            }

            try
            {
                var response = await _persistence.W2PCashShop.Gift(session.AccountId, session.CharacterId, cashShopItemId, recipientName);

                if (response is not { Bought: true })
                {
                    string reason = response?.Reason ?? "That gift didn't go through.";
                    _enqueueOnTickThread(() => W2CMarketResultPacketSender.Send(peer, false, reason, refresh: 2));
                    return;
                }

                Logger.Info($"[CashShop] Account {session.AccountId} (character {session.CharacterId}) gifted " +
                            $"'{response.DeliveredName}' to {response.RecipientName}; {response.Balance} credits left");

                _enqueueOnTickThread(() => W2CMarketResultPacketSender.Send(
                    peer, true,
                    $"Sent {response.DeliveredName} to {response.RecipientName}. It's waiting in their mailbox.",
                    refresh: 2));
            }
            catch (Exception ex)
            {
                // A failed call means the transaction never committed, so nothing was charged.
                Logger.Warn($"[CashShop] Gift failed for account {session.AccountId}: {ex.Message}");
                _enqueueOnTickThread(() => W2CMarketResultPacketSender.Send(peer, false, "The cash shop is unavailable right now."));
            }
        }
    }
}
