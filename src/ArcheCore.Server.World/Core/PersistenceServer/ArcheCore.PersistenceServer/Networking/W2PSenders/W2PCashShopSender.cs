using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Senders
{
    /// <summary>
    /// The cash shop, on the persistence server. A purchase is charged and
    /// posted to the buyer's mailbox there in one transaction; this just asks
    /// for it and reports what came back.
    /// </summary>
    public class W2PCashShopSender
    {
        private readonly PersistenceClient _client;

        public W2PCashShopSender(PersistenceClient client) => _client = client;

        public Task<P2WCashShopCatalogResponse> Catalog(int accountId)
            => _client.PostAsync<W2PCashShopCatalogRequest, P2WCashShopCatalogResponse>(
                "/cashshop/catalog", new W2PCashShopCatalogRequest { AccountId = accountId });

        public Task<P2WCashShopBuyResponse> Buy(int accountId, long characterId, int cashShopItemId)
            => _client.PostAsync<W2PCashShopBuyRequest, P2WCashShopBuyResponse>(
                "/cashshop/buy",
                new W2PCashShopBuyRequest { AccountId = accountId, CharacterId = characterId, CashShopItemId = cashShopItemId });

        /// <summary>Buy an item for another character: charged to the sender, mailed to the recipient.</summary>
        public Task<P2WCashShopBuyResponse> Gift(int accountId, long characterId, int cashShopItemId, string recipientName)
            => _client.PostAsync<W2PCashShopGiftRequest, P2WCashShopBuyResponse>(
                "/cashshop/gift",
                new W2PCashShopGiftRequest
                {
                    AccountId = accountId, CharacterId = characterId,
                    CashShopItemId = cashShopItemId, RecipientName = recipientName
                });
    }
}
