using Microsoft.Extensions.Logging;
using NLog;

namespace ArcheCore.Server.World.Core.Services;

public class DemoService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public DemoService()
    {
       
    }

    public Task RunService()
    {
        Logger.Info("Started DemoService!");
        return Task.CompletedTask;
    }
}