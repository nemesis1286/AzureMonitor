using AzureMonitor.SyntheticProbes.Functions;
using AzureMonitor.SyntheticProbes.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AzureMonitor.SyntheticProbes.Tests.Functions;

public class SyntheticProbeFunctionTests
{
    [Fact]
    public async Task Run_CallsOrchestratorExecuteAllProbes()
    {
        var orchestrator = new Mock<IProbeOrchestrator>();
        var logger = new NullLogger<SyntheticProbeFunction>();
        var function = new SyntheticProbeFunction(orchestrator.Object, logger);

        var timerInfo = CreateTimerInfo(isPastDue: false);

        await function.Run(timerInfo, CancellationToken.None);

        orchestrator.Verify(o => o.ExecuteAllProbesAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Run_PastDue_StillExecutes()
    {
        var orchestrator = new Mock<IProbeOrchestrator>();
        var logger = new NullLogger<SyntheticProbeFunction>();
        var function = new SyntheticProbeFunction(orchestrator.Object, logger);

        var timerInfo = CreateTimerInfo(isPastDue: true);

        await function.Run(timerInfo, CancellationToken.None);

        orchestrator.Verify(o => o.ExecuteAllProbesAsync(CancellationToken.None), Times.Once);
    }

    private static TimerInfo CreateTimerInfo(bool isPastDue)
    {
        return new TimerInfo
        {
            IsPastDue = isPastDue,
            ScheduleStatus = new ScheduleStatus
            {
                Last = DateTime.UtcNow.AddMinutes(-5),
                Next = DateTime.UtcNow.AddMinutes(5),
                LastUpdated = DateTime.UtcNow
            }
        };
    }
}
