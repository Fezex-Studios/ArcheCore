using AppLifetime.Example;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public static class ExampleBootstrap
{
    public static HostApplicationBuilder UseExampleService(this HostApplicationBuilder builder)
    {
        // Logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        // Services
        builder.Services.AddHostedService<ExampleHostedService>();

        return builder;
    }
}