using ArcheCore.Server.World.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ArcheCore.Server.World;

public static class DemoServiceBootstrap
{
    public static HostApplicationBuilder AddDemoBootstrap(this HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<DemoService>();
        return builder;
    }
}