using System.Net.Http.Json;

namespace ArcheCore.LoadTester
{
    /// <summary>
    /// How a bot gets a valid session token before it can send Authenticate
    /// to the world server. Two implementations because the two things you
    /// actually want to measure are different:
    ///
    ///   - HttpLoginTokenSource exercises the REAL path: AuthServer login,
    ///     over HTTP, same as a real client. This is what you run before a
    ///     launch, because it also load-tests AuthServer, and because it's
    ///     the only mode that proves the full pipeline works end to end.
    ///
    ///   - PregeneratedTokenSource reads tokens from a file you seeded
    ///     ahead of time. Use this when you specifically want to isolate
    ///     WORLD SERVER capacity from AUTH SERVER capacity - e.g. you
    ///     already know auth can handle it and you don't want its latency
    ///     or rate limiting showing up in your world-server numbers.
    ///
    /// Start with HttpLogin. Only reach for Pregenerated once you have a
    /// specific reason to separate the two.
    /// </summary>
    public interface ITokenSource
    {
        Task<string> GetTokenAsync(int botIndex, CancellationToken ct);
    }

    /// <summary>
    /// Logs in against the real AuthServer HTTP endpoint. ADJUST THE ROUTE
    /// AND PAYLOAD SHAPE to match your actual AuthServer controller - this
    /// assumes a conventional POST {username, password} -> {token} login
    /// endpoint. Grep your Auth server project for the route attribute on
    /// the login action and fix LoginPath/BuildLoginBody below to match.
    ///
    /// Expects test accounts to already exist (loadtest_bot_0001 style) -
    /// seed them the same way your real signup flow creates accounts, or
    /// add a seed script to AuthServer's own repo. Not this tester's job.
    /// </summary>
    public sealed class HttpLoginTokenSource : ITokenSource
    {
        private readonly HttpClient _http;
        private readonly string _usernamePattern;
        private readonly string _password;
        private readonly string _loginPath;

        public HttpLoginTokenSource(
            string authServerBaseUrl,
            string usernamePattern = "loadtest_bot_{0:0000}",
            string password = "LoadTest!12345",
            string loginPath = "/login")
        {
            _http = new HttpClient { BaseAddress = new Uri(authServerBaseUrl) };
            _usernamePattern = usernamePattern;
            _password = password;
            _loginPath = loginPath;
        }

        public async Task<string> GetTokenAsync(int botIndex, CancellationToken ct)
        {
            var username = string.Format(_usernamePattern, botIndex);

            var response = await _http.PostAsJsonAsync(
                _loginPath,
                new { Username = username, Password = _password },
                ct);

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct);

            if (body?.Token is null)
                throw new InvalidOperationException($"Login succeeded but no token in response for {username}");

            return body.Token;
        }

        private sealed class LoginResponse
        {
            public string? Token { get; set; }
        }
    }

    /// <summary>
    /// One token per line, in order. Generate this list however you issue
    /// tokens outside the normal login flow - e.g. a small admin script
    /// that inserts sessions directly.
    /// </summary>
    public sealed class PregeneratedTokenSource : ITokenSource
    {
        private readonly string[] _tokens;

        public PregeneratedTokenSource(string filePath)
        {
            _tokens = File.ReadAllLines(filePath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();
        }

        public Task<string> GetTokenAsync(int botIndex, CancellationToken ct)
        {
            if (botIndex >= _tokens.Length)
                throw new InvalidOperationException(
                    $"Requested token for bot {botIndex} but only {_tokens.Length} tokens were provided.");

            return Task.FromResult(_tokens[botIndex]);
        }
    }
}
