# Running ArcheCore safely (audit gap 3)

## Secrets

The real values do **not** go in `appsettings.json`, because that file is committed. Every service also loads `appsettings.Local.json` from its folder, and then environment variables, which override everything.

| Service | Put in `appsettings.Local.json` |
|---|---|
| World | `World:InternalSecret` |
| Auth | `ConnectionStrings:Auth`, `Auth:InternalSecret` |
| Persistence | `ConnectionStrings:Persistence`, `InternalSecret` |
| Auction | `ConnectionStrings:AuctionDb`, `Persistence:InternalSecret` |

- Each service folder has an `appsettings.Local.example.json` to copy.
- `appsettings.Local.json` is listed in each folder's `.gitignore`.
- `InternalSecret` must be **the same value in all four**.
- Generate a new secret with `openssl rand -base64 48`.

The committed `appsettings.json` files hold the placeholder `replace_this_with_a_real_secret`. Every service refuses to start with it, so a missing local file fails loudly instead of running with no secret.

**Rotate the old secret.** The previous one was committed to git, so treat it as public. The 2026-09-25 patch ships `appsettings.Local.json` files with a freshly generated secret already filled in.

The same values can come from environment variables instead:

- World: `World__InternalSecret`
- Auth: `Auth__InternalSecret`
- Persistence: `InternalSecret`
- Auction: `Persistence__InternalSecret`
- Connection strings, for example `ConnectionStrings__Persistence`

## What listens where

| Service | Binds | Who talks to it |
|---|---|---|
| World | UDP 7777, all interfaces | players |
| Auth | `0.0.0.0:3000` | the launcher and the client (public) |
| Persistence | `127.0.0.1:7778` | world server and auction service only |
| Auction | `127.0.0.1:5090` | world server only |

Persistence used to bind `0.0.0.0`, which put the whole character database's API on the network behind one shared secret. If the world server ever runs on a different machine from Persistence, bind Persistence to the private-network address, never a public one.

## TLS for Auth

Auth is the only HTTP service players reach, and passwords go to it. In production, put a reverse proxy in front of it that terminates TLS, and don't expose Kestrel directly.

1. Change the Auth Kestrel URL to `http://127.0.0.1:3000`, so only the proxy can reach it.
2. Put Caddy (automatic Let's Encrypt) or nginx on 443, proxying to `127.0.0.1:3000`.
3. Set `Auth:TrustForwardedFor` to `true`. Only do this behind the proxy: Auth then takes the client address from `X-Forwarded-For`, which the proxy sets. Without a proxy, any client could fake that header.
4. Point the launcher and the client at `https://your-domain`.

Minimal Caddyfile:

```
auth.example.com {
    reverse_proxy 127.0.0.1:3000
}
```

## Login lockout

- **Per username and address:** 5 failed logins (`Auth:MaxLoginAttempts`) lock that username from that address for 15 minutes (`Auth:LockoutMinutes`). Someone typing your name with wrong passwords no longer locks *you* out.
- **Per account:** 50 failures from anywhere (`Auth:AccountLockAttempts`) lock the account everywhere. This is the backstop against a guess spread over many addresses.
- **Rate limits:** unchanged, 10/min per address overall and 5/min on `/login` and `/register`. The address is the socket address unless `TrustForwardedFor` is on.

## Tests

```
ARCHECORE_TEST_DB="Server=127.0.0.1;Port=3306;Database=archecore_test;User=root;Password=...;" \
  dotnet run --project ArcheCore.Tests
```

- The run drops and recreates that database, so the database name must contain `test`.
- It starts the real persistence server itself.
- Exit code 0 means every check passed.
