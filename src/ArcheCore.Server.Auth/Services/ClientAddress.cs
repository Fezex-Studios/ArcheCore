namespace ArcheCore.Server.Auth.Services;

/// <summary>
/// The address a request came from, for rate limits and lockouts.
///
/// X-Forwarded-For is only believed when Auth:TrustForwardedFor is on, i.e.
/// when a reverse proxy that sets it sits in front of Auth. Believing it
/// otherwise let any client pick a fresh fake address per request and step
/// around every per-IP limit.
/// </summary>
public static class ClientAddress
{
    public static string Of(HttpContext context, bool trustForwardedFor)
    {
        if (trustForwardedFor)
        {
            var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
                return forwarded.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
