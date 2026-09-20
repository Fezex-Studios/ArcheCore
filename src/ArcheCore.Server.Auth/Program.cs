using System.Threading.RateLimiting;
using ArcheCore.Server.Auth.Config;
using ArcheCore.Server.Auth.Data;
using ArcheCore.Server.Auth.Endpoints;
using ArcheCore.Server.Auth.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Config ───────────────────────────────────────────────────────────
// Replaces ServerConfig.ts + dotenv + the .env file. Same appsettings.json
// shape as the World and Persistence servers, so the shared secret now
// lives in three files of the same format instead of two plus a .env you
// had to open a different editor to reach.
builder.Services.Configure<AuthServerConfig>(builder.Configuration.GetSection("Auth"));

var config = builder.Configuration.GetSection("Auth").Get<AuthServerConfig>()
             ?? new AuthServerConfig();

// Fail fast, before anything binds a socket.
config.Validate();

var connectionString = builder.Configuration.GetConnectionString("Auth")
    ?? throw new InvalidOperationException(
        "Missing 'Auth' connection string. Add it under ConnectionStrings in " +
        "appsettings.json, pointing at the archecore_auth database.");

// ── JSON ─────────────────────────────────────────────────────────────
// CRITICAL. ASP.NET Core serializes camelCase by default. The launcher
// (login.api.ts) reads data.Success / data.Token / data.Message and the
// WorldServer's AuthService reads Valid / AccountId — all PascalCase. A
// camelCase response would come back 200 OK with every field reading as
// null, which looks like "invalid credentials" rather than like a bug.
// Null naming policy means "use the property name exactly as declared".
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

// ── Database ─────────────────────────────────────────────────────────
// Same provider and connection style as the persistence server. Point
// this at its OWN database (archecore_auth), not the persistence one:
// password hashes and character rows have no reason to share a blast
// radius, and it lets you give this server a MySQL user with no access
// to anything else.
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// ── Services ─────────────────────────────────────────────────────────
// PasswordService is a singleton because it computes the timing-defense
// dummy hash once in its constructor; making it transient would pay that
// ~250ms bcrypt cost on every single request that resolves it.
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<GameDataProvider>();
builder.Services.AddHostedService<SessionPurgeService>();

// ── CORS ─────────────────────────────────────────────────────────────
// Matches the old `origin: true, methods: ["GET", "POST"]`: reflect
// whatever origin asked. The launcher is a Tauri webview whose origin
// varies by platform and build, which is why this is permissive. No
// credentials are used, so this can't be combined into a
// cookie-stealing shape.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .SetIsOriginAllowed(_ => true)
        .AllowAnyHeader()
        .WithMethods("GET", "POST"));
});

// ── Rate limiting ────────────────────────────────────────────────────
// Replaces @fastify/rate-limit. Two layers, same numbers as before:
//   - global 10/minute per IP
//   - "auth-strict" 5/minute on /login and /register
// Both apply to those two routes, so the effective limit there is 5.
builder.Services.AddRateLimiter(options =>
{
    // The launcher special-cases 429 with a real message ("Too many
    // attempts. Wait a minute and try again."), so the status code
    // matters to the UI, not just to the protocol.
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // /validate-session is WorldServer-to-AuthServer and already
        // throttled per-peer on the WorldServer side; rate-limiting it
        // here would throttle the whole shard through one IP. /gamedata
        // is the launcher's health check and its download route — see
        // GameDataEndpoints.
        if (path.StartsWith("/validate-session", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/gamedata", StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitPartition.GetNoLimiter("exempt");
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window      = TimeSpan.FromMinutes(1)
            });
    });

    options.AddPolicy("auth-strict", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window      = TimeSpan.FromMinutes(1)
            }));
});

var app = builder.Build();

// ── Schema ───────────────────────────────────────────────────────────
// EF migrations, from day one, and nothing else. This project starts on a
// fresh MySQL database with no legacy schema to accommodate, so there is
// no reason to repeat the world server's mistake of having two systems
// own one database. If you need a column, add a migration.
//
// You must generate the initial migration before the first run:
//     dotnet ef migrations add InitialCreate --project src/ArcheCore.Server.Auth
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("Database Ready");
}

app.UseCors();
app.UseRateLimiter();

app.MapRegister();
app.MapLogin();
app.MapValidateSession();
app.MapGameData();

app.Logger.LogInformation("Auth Server Ready");

app.Run();

// Partition key for the rate limiter. Prefers X-Forwarded-For so the limit
// still works per-user once this sits behind a reverse proxy; falls back to
// the socket address. Anonymous clients that present neither share one
// bucket, which is the safe direction to fail.
static string ClientKey(HttpContext context)
{
    var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();

    if (!string.IsNullOrWhiteSpace(forwarded))
        return forwarded.Split(',')[0].Trim();

    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
