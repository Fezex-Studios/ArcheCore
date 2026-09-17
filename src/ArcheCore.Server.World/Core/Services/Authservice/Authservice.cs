using System.Text;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using NLog;
using System.Net.Http;
using ArcheCore.Server.World.Utils.Config;
using Microsoft.Extensions.Http;


namespace ArcheCore.Server.World.Core.Services.Authservice
{
    /// <summary>
    /// Calls the Auth Server's /validate-session endpoint.
    /// Returns the AccountId on success, or -1 if the token is invalid/expired.
    ///
    /// Requires INTERNAL_SECRET in ServerConfig.json to match the Auth Server's
    /// INTERNAL_SECRET environment variable.
    /// </summary>
    public class AuthService
    {
        private readonly HttpClient _http;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly WorldServerConfig _worldConfig;

        
        public AuthService(IHttpClientFactory httpClientFactory, IOptions<WorldServerConfig> worldConfig)
        {
            _http = httpClientFactory.CreateClient();
            _worldConfig = worldConfig.Value;
        }
        
        
        private class ValidateResponse
        {
            [JsonProperty("Valid")]
            public bool Valid;

            [JsonProperty("AccountId")]
            public int AccountId;
        }

        // Bots load-tested locally never need a real account row, a real
        // login, or a real session — only a stable, unique AccountId the
        // rest of the pipeline (character list / select / create) can key
        // off. The 900000 offset keeps these out of your real account id
        // range so they can never collide with or masquerade as a real
        // player, and the prefix makes them trivially greppable in logs.
        private const string LoadTestTokenPrefix = "loadtest:";
        private const int LoadTestAccountIdOffset = 900_000;

        public async Task<int> ValidateToken(string token)
        {
            if (_worldConfig.AllowLoadTestBypass
                && token is not null
                && token.StartsWith(LoadTestTokenPrefix, StringComparison.Ordinal))
            {
                var suffix = token.AsSpan(LoadTestTokenPrefix.Length);
                if (int.TryParse(suffix, out var botIndex))
                {
                    var accountId = LoadTestAccountIdOffset + botIndex;
                    Logger.Warn($"[AuthService] LOAD-TEST BYPASS active — token '{token}' -> AccountId={accountId}. " +
                                 "AllowLoadTestBypass must be false outside a load test.");
                    return accountId;
                }
            }

            try
            {
                string url  = $"{_worldConfig.AuthServerUrl}/validate-session";
                string body = JsonConvert.SerializeObject(new { Token = token });

                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };

                // Internal secret — must match INTERNAL_SECRET in the Auth Server .env
                request.Headers.Add("x-internal-secret", _worldConfig.InternalSecret);

                HttpResponseMessage response = await _http.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Error($"[AuthService] Validate request failed with status {response.StatusCode}");
                    return -1;
                }

                string json = await response.Content.ReadAsStringAsync();
                Logger.Info($"[AuthService] Raw response: {json}");

                var result = JsonConvert.DeserializeObject<ValidateResponse>(json);

                if (result == null)
                {
                    Logger.Error("[AuthService] Empty or malformed response from auth server");
                    return -1;
                }

                return result.Valid ? result.AccountId : -1;
            }
            catch (Exception e)
            {
               Logger.Error($"[AuthService] Validation request failed: {e.Message}");
                return -1;
            }
        }
    }
}