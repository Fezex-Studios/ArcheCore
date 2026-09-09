using MessagePack;
using Microsoft.AspNetCore.Http;

namespace ArcheCore.PersistenceServer.Api.Extensions
{
    public static class MsgPackHttpExtensions
    {
        public const string MsgPackContentType = "application/x-msgpack";

        // Returns null on a malformed body instead of throwing, so each
        // endpoint just checks for null rather than wrapping in try/catch.
        public static async Task<T?> ReadMsgPackAsync<T>(this HttpRequest request)
            where T : class
        {
            try
            {
                return await MessagePackSerializer.DeserializeAsync<T>(request.Body);
            }
            catch (MessagePackSerializationException)
            {
                return null;
            }
        }

        public static async Task WriteMsgPackAsync<T>(this HttpResponse response, T value)
        {
            response.ContentType = MsgPackContentType;
            await MessagePackSerializer.SerializeAsync(response.Body, value);
        }
    }
}