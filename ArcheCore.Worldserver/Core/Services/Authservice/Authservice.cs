
using System.Text;
using ArcheCore.Worldserver.Utils.Config;
using Newtonsoft.Json;
using NLog;


namespace Shared.AuthService
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

        
        public AuthService(HttpClient http)
        {
            _http = http;
            
        }
        
        
        private class ValidateResponse
        {
            [JsonProperty("Valid")]
            public bool Valid;

            [JsonProperty("AccountId")]
            public int AccountId;
        }

        public  async Task<int> ValidateToken(string token)
        {
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