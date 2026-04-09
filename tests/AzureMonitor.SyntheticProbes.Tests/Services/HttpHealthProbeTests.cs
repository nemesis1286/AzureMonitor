using System.Net;
using AzureMonitor.SyntheticProbes.Models;
using AzureMonitor.SyntheticProbes.Services;
using AzureMonitor.SyntheticProbes.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AzureMonitor.SyntheticProbes.Tests.Services;

public class HttpHealthProbeTests
{
    private readonly NullLogger<HttpHealthProbe> _logger = new();

    private HttpHealthProbe CreateProbe(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("HealthProbe")).Returns(client);
        return new HttpHealthProbe(factory.Object, _logger);
    }

    private static ProbeEndpoint CreateHttpEndpoint(
        int expectedStatus = 200,
        string? expectedBody = null,
        int timeoutSeconds = 30)
    {
        return new ProbeEndpoint
        {
            Name = "Test HTTP Endpoint",
            ProbeType = ProbeType.Http,
            Url = "https://test.example.com/health",
            ExpectedStatusCode = expectedStatus,
            TimeoutSeconds = timeoutSeconds,
            ExpectedResponseBodyContains = expectedBody
        };
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulResponse_ReturnsSuccess()
    {
        var handler = new TestHttpMessageHandler(HttpStatusCode.OK, "Healthy");
        var probe = CreateProbe(handler);
        var endpoint = CreateHttpEndpoint();

        var result = await probe.ExecuteAsync(endpoint, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        result.EndpointName.Should().Be("Test HTTP Endpoint");
    }

    [Fact]
    public async Task ExecuteAsync_WrongStatusCode_ReturnsFailure()
    {
        var handler = new TestHttpMessageHandler(HttpStatusCode.InternalServerError);
        var probe = CreateProbe(handler);
        var endpoint = CreateHttpEndpoint(expectedStatus: 200);

        var result = await probe.ExecuteAsync(endpoint, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(500);
        result.Message.Should().Contain("Expected status 200 but got 500");
    }

    [Fact]
    public async Task ExecuteAsync_BodyMismatch_ReturnsFailure()
    {
        var handler = new TestHttpMessageHandler(HttpStatusCode.OK, "Degraded");
        var probe = CreateProbe(handler);
        var endpoint = CreateHttpEndpoint(expectedBody: "Healthy");

        var result = await probe.ExecuteAsync(endpoint, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("does not contain expected string");
    }

    [Fact]
    public async Task ExecuteAsync_BodyMatch_ReturnsSuccess()
    {
        var handler = new TestHttpMessageHandler(HttpStatusCode.OK, "Status: Healthy - All systems go");
        var probe = CreateProbe(handler);
        var endpoint = CreateHttpEndpoint(expectedBody: "Healthy");

        var result = await probe.ExecuteAsync(endpoint, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_ReturnsFailure()
    {
        var handler = new TestHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var probe = CreateProbe(handler);
        var endpoint = CreateHttpEndpoint(timeoutSeconds: 1);

        var result = await probe.ExecuteAsync(endpoint, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("timed out");
    }

    [Fact]
    public async Task ExecuteAsync_ConnectionError_ReturnsFailure()
    {
        var handler = new TestHttpMessageHandler((_, _) =>
            throw new HttpRequestException("Connection refused"));
        var probe = CreateProbe(handler);
        var endpoint = CreateHttpEndpoint();

        var result = await probe.ExecuteAsync(endpoint, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Connection failed");
    }

    [Fact]
    public async Task ExecuteAsync_CustomHeaders_AreSent()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new TestHttpMessageHandler((req, _) =>
        {
            capturedRequest = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var probe = CreateProbe(handler);
        var endpoint = CreateHttpEndpoint();
        endpoint.Headers = new Dictionary<string, string>
        {
            ["X-Custom-Header"] = "test-value",
            ["Authorization"] = "Bearer token123"
        };

        await probe.ExecuteAsync(endpoint, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.GetValues("X-Custom-Header").Should().Contain("test-value");
        capturedRequest.Headers.GetValues("Authorization").Should().Contain("Bearer token123");
    }

    [Fact]
    public void SupportedType_ReturnsHttp()
    {
        var factory = new Mock<IHttpClientFactory>();
        var probe = new HttpHealthProbe(factory.Object, _logger);

        probe.SupportedType.Should().Be(ProbeType.Http);
    }
}
