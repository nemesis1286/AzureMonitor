using System.Diagnostics;
using System.Net.Sockets;
using AzureMonitor.SyntheticProbes.Models;
using Microsoft.Extensions.Logging;

namespace AzureMonitor.SyntheticProbes.Services;

public class TcpHealthProbe : IHealthProbe
{
    private readonly ILogger<TcpHealthProbe> _logger;

    public ProbeType SupportedType => ProbeType.Tcp;

    public TcpHealthProbe(ILogger<TcpHealthProbe> logger)
    {
        _logger = logger;
    }

    public async Task<ProbeResult> ExecuteAsync(ProbeEndpoint endpoint, CancellationToken cancellationToken)
    {
        var result = new ProbeResult
        {
            EndpointName = endpoint.Name,
            Timestamp = DateTimeOffset.UtcNow
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(endpoint.TimeoutSeconds));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Probing TCP endpoint {EndpointName} at {Host}:{Port}",
                endpoint.Name, endpoint.Host, endpoint.Port);

            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(endpoint.Host!, endpoint.Port, timeoutCts.Token);

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.Success = true;
            result.Message = $"TCP connection to {endpoint.Host}:{endpoint.Port} succeeded";

            _logger.LogInformation("Probe {EndpointName} succeeded in {Duration}ms",
                endpoint.Name, result.Duration.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.Success = false;
            result.Message = $"TCP connection to {endpoint.Host}:{endpoint.Port} timed out after {endpoint.TimeoutSeconds}s";
            _logger.LogWarning("Probe {EndpointName} timed out after {Timeout}s", endpoint.Name, endpoint.TimeoutSeconds);
        }
        catch (SocketException ex)
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.Success = false;
            result.Message = $"TCP connection to {endpoint.Host}:{endpoint.Port} failed: {ex.Message}";
            _logger.LogError(ex, "Probe {EndpointName} TCP connection failed", endpoint.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.Success = false;
            result.Message = $"Unexpected error: {ex.Message}";
            _logger.LogError(ex, "Probe {EndpointName} unexpected error", endpoint.Name);
        }

        return result;
    }
}
