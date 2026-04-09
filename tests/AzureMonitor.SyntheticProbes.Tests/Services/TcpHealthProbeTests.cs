using System.Net;
using System.Net.Sockets;
using AzureMonitor.SyntheticProbes.Models;
using AzureMonitor.SyntheticProbes.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AzureMonitor.SyntheticProbes.Tests.Services;

public class TcpHealthProbeTests
{
    private readonly TcpHealthProbe _probe = new(new NullLogger<TcpHealthProbe>());

    [Fact]
    public async Task ExecuteAsync_OpenPort_ReturnsSuccess()
    {
        // Start a TCP listener on a random port
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var endpoint = new ProbeEndpoint
            {
                Name = "Test TCP",
                ProbeType = ProbeType.Tcp,
                Host = "127.0.0.1",
                Port = port,
                TimeoutSeconds = 5
            };

            var result = await _probe.ExecuteAsync(endpoint, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.EndpointName.Should().Be("Test TCP");
            result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
            result.Message.Should().Contain("succeeded");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ExecuteAsync_ClosedPort_ReturnsFailure()
    {
        // Use a port that is very unlikely to be open
        var endpoint = new ProbeEndpoint
        {
            Name = "Closed Port Test",
            ProbeType = ProbeType.Tcp,
            Host = "127.0.0.1",
            Port = 19999,
            TimeoutSeconds = 3
        };

        var result = await _probe.ExecuteAsync(endpoint, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("failed");
    }

    [Fact]
    public void SupportedType_ReturnsTcp()
    {
        _probe.SupportedType.Should().Be(ProbeType.Tcp);
    }
}
