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

public static class ServerBootStrap
{
    public static IHostBuilder UseWorldServer(this IHostBuilder builder)
    {
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddNLog("nlog.config");
        });

        builder.ConfigureServices((context, services) =>
        {
            services.AddDbContextFactory<WorldDataDbContext>((sp, options) =>
            {
                var dbConfig = sp.GetRequiredService<IOptions<DatabaseConfig>>().Value;
                options.UseSqlite($"Data Source={dbConfig.WorldDb}");
                options.UseLoggerFactory(LoggerFactory.Create(b => b.AddFilter(_ => false)));
                options.EnableSensitiveDataLogging(false);
            });

            services.Configure<WorldServerConfig>(context.Configuration.GetSection("World"));
            services.Configure<NetworkConfig>(context.Configuration.GetSection("Network"));
            services.Configure<DatabaseConfig>(context.Configuration.GetSection("Database"));

            services.AddSingleton<GameDataPatchRunner>();
            services.AddHostedService<WorldServer>();

            services.AddSingleton<DemoService>();
            services.AddHttpClient();
            services.AddSingleton<AuthService>();
            services.AddSingleton<QuestManager>();
        });

        return builder;
    }
}