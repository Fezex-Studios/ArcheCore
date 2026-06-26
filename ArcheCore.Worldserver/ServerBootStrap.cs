using ArcheCore.Worldserver.Core.Services;
using ArcheCore.Worldserver.Utils.Config;
using ArcheCore.Worldserver.Utils.Database.SQLite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog.Extensions.Logging;

namespace ArcheCore.Worldserver;

public static class ServerBootStrap
{
    
    public static IHostBuilder UseWorldServer(this IHostBuilder builder)
    
    {
        
        builder.ConfigureLogging(logging =>
        {
            
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Trace); // let NLog rules decide, not this
            logging.AddFilter("Microsoft", LogLevel.Warning);
            logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
            logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Error);
            logging.AddNLog("nlog.config");
        });

        builder.ConfigureServices((context, services) =>
        {
            services.AddDbContext<WorldDataDbContext>((sp,options) =>
            {
                var dbConfig = sp.GetRequiredService<IOptions<DatabaseConfig>>().Value;
                options.UseSqlite($"Data Source={dbConfig.WorldDb}");
                options.UseLoggerFactory(LoggerFactory.Create(b => b.AddFilter(_ => false))); // silence EF entirely
                options.EnableSensitiveDataLogging(false);
            });

            services.AddSingleton<QuestManager>();

            services.Configure<LoggingConfig>(
                context.Configuration.GetSection("Logging"));

            services.Configure<WorldServerConfig>(
                context.Configuration.GetSection("World"));

            services.Configure<NetworkConfig>(
                context.Configuration.GetSection("Network"));

            services.Configure<DatabaseConfig>(
                context.Configuration.GetSection("Database"));

            services.AddHostedService<global::WorldServer>();
            services.AddSingleton<DemoService>();
        });

        return builder;
    }
}