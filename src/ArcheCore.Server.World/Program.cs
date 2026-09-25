using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ArcheCore.Server.World;

public class Program
{
    public static void Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // Secrets (audit gap 3): the real InternalSecret lives in
        // appsettings.Local.json (git-ignored; copy the .example file) or in
        // environment variables (World__InternalSecret), never in the
        // committed appsettings.json. Env vars re-added last so they win.
        builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
        builder.Configuration.AddEnvironmentVariables();

        builder
            .UseWorldServer()
            .AddDemoBootstrap();    

        var host = builder.Build();
        host.Run();
    }
}