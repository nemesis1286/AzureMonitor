using System.Diagnostics;
using AzureMonitor.SyntheticProbes.Models;
using Microsoft.Extensions.Logging;

namespace AzureMonitor.SyntheticProbes.Services;

public class HttpHealthProbe : IHealthProbe
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpHealthProbe> _logger;

    public ProbeType SupportedType => ProbeType.Http;

    public HttpHealthProbe(IHttpClientFactory httpClientFactory, ILogger<HttpHealthProbe> logger)
    {
        _httpClientFactory = httpClientFactory;
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
            var client = _httpClientFactory.CreateClient("HealthProbe");

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Url);

            if (endpoint.Headers is not null)
            {
                foreach (var header in endpoint.Headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            _logger.LogInformation("Probing HTTP endpoint {EndpointName} at {Url}", endpoint.Name, endpoint.Url);

            using var response = await client.SendAsync(request, timeoutCts.Token);

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.StatusCode = (int)response.StatusCode;

            var expectedStatus = endpoint.ExpectedStatusCode;
            if (result.StatusCode != expectedStatus)
            {
                result.Success = false;
                result.Message = $"Expected status {expectedStatus} but got {result.StatusCode}";
                _logger.LogWarning("Probe {EndpointName} failed: {Message}", endpoint.Name, result.Message);
                return result;
            }

            if (!string.IsNullOrEmpty(endpoint.ExpectedResponseBodyContains))
            {
                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                if (!body.Contains(endpoint.ExpectedResponseBodyContains, StringComparison.OrdinalIgnoreCase))
                {
                    result.Success = false;
                    result.Message = $"Response body does not contain expected string '{endpoint.ExpectedResponseBodyContains}'";
                    _logger.LogWarning("Probe {EndpointName} failed: {Message}", endpoint.Name, result.Message);
                    return result;
                }
            }

            result.Success = true;
            result.Message = $"HTTP {result.StatusCode} OK";
            _logger.LogInformation("Probe {EndpointName} succeeded in {Duration}ms", endpoint.Name, result.Duration.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.Success = false;
            result.Message = $"Request timed out after {endpoint.TimeoutSeconds}s";
            _logger.LogWarning("Probe {EndpointName} timed out after {Timeout}s", endpoint.Name, endpoint.TimeoutSeconds);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.Success = false;
            result.Message = $"Connection failed: {ex.Message}";
            _logger.LogError(ex, "Probe {EndpointName} connection failed", endpoint.Name);
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
