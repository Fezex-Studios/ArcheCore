using ArcheCore.Server.World.Core.Services;
using ArcheCore.Server.World.Core.Services.Authservice;
using ArcheCore.Server.World.Utils.Config;
using ArcheCore.Server.World.Utils.Database.SQLite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog.Extensions.Logging;
using Shared.AuthService;

namespace ArcheCore.Server.World;

public static class ServerBootstrap
{
    public static HostApplicationBuilder UseWorldServer(this HostApplicationBuilder builder)
    {
        // --------------------
        // LOGGING (NLog)
        // --------------------
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddNLog("nlog.config");

        // --------------------
        // CONFIG
        // --------------------
        builder.Services.Configure<WorldServerConfig>(
            builder.Configuration.GetSection("World"));

        builder.Services.Configure<NetworkConfig>(
            builder.Configuration.GetSection("Network"));

        builder.Services.Configure<DatabaseConfig>(
            builder.Configuration.GetSection("Database"));

        // --------------------
        // DB
        // --------------------
        builder.Services.AddDbContextFactory<WorldDataDbContext>((sp, options) =>
        {
            var dbConfig = sp.GetRequiredService<IOptions<DatabaseConfig>>().Value;

            options.UseSqlite($"Data Source={dbConfig.WorldDb}");

            // WARNING: this disables EF logging completely (same as your old setup)
            options.UseLoggerFactory(LoggerFactory.Create(_ => { }));

            options.EnableSensitiveDataLogging(false);
        });

        // --------------------
        // SERVICES
        // --------------------
        builder.Services.AddSingleton<GameDataPatchRunner>();

        builder.Services.AddHostedService<WorldServer>();

        
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<QuestManager>();

        return builder;
    }
}