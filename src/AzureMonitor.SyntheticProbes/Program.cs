using AzureMonitor.SyntheticProbes.Models;
using AzureMonitor.SyntheticProbes.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Extensions.Http;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
    })
    .ConfigureServices((context, services) =>
    {
        // Application Insights telemetry
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Bind probe configuration from appsettings.json
        var probeSection = context.Configuration.GetSection(ProbeConfiguration.SectionName);
        services.Configure<ProbeConfiguration>(probeSection);

        // Override RunLocation from REGION_NAME environment variable if available
        var regionName = Environment.GetEnvironmentVariable("REGION_NAME");
        if (!string.IsNullOrEmpty(regionName))
        {
            services.PostConfigure<ProbeConfiguration>(config =>
            {
                config.RunLocation = regionName;
            });
        }

        // HttpClient with Polly retry policy for transient faults
        services.AddHttpClient("HealthProbe")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                // Do not follow redirects automatically so we can check the actual status code
                AllowAutoRedirect = false
            })
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(2, retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

        // Register health probe implementations
        services.AddSingleton<IHealthProbe, HttpHealthProbe>();
        services.AddSingleton<IHealthProbe, TcpHealthProbe>();

        // Register services
        services.AddSingleton<IAvailabilityReporter, AvailabilityReporter>();
        services.AddSingleton<IProbeOrchestrator, ProbeOrchestrator>();
    })
    .Build();

host.Run();
