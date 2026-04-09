namespace AzureMonitor.SyntheticProbes.Models;

public class ProbeConfiguration
{
    public const string SectionName = "ProbeConfiguration";

    public List<ProbeEndpoint> Endpoints { get; set; } = new();
    public string RunLocation { get; set; } = "Unknown";
    public int DefaultTimeoutSeconds { get; set; } = 30;
}
