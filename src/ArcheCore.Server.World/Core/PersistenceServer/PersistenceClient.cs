using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using ArcheCore.Server.World.PersistenceServer.Senders;
using ArcheCore.Server.World.Utils.Config;
using MessagePack;
using NLog;

namespace Worldserver.ArcheCore.PersistenceServer.Scripts
{
    public class PersistenceClient : IDisposable
    {
        private const string MsgPackContentType = "application/x-msgpack";

        private readonly HttpClient _http;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // One sender per route — same public shape as before, so nothing
        // calling _persistence.W2PCharacterLoad.Send(...) etc. needs to change.
        public W2PCharacterLoadSender   W2PCharacterLoad   { get; }
        public W2PCharacterCreateSender W2PCharacterCreate { get; }
        public W2PCharacterListSender   W2PCharacterList   { get; }
        public W2PCharacterSaveSender   W2PCharacterSave   { get; }
        public W2PInventorySaveSender   W2PInventorySave   { get; }
        public W2PQuestSaveSender       W2PQuestSave       { get; }
        public W2PConnectSender         W2PConnect         { get; }
        public W2PHelloWorldSender      W2PHelloWorld      { get; }

        public PersistenceClient(WorldServerConfig worldConfig)
        {
            // SocketsHttpHandler gives us pooled, kept-alive connections to
            // Persistence instead of one hand-managed TCP socket — this is
            // the thing that made scaling to 8 shards awkward under raw TCP.
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer  = 20
            };

            _http = new HttpClient(handler)
            {
                BaseAddress = new Uri(worldConfig.PersistenceBaseUrl),
                Timeout     = TimeSpan.FromSeconds(10)
            };

            // Proves to Persistence that this really is the WorldServer.
            // Set here rather than per-send so a route added later can't
            // forget it. Must match InternalSecret in the Persistence
            // server's appsettings.json.
            _http.DefaultRequestHeaders.Add("x-internal-secret", worldConfig.InternalSecret);

            W2PCharacterLoad   = new W2PCharacterLoadSender(this);
            W2PCharacterCreate = new W2PCharacterCreateSender(this);
            W2PCharacterList   = new W2PCharacterListSender(this);
            W2PCharacterSave   = new W2PCharacterSaveSender(this);
            W2PInventorySave   = new W2PInventorySaveSender(this);
            W2PQuestSave       = new W2PQuestSaveSender(this);
            W2PConnect         = new W2PConnectSender(this);
            W2PHelloWorld      = new W2PHelloWorldSender(this);
        }

        public async Task Start()
        {
            await W2PConnect.Send("WorldServer 1 has connected");
            await W2PHelloWorld.Send("Hello THIS IS A MESSAGE SENT FROM THE WORLDSERVER");
        }

        // Request/response pair — Load, Create, List, Connect all use this.
        internal async Task<TResponse> PostAsync<TRequest, TResponse>(
            string route, TRequest payload)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            using var content = BuildContent(payload);
            var serializeMs = sw.Elapsed.TotalMilliseconds;

            var httpResponse = await _http.PostAsync(route, content);
            var sendMs = sw.Elapsed.TotalMilliseconds;

            httpResponse.EnsureSuccessStatusCode();

            await using var stream = await httpResponse.Content.ReadAsStreamAsync();
            var openStreamMs = sw.Elapsed.TotalMilliseconds;

            var result = await MessagePackSerializer.DeserializeAsync<TResponse>(stream);
            var totalMs = sw.Elapsed.TotalMilliseconds;

            Logger.Info(
                $"[PersistenceTiming] {route} | serialize={serializeMs:F1} " +
                $"send={sendMs:F1} openStream={openStreamMs:F1} " +
                $"deserialize={totalMs:F1} (cumulative ms)");

            return result;
        }

        // Reports whether the server actually accepted the request (2xx).
        // Still throws on network errors/timeouts - callers must catch.
        // Used by both W2PCharacterSave and W2PInventorySave.
        internal async Task<bool> PostForStatusAsync<TRequest>(string route, TRequest payload)
        {
            using var content = BuildContent(payload);

            var httpResponse = await _http.PostAsync(route, content);

            if (!httpResponse.IsSuccessStatusCode)
                Logger.Warn($"[PersistenceClient] {route} returned {(int)httpResponse.StatusCode}");

            return httpResponse.IsSuccessStatusCode;
        }

        // Fire-and-forget-style — HelloWorld never returned a body even
        // under TCP. Status code is checked purely for logging.
        internal async Task PostAsync<TRequest>(string route, TRequest payload)
        {
            using var content = BuildContent(payload);

            var httpResponse = await _http.PostAsync(route, content);

            if (!httpResponse.IsSuccessStatusCode)
                Logger.Warn($"[PersistenceClient] {route} returned {(int)httpResponse.StatusCode}");
        }

        private static ByteArrayContent BuildContent<TRequest>(TRequest payload)
        {
            var bytes = MessagePackSerializer.Serialize(payload);
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(MsgPackContentType);
            return content;
        }

        public void Dispose() => _http.Dispose();
    }
}
