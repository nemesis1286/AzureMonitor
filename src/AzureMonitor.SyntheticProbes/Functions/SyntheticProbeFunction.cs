using AzureMonitor.SyntheticProbes.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AzureMonitor.SyntheticProbes.Functions;

public class SyntheticProbeFunction
{
    private readonly IProbeOrchestrator _orchestrator;
    private readonly ILogger<SyntheticProbeFunction> _logger;

    public SyntheticProbeFunction(IProbeOrchestrator orchestrator, ILogger<SyntheticProbeFunction> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    [Function("SyntheticProbeFunction")]
    public async Task Run(
        [TimerTrigger("%PROBE_SCHEDULE%")] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Synthetic probe function triggered at {Time}", DateTimeOffset.UtcNow);

        if (timerInfo.IsPastDue)
        {
            _logger.LogWarning("Timer trigger is past due, execution may have been delayed");
        }

        await _orchestrator.ExecuteAllProbesAsync(cancellationToken);

        _logger.LogInformation("Synthetic probe function completed. Next scheduled run: {NextRun}",
            timerInfo.ScheduleStatus?.Next);
    }
}
