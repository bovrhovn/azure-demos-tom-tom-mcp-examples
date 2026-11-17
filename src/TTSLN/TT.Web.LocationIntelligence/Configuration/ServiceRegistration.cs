using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TT.Web.LocationIntelligence.Features.Chat;
using TT.Web.LocationIntelligence.Features.Operations;
using TT.Web.LocationIntelligence.Options;

namespace TT.Web.LocationIntelligence.Configuration;

public static class ServiceRegistration
{
    public static void AddLocationIntelligence(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddRazorPages();
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        services.AddDataProtection();
        services.AddSignalR(options => options.EnableDetailedErrors = builder.Environment.IsDevelopment());

        services.AddApplicationOptions(builder.Environment);
        services.AddProxySupport();
        services.AddChatServices();
        builder.AddTelemetry();
    }

    private static void AddApplicationOptions(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddOptions<CosmosOptions>().BindConfiguration(CosmosOptions.Section)
            .ValidateDataAnnotations()
            .Validate(options => Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint)
                && endpoint.Scheme == "https" && (environment.IsDevelopment()
                    ? options.UseEmulator : !options.UseEmulator && !endpoint.IsLoopback),
                "Development requires the emulator; non-development requires a production HTTPS Cosmos endpoint.")
            .ValidateOnStart();
        services.AddOptions<AgentOptions>().BindConfiguration(AgentOptions.Section)
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<ChatOptions>().BindConfiguration(ChatOptions.Section)
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<BrowserSessionOptions>().BindConfiguration(BrowserSessionOptions.Section)
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<ReverseProxyOptions>().BindConfiguration(ReverseProxyOptions.Section)
            .ValidateOnStart();
    }

    private static void AddProxySupport(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ReverseProxyOptions>>((options, proxy) =>
        {
            options.ForwardedHeaders = proxy.Value.ForwardedHeaders;
            if (proxy.Value.TrustAllProxies)
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            }
        });
    }

    private static void AddChatServices(this IServiceCollection services)
    {
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
        services.AddSingleton(CosmosChatStore.CreateClient);
        services.AddSingleton<IChatStore, CosmosChatStore>();
        services.AddSingleton<IChatResponder, FoundryChatResponder>();
        services.AddScoped<ChatService>();
        services.AddHealthChecks().AddCheck<CosmosHealthCheck>("cosmos", tags: ["ready"]);
    }

    private static void AddTelemetry(this WebApplicationBuilder builder)
    {
        var insights = builder.Configuration["ApplicationInsights:ConnectionString"];
        if (string.IsNullOrWhiteSpace(insights))
            insights = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        if (string.IsNullOrWhiteSpace(insights)) return;

        builder.Services.AddApplicationInsightsTelemetry(options => options.ConnectionString = insights);
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(AgentTelemetry.SourceName))
            .WithMetrics(metrics => metrics.AddMeter(AgentTelemetry.SourceName));
        builder.Logging.AddFilter<OpenTelemetry.Logs.OpenTelemetryLoggerProvider>("", LogLevel.Information);
    }
}
