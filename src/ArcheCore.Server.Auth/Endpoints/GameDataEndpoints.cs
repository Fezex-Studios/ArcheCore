using ArcheCore.Server.Auth.Contracts;
using ArcheCore.Server.Auth.Services;

namespace ArcheCore.Server.Auth.Endpoints;

/// <summary>
/// Port of GameDataRoute.ts. Both routes are exempt from rate limiting, as
/// before — the launcher uses /gamedata/version as its "is the server up?"
/// check (see checkServerOnline in login.api.ts), and rate-limiting a
/// health check makes the launcher report the server as down whenever
/// someone is being noisy elsewhere.
/// </summary>
public static class GameDataEndpoints
{
    public static void MapGameData(this IEndpointRouteBuilder app)
    {
        // Returns the current file hash so the client can decide whether it
        // needs to download.
        app.MapGet("/gamedata/version", (GameDataProvider gameData) =>
        {
            var hash = gameData.GetHash();

            if (hash is null)
            {
                return Results.Json(
                    new ErrorResponse { Error = "gamedata.bin not found on server" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new GameDataVersionResponse { Hash = hash });
        });

        // Streams the raw file.
        app.MapGet("/gamedata/db", (GameDataProvider gameData) =>
        {
            if (!gameData.Exists)
            {
                return Results.Json(
                    new ErrorResponse { Error = "gamedata.bin not found on server" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            // Results.File streams from disk rather than buffering the
            // whole thing into memory, and handles range requests, so a
            // launcher download that drops at 90% can resume instead of
            // starting over.
            return Results.File(
                gameData.Path,
                contentType: "application/octet-stream",
                fileDownloadName: "gamedata.bin",
                enableRangeProcessing: true);
        });
    }
}
