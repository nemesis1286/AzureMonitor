using AzureMonitor.SyntheticProbes.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureMonitor.SyntheticProbes.Services;

public class ProbeOrchestrator : IProbeOrchestrator
{
    private readonly IEnumerable<IHealthProbe> _healthProbes;
    private readonly IAvailabilityReporter _reporter;
    private readonly ProbeConfiguration _config;
    private readonly ILogger<ProbeOrchestrator> _logger;
    private static readonly SemaphoreSlim _semaphore = new(10, 10);

    public ProbeOrchestrator(
        IEnumerable<IHealthProbe> healthProbes,
        IAvailabilityReporter reporter,
        IOptions<ProbeConfiguration> config,
        ILogger<ProbeOrchestrator> logger)
    {
        _healthProbes = healthProbes;
        _reporter = reporter;
        _config = config.Value;
        _logger = logger;
    }

    public async Task ExecuteAllProbesAsync(CancellationToken cancellationToken)
    {
        var endpoints = _config.Endpoints;

        if (endpoints.Count == 0)
        {
            _logger.LogWarning("No probe endpoints configured");
            return;
        }

        _logger.LogInformation("Starting synthetic probe execution for {Count} endpoints", endpoints.Count);

        var probeMap = _healthProbes.ToDictionary(p => p.SupportedType);
        var tasks = endpoints.Select(endpoint => ExecuteProbeAsync(endpoint, probeMap, cancellationToken));

        await Task.WhenAll(tasks);

        _reporter.Flush();

        _logger.LogInformation("Completed synthetic probe execution for {Count} endpoints", endpoints.Count);
    }

    private async Task ExecuteProbeAsync(
        ProbeEndpoint endpoint,
        Dictionary<ProbeType, IHealthProbe> probeMap,
        CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (!probeMap.TryGetValue(endpoint.ProbeType, out var probe))
            {
                _logger.LogError("No probe implementation found for type {ProbeType}", endpoint.ProbeType);
                var failResult = new ProbeResult
                {
                    EndpointName = endpoint.Name,
                    Success = false,
                    Duration = TimeSpan.Zero,
                    Message = $"No probe implementation for type {endpoint.ProbeType}",
                    Timestamp = DateTimeOffset.UtcNow
                };
                _reporter.Report(endpoint, failResult);
                return;
            }

            if (endpoint.TimeoutSeconds <= 0)
            {
                endpoint.TimeoutSeconds = _config.DefaultTimeoutSeconds;
            }

            var result = await probe.ExecuteAsync(endpoint, cancellationToken);
            _reporter.Report(endpoint, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Probe execution cancelled for {EndpointName}", endpoint.Name);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error executing probe for {EndpointName}", endpoint.Name);
            var errorResult = new ProbeResult
            {
                EndpointName = endpoint.Name,
                Success = false,
                Duration = TimeSpan.Zero,
                Message = $"Unhandled error: {ex.Message}",
                Timestamp = DateTimeOffset.UtcNow
            };
            _reporter.Report(endpoint, errorResult);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
