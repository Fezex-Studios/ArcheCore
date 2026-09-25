using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using ArcheCore.Server.World.Utils.Config;
using MessagePack;
using NLog;

namespace ArcheCore.Server.World.AuctionServer
{
    /// <summary>
    /// Talks to the auction service, which has its own MySQL database
    /// holding the listings. Mail it owes players (sale proceeds, purchases,
    /// unsold listings) is delivered by that service itself, to the general
    /// mailbox on the persistence server - this client has nothing to do
    /// with mail. Same shape as PersistenceClient: MessagePack over HTTP,
    /// one method per route.
    ///
    /// Every call can fail, because that service can be down. Failure always
    /// means "nothing happened" - the callers treat an exception exactly
    /// like a refusal, so a dead auction service costs sales, never items.
    /// </summary>
    public class AuctionClient
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly HttpClient _http;

        public AuctionClient(WorldServerConfig config)
        {
            string baseUrl = string.IsNullOrWhiteSpace(config.AuctionServerUrl)
                ? "http://127.0.0.1:5090"
                : config.AuctionServerUrl;

            _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };
            Logger.Info($"[Auction] Using the auction service at {baseUrl}");
        }

        public async Task<bool> PingAsync()
        {
            try
            {
                var response = await _http.GetAsync("/health");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Auction] Not answering at {_http.BaseAddress}: {ex.Message}");
                return false;
            }
        }

        public Task<P2WAuctionBrowseResponse> Browse(string search, long sellerCharacterId)
            => PostAsync<W2PAuctionBrowseRequest, P2WAuctionBrowseResponse>("/auctions/browse",
                new W2PAuctionBrowseRequest { Search = search ?? "", SellerCharacterId = sellerCharacterId });

        public Task<P2WAuctionCreateResponse> Create(long sellerCharacterId, string sellerName, int itemTemplateId,
                                                     string itemName, int quantity, int price, long expiresAtTicks)
            => PostAsync<W2PAuctionCreateRequest, P2WAuctionCreateResponse>("/auctions/create",
                new W2PAuctionCreateRequest
                {
                    SellerCharacterId = sellerCharacterId, SellerName = sellerName,
                    ItemTemplateId = itemTemplateId, ItemName = itemName,
                    Quantity = quantity, Price = price, ExpiresAtTicks = expiresAtTicks
                });

        /// <summary>Look at one live listing without touching it. Found = false if it's gone or expired.</summary>
        public Task<P2WAuctionGetResponse> Get(long auctionId)
            => PostAsync<W2PAuctionGetRequest, P2WAuctionGetResponse>("/auctions/get",
                new W2PAuctionGetRequest { AuctionId = auctionId });

        /// <summary>
        /// BUY a listing. Exactly one caller wins. On a win the service has
        /// already queued the seller's gold and the buyer's item as mail, in
        /// the same transaction that removed the listing - so the caller has
        /// nothing left to deliver. The buyer must have been charged BEFORE
        /// calling this.
        ///
        /// SAFE TO REPEAT with the same purchaseKey: a purchase that already
        /// happened is answered Taken = true rather than being attempted
        /// again. That's what lets a timeout be resolved instead of guessed at.
        /// </summary>
        public Task<P2WAuctionTakeResponse> Buy(long auctionId, long buyerCharacterId, string purchaseKey)
            => PostAsync<W2PAuctionTakeRequest, P2WAuctionTakeResponse>("/auctions/take",
                new W2PAuctionTakeRequest { AuctionId = auctionId, BuyerCharacterId = buyerCharacterId, PurchaseKey = purchaseKey });

        /// <summary>CANCEL your own listing. The service mails the item back.</summary>
        public Task<P2WAuctionTakeResponse> Cancel(long auctionId, long sellerCharacterId)
            => PostAsync<W2PAuctionTakeRequest, P2WAuctionTakeResponse>("/auctions/take",
                new W2PAuctionTakeRequest { AuctionId = auctionId, ExpectedSellerId = sellerCharacterId });

        public Task<P2WAuctionExpireResponse> Expire(long nowTicks, int limit)
            => PostAsync<W2PAuctionExpireRequest, P2WAuctionExpireResponse>("/auctions/expire",
                new W2PAuctionExpireRequest { NowTicks = nowTicks, Limit = limit });

        private async Task<TResponse> PostAsync<TRequest, TResponse>(string route, TRequest payload)
        {
            using var content = new ByteArrayContent(MessagePackSerializer.Serialize(payload));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-msgpack");

            var response = await _http.PostAsync(route, content);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);

            return buffer.Length == 0 ? default : MessagePackSerializer.Deserialize<TResponse>(buffer.ToArray());
        }
    }
}
