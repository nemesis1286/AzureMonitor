using System.Diagnostics;
using AzureMonitor.SyntheticProbes.Models;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureMonitor.SyntheticProbes.Services;

public class AvailabilityReporter : IAvailabilityReporter
{
    private readonly TelemetryClient _telemetryClient;
    private readonly string _runLocation;
    private readonly ILogger<AvailabilityReporter> _logger;

    public AvailabilityReporter(
        TelemetryConfiguration telemetryConfiguration,
        IOptions<ProbeConfiguration> probeConfig,
        ILogger<AvailabilityReporter> logger)
    {
        _telemetryClient = new TelemetryClient(telemetryConfiguration);
        _runLocation = probeConfig.Value.RunLocation;
        _logger = logger;
    }

    public void Report(ProbeEndpoint endpoint, ProbeResult result)
    {
        var availability = new AvailabilityTelemetry
        {
            Name = endpoint.Name,
            Timestamp = result.Timestamp,
            Duration = result.Duration,
            RunLocation = _runLocation,
            Success = result.Success,
            Message = result.Message ?? string.Empty
        };

        // Set distributed tracing context
        var currentActivity = Activity.Current;
        if (currentActivity != null)
        {
            availability.Context.Operation.ParentId = currentActivity.SpanId.ToString();
            availability.Context.Operation.Id = currentActivity.RootId;
        }

        using (var activity = new Activity("AvailabilityContext"))
        {
            activity.Start();
            availability.Id = Activity.Current?.SpanId.ToString();
        }

        // Custom properties for richer telemetry
        availability.Properties["ProbeType"] = endpoint.ProbeType.ToString();

        if (endpoint.ProbeType == ProbeType.Http)
        {
            availability.Properties["Url"] = endpoint.Url ?? string.Empty;
            if (result.StatusCode.HasValue)
            {
                availability.Properties["StatusCode"] = result.StatusCode.Value.ToString();
            }
        }
        else if (endpoint.ProbeType == ProbeType.Tcp)
        {
            availability.Properties["Target"] = $"{endpoint.Host}:{endpoint.Port}";
        }

        // Custom metrics
        availability.Metrics["DurationMs"] = result.Duration.TotalMilliseconds;

        _telemetryClient.TrackAvailability(availability);

        _logger.LogInformation(
            "Reported availability for {EndpointName}: Success={Success}, Duration={Duration}ms",
            endpoint.Name, result.Success, result.Duration.TotalMilliseconds);
    }

    public void Flush()
    {
        _telemetryClient.Flush();
    }
}
