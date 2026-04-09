namespace AzureMonitor.SyntheticProbes.Models;

public enum ProbeType
{
    Http,
    Tcp
}

public class ProbeEndpoint
{
    public string Name { get; set; } = string.Empty;
    public ProbeType ProbeType { get; set; } = ProbeType.Http;

    // HTTP probe properties
    public string? Url { get; set; }
    public int ExpectedStatusCode { get; set; } = 200;
    public Dictionary<string, string>? Headers { get; set; }
    public string? ExpectedResponseBodyContains { get; set; }

    // TCP probe properties
    public string? Host { get; set; }
    public int Port { get; set; }

    // Common
    public int TimeoutSeconds { get; set; } = 30;
}
