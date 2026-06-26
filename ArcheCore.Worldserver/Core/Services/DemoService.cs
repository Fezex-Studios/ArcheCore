using Microsoft.Extensions.Logging;

namespace ArcheCore.Worldserver.Core.Services;

public class DemoService
{
    private readonly ILogger<DemoService> _logger;
    public DemoService(ILogger<DemoService> logger)
    {
        _logger = logger;
    }

    public Task RunService()
    {
        _logger.LogInformation("Started DemoService!");
        return Task.CompletedTask;
    }
}