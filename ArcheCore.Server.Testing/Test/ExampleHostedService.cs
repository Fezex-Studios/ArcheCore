using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AppLifetime.Example;

public sealed class ExampleHostedService : IHostedService
{
    private readonly ILogger _logger;

    public ExampleHostedService(
        ILogger<ExampleHostedService> logger
        )
    {
        _logger = logger;

       
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("StartAsync has been called.");
        await Task.CompletedTask;
    }
    
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("7. StopAsync has been called.");

        await Task.CompletedTask;
    }

    
    
}