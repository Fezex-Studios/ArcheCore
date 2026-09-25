using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using ArcheCore.Server.Auction.Data;
using MessagePack;
using Microsoft.EntityFrameworkCore;

namespace ArcheCore.Server.Auction.Services;

/// <summary>
/// Carries mail from this service's outbox to the persistence server's
/// general mailbox.
///
/// A sale queues its mail in the SAME transaction that removes the listing
/// (see /auctions/take), so by the time anything lands here the sale is
/// already final. This only has to make sure the mail arrives:
///
///   - rows are sent oldest first, and deleted only after the persistence
///     server says it has them;
///   - every send carries the row's DeliveryKey, and the persistence server
///     delivers a given key once - so if this service dies between "they
///     accepted it" and "I deleted my copy", the resend after a restart is
///     recognised and ignored rather than delivered twice;
///   - if the persistence server is down or unreachable, nothing is lost:
///     the rows stay queued and this tries again a moment later. Players
///     just see their mail arrive a little late.
///
/// A sale doesn't wait for the next lap: it nudges this awake (MailSignal),
/// so mail normally arrives within milliseconds of the sale.
///
/// One instance only. Two dispatchers would still never double-deliver
/// (same keys), but they'd waste each other's work.
/// </summary>
public class MailDispatcher : BackgroundService
{
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _http;
    private readonly MailSignal _signal;
    private readonly ILogger<MailDispatcher> _log;
    private readonly TimeSpan _interval;

    public MailDispatcher(IServiceScopeFactory scopes, IHttpClientFactory http, MailSignal signal,
                          ILogger<MailDispatcher> log, IConfiguration config)
    {
        _scopes = scopes;
        _http = http;
        _signal = signal;
        _log = log;
        _interval = TimeSpan.FromSeconds(Math.Max(1, config.GetValue("Auction:MailDispatchSeconds", 2)));
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        bool down = false;

        while (!stop.IsCancellationRequested)
        {
            int delivered = 0;

            _signal.PassStarted();

            try
            {
                delivered = await DeliverBatchAsync(stop);

                if (down)
                {
                    _log.LogInformation("[Mail] The persistence server is answering again; queued mail is flowing.");
                    down = false;
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log once per outage, not once per retry.
                if (!down)
                {
                    _log.LogWarning("[Mail] Can't reach the persistence server ({Message}). Mail is safe in the outbox " +
                                    "and will be retried. If this says 404, check Persistence:InternalSecret.", ex.Message);
                    down = true;
                }
            }
            finally
            {
                // Even a failed pass counts: anyone waiting on it wants to know it's over.
                _signal.PassFinished();
            }

            // A full batch means there's probably more: go straight back.
            // Otherwise sleep - but a sale wakes this at once (see MailSignal),
            // so the interval is only a safety net for mail that was queued
            // while the persistence server was unreachable.
            if (delivered < BatchSize)
            {
                try { await _signal.WaitForWorkAsync(_interval, stop); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task<int> DeliverBatchAsync(CancellationToken stop)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();

        var rows = await db.MailOutbox
            .Where(m => !m.Failed)
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(stop);

        if (rows.Count == 0)
            return 0;

        var client = _http.CreateClient("persistence");
        int done = 0;

        foreach (var row in rows)
        {
            var request = new W2PMailSendRequest
            {
                DeliveryKey    = row.DeliveryKey,
                CharacterId    = row.CharacterId,
                Sender         = row.Sender,
                Subject        = row.Subject,
                Gold           = row.Gold,
                ItemTemplateId = row.ItemTemplateId,
                ItemQuantity   = row.ItemQuantity
            };

            using var content = new ByteArrayContent(MessagePackSerializer.Serialize(request));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-msgpack");

            // A network error or a non-2xx throws out of here on purpose: the
            // whole batch stops, stays queued, and is retried in order.
            using var response = await client.PostAsync("/mail/send", content, stop);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(stop);
            var result = bytes.Length == 0 ? null : MessagePackSerializer.Deserialize<P2WMailSendResponse>(bytes);

            if (result is { Sent: true })
            {
                db.MailOutbox.Remove(row);
                done++;
            }
            else
            {
                // A definite "no" (the character is gone, say). Retrying
                // won't change it, so park it where an admin can see it
                // instead of blocking the queue behind it.
                string reason = result?.Reason ?? "refused";

                row.Failed = true;
                row.FailureReason = reason.Length > 256 ? reason[..256] : reason;

                _log.LogError("[Mail] UNDELIVERABLE: {Gold}g + {Qty}x item {Item} for character {Character} " +
                              "('{Subject}'): {Reason}. Parked in mail_outbox.",
                              row.Gold, row.ItemQuantity, row.ItemTemplateId, row.CharacterId, row.Subject, row.FailureReason);
            }

            // Save per row: the moment they've accepted it, forget it.
            await db.SaveChangesAsync(stop);
        }

        return done;
    }
}
