using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Senders
{
    /// <summary>
    /// The general mailbox, on the persistence server. Every character has
    /// one, and everything that gives a player something posts to it: the
    /// auction house (through its own outbox), the cash shop, refunds and
    /// admin gifts.
    /// </summary>
    public class W2PMailSender
    {
        private readonly PersistenceClient _client;

        public W2PMailSender(PersistenceClient client) => _client = client;

        public Task<P2WMailListResponse> List(long characterId, int accountId)
            => _client.PostAsync<W2PMailListRequest, P2WMailListResponse>(
                "/mail/list", new W2PMailListRequest { CharacterId = characterId, AccountId = accountId });

        /// <summary>Deletes and returns in one transaction - Claimed = false means give nothing.</summary>
        public Task<P2WMailClaimResponse> Claim(long characterId, int accountId, long mailId)
            => _client.PostAsync<W2PMailClaimRequest, P2WMailClaimResponse>(
                "/mail/claim",
                new W2PMailClaimRequest { CharacterId = characterId, AccountId = accountId, MailId = mailId });

        /// <summary>
        /// Post something. Throws if the persistence server can't be reached;
        /// Sent = false in the answer means it will never be accepted.
        ///
        /// Pass a deliveryKey when the same mail might be sent twice (a refund
        /// that a retry could repeat): the persistence server delivers a given
        /// key once, however many times it's sent. Null means "no key" - fine
        /// for mail that is only ever sent once, from one place.
        /// </summary>
        public Task<P2WMailSendResponse> Send(long characterId, string sender, string subject,
                                              int gold, int itemTemplateId, int quantity,
                                              string? deliveryKey = null)
            => _client.PostAsync<W2PMailSendRequest, P2WMailSendResponse>(
                "/mail/send",
                new W2PMailSendRequest
                {
                    DeliveryKey = deliveryKey,
                    CharacterId = characterId, Sender = sender, Subject = subject,
                    Gold = gold, ItemTemplateId = itemTemplateId, ItemQuantity = quantity
                });
    }
}
