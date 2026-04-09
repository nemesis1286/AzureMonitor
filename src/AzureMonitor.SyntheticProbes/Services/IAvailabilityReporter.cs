using AzureMonitor.SyntheticProbes.Models;

namespace AzureMonitor.SyntheticProbes.Services;

public interface IAvailabilityReporter
{
    void Report(ProbeEndpoint endpoint, ProbeResult result);
    void Flush();
}
