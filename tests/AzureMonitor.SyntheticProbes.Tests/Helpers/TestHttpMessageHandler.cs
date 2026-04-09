using System.Net;

namespace AzureMonitor.SyntheticProbes.Tests.Helpers;

public class TestHttpMessageHandler : DelegatingHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    public TestHttpMessageHandler(HttpStatusCode statusCode, string content = "")
        : this((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content)
        }))
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return _handler(request, cancellationToken);
    }
}
