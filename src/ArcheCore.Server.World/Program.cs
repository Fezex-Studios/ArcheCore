using Microsoft.Extensions.Hosting;

namespace ArcheCore.Server.World;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder
            .UseWorldServer()
            .AddDemoBootstrap();

        var host = builder.Build();
        host.Run();
    }
}