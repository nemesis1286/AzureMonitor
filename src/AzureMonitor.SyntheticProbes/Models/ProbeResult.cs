namespace AzureMonitor.SyntheticProbes.Models;

public class ProbeResult
{
    public string EndpointName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public TimeSpan Duration { get; set; }
    public string? Message { get; set; }
    public int? StatusCode { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
