using Microsoft.Extensions.Hosting;

namespace ArcheCore.Server.World;

public class Program
{
    static void Main(string[] args)
    {
        Host.CreateDefaultBuilder(args)
            .UseWorldServer()
            .Build()
            .Run();
    }
}