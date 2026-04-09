namespace AzureMonitor.SyntheticProbes.Services;

public interface IProbeOrchestrator
{
    Task ExecuteAllProbesAsync(CancellationToken cancellationToken);
}
