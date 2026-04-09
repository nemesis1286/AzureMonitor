using AzureMonitor.SyntheticProbes.Models;

namespace AzureMonitor.SyntheticProbes.Services;

public interface IHealthProbe
{
    ProbeType SupportedType { get; }
    Task<ProbeResult> ExecuteAsync(ProbeEndpoint endpoint, CancellationToken cancellationToken);
}
