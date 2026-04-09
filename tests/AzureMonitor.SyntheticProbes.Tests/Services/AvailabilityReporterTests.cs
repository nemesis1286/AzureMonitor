using AzureMonitor.SyntheticProbes.Models;
using AzureMonitor.SyntheticProbes.Services;
using FluentAssertions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AzureMonitor.SyntheticProbes.Tests.Services;

public class AvailabilityReporterTests : IDisposable
{
    private readonly InMemoryTelemetryChannel _channel;
    private readonly TelemetryConfiguration _telemetryConfig;
    private readonly AvailabilityReporter _reporter;

    public AvailabilityReporterTests()
    {
        _channel = new InMemoryTelemetryChannel();
        _telemetryConfig = new TelemetryConfiguration
        {
            TelemetryChannel = _channel,
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000"
        };

        var config = Options.Create(new ProbeConfiguration
        {
            RunLocation = "Test-Location"
        });

        _reporter = new AvailabilityReporter(_telemetryConfig, config, new NullLogger<AvailabilityReporter>());
    }

    [Fact]
    public void Report_HttpProbe_TracksAvailabilityWithCorrectFields()
    {
        var endpoint = new ProbeEndpoint
        {
            Name = "Test API",
            ProbeType = ProbeType.Http,
            Url = "https://api.internal.com/health"
        };

        var result = new ProbeResult
        {
            EndpointName = "Test API",
            Success = true,
            Duration = TimeSpan.FromMilliseconds(250),
            Message = "HTTP 200 OK",
            StatusCode = 200,
            Timestamp = DateTimeOffset.UtcNow
        };

        _reporter.Report(endpoint, result);

        _channel.Items.Should().ContainSingle();
        var telemetry = _channel.Items[0] as AvailabilityTelemetry;
        telemetry.Should().NotBeNull();
        telemetry!.Name.Should().Be("Test API");
        telemetry.Success.Should().BeTrue();
        telemetry.Duration.Should().Be(TimeSpan.FromMilliseconds(250));
        telemetry.RunLocation.Should().Be("Test-Location");
        telemetry.Message.Should().Be("HTTP 200 OK");
        telemetry.Properties["ProbeType"].Should().Be("Http");
        telemetry.Properties["Url"].Should().Be("https://api.internal.com/health");
        telemetry.Properties["StatusCode"].Should().Be("200");
        telemetry.Metrics["DurationMs"].Should().Be(250);
    }

    [Fact]
    public void Report_TcpProbe_TracksAvailabilityWithTargetProperty()
    {
        var endpoint = new ProbeEndpoint
        {
            Name = "SQL Server",
            ProbeType = ProbeType.Tcp,
            Host = "sql.internal.com",
            Port = 1433
        };

        var result = new ProbeResult
        {
            EndpointName = "SQL Server",
            Success = true,
            Duration = TimeSpan.FromMilliseconds(15),
            Message = "TCP connection succeeded",
            Timestamp = DateTimeOffset.UtcNow
        };

        _reporter.Report(endpoint, result);

        _channel.Items.Should().ContainSingle();
        var telemetry = _channel.Items[0] as AvailabilityTelemetry;
        telemetry.Should().NotBeNull();
        telemetry!.Properties["ProbeType"].Should().Be("Tcp");
        telemetry.Properties["Target"].Should().Be("sql.internal.com:1433");
    }

    [Fact]
    public void Report_FailedProbe_TracksAvailabilityWithFailure()
    {
        var endpoint = new ProbeEndpoint
        {
            Name = "Failing Endpoint",
            ProbeType = ProbeType.Http,
            Url = "https://bad.internal.com"
        };

        var result = new ProbeResult
        {
            EndpointName = "Failing Endpoint",
            Success = false,
            Duration = TimeSpan.FromSeconds(5),
            Message = "Connection failed: Connection refused",
            Timestamp = DateTimeOffset.UtcNow
        };

        _reporter.Report(endpoint, result);

        var telemetry = _channel.Items[0] as AvailabilityTelemetry;
        telemetry.Should().NotBeNull();
        telemetry!.Success.Should().BeFalse();
        telemetry.Message.Should().Be("Connection failed: Connection refused");
    }

    [Fact]
    public void Report_SetsAvailabilityId()
    {
        var endpoint = new ProbeEndpoint { Name = "Test", ProbeType = ProbeType.Http, Url = "https://test.com" };
        var result = new ProbeResult { Success = true, Duration = TimeSpan.FromMilliseconds(1) };

        _reporter.Report(endpoint, result);

        var telemetry = _channel.Items[0] as AvailabilityTelemetry;
        telemetry.Should().NotBeNull();
        telemetry!.Id.Should().NotBeNullOrEmpty();
    }

    public void Dispose()
    {
        _telemetryConfig.Dispose();
    }
}

/// <summary>
/// In-memory telemetry channel for testing purposes.
/// </summary>
public class InMemoryTelemetryChannel : ITelemetryChannel
{
    public List<ITelemetry> Items { get; } = new();
    public bool? DeveloperMode { get; set; } = true;
    public string? EndpointAddress { get; set; }

    public void Send(ITelemetry item)
    {
        Items.Add(item);
    }

    public void Flush() { }

    public void Dispose() { }
}
