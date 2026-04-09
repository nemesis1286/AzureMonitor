using AzureMonitor.SyntheticProbes.Models;
using AzureMonitor.SyntheticProbes.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AzureMonitor.SyntheticProbes.Tests.Services;

public class ProbeOrchestratorTests
{
    private readonly Mock<IAvailabilityReporter> _reporter = new();
    private readonly NullLogger<ProbeOrchestrator> _logger = new();

    private ProbeOrchestrator CreateOrchestrator(
        List<ProbeEndpoint> endpoints,
        params IHealthProbe[] probes)
    {
        var config = Options.Create(new ProbeConfiguration
        {
            Endpoints = endpoints,
            RunLocation = "Test",
            DefaultTimeoutSeconds = 30
        });

        return new ProbeOrchestrator(probes, _reporter.Object, config, _logger);
    }

    [Fact]
    public async Task ExecuteAllProbesAsync_AllEndpointsProbed()
    {
        var endpoints = new List<ProbeEndpoint>
        {
            new() { Name = "HTTP 1", ProbeType = ProbeType.Http, Url = "https://test1.com" },
            new() { Name = "HTTP 2", ProbeType = ProbeType.Http, Url = "https://test2.com" },
            new() { Name = "TCP 1", ProbeType = ProbeType.Tcp, Host = "db.local", Port = 1433 }
        };

        var httpProbe = new Mock<IHealthProbe>();
        httpProbe.Setup(p => p.SupportedType).Returns(ProbeType.Http);
        httpProbe.Setup(p => p.ExecuteAsync(It.IsAny<ProbeEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProbeResult { Success = true, Duration = TimeSpan.FromMilliseconds(100) });

        var tcpProbe = new Mock<IHealthProbe>();
        tcpProbe.Setup(p => p.SupportedType).Returns(ProbeType.Tcp);
        tcpProbe.Setup(p => p.ExecuteAsync(It.IsAny<ProbeEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProbeResult { Success = true, Duration = TimeSpan.FromMilliseconds(50) });

        var orchestrator = CreateOrchestrator(endpoints, httpProbe.Object, tcpProbe.Object);

        await orchestrator.ExecuteAllProbesAsync(CancellationToken.None);

        httpProbe.Verify(p => p.ExecuteAsync(It.IsAny<ProbeEndpoint>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        tcpProbe.Verify(p => p.ExecuteAsync(It.IsAny<ProbeEndpoint>(), It.IsAny<CancellationToken>()), Times.Once);
        _reporter.Verify(r => r.Report(It.IsAny<ProbeEndpoint>(), It.IsAny<ProbeResult>()), Times.Exactly(3));
        _reporter.Verify(r => r.Flush(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAllProbesAsync_EmptyEndpoints_LogsWarning()
    {
        var orchestrator = CreateOrchestrator(new List<ProbeEndpoint>());

        await orchestrator.ExecuteAllProbesAsync(CancellationToken.None);

        _reporter.Verify(r => r.Report(It.IsAny<ProbeEndpoint>(), It.IsAny<ProbeResult>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAllProbesAsync_ProbeFailure_DoesNotBlockOthers()
    {
        var endpoints = new List<ProbeEndpoint>
        {
            new() { Name = "Failing", ProbeType = ProbeType.Http, Url = "https://fail.com" },
            new() { Name = "Succeeding", ProbeType = ProbeType.Http, Url = "https://ok.com" }
        };

        var callCount = 0;
        var httpProbe = new Mock<IHealthProbe>();
        httpProbe.Setup(p => p.SupportedType).Returns(ProbeType.Http);
        httpProbe.Setup(p => p.ExecuteAsync(It.IsAny<ProbeEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new Exception("Simulated failure");
                return new ProbeResult { Success = true };
            });

        var orchestrator = CreateOrchestrator(endpoints, httpProbe.Object);

        await orchestrator.ExecuteAllProbesAsync(CancellationToken.None);

        // Both probes should be attempted, and both results reported
        _reporter.Verify(r => r.Report(It.IsAny<ProbeEndpoint>(), It.IsAny<ProbeResult>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAllProbesAsync_MissingProbeType_ReportsFailure()
    {
        var endpoints = new List<ProbeEndpoint>
        {
            new() { Name = "TCP Endpoint", ProbeType = ProbeType.Tcp, Host = "db.local", Port = 1433 }
        };

        // Only register HTTP probe, not TCP
        var httpProbe = new Mock<IHealthProbe>();
        httpProbe.Setup(p => p.SupportedType).Returns(ProbeType.Http);

        var orchestrator = CreateOrchestrator(endpoints, httpProbe.Object);

        await orchestrator.ExecuteAllProbesAsync(CancellationToken.None);

        _reporter.Verify(r => r.Report(
            It.IsAny<ProbeEndpoint>(),
            It.Is<ProbeResult>(pr => !pr.Success && pr.Message!.Contains("No probe implementation"))),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAllProbesAsync_SetsDefaultTimeout_WhenEndpointTimeoutIsZero()
    {
        var endpoints = new List<ProbeEndpoint>
        {
            new() { Name = "No Timeout", ProbeType = ProbeType.Http, Url = "https://test.com", TimeoutSeconds = 0 }
        };

        ProbeEndpoint? capturedEndpoint = null;
        var httpProbe = new Mock<IHealthProbe>();
        httpProbe.Setup(p => p.SupportedType).Returns(ProbeType.Http);
        httpProbe.Setup(p => p.ExecuteAsync(It.IsAny<ProbeEndpoint>(), It.IsAny<CancellationToken>()))
            .Callback<ProbeEndpoint, CancellationToken>((ep, _) => capturedEndpoint = ep)
            .ReturnsAsync(new ProbeResult { Success = true });

        var orchestrator = CreateOrchestrator(endpoints, httpProbe.Object);

        await orchestrator.ExecuteAllProbesAsync(CancellationToken.None);

        capturedEndpoint.Should().NotBeNull();
        capturedEndpoint!.TimeoutSeconds.Should().Be(30);
    }
}
