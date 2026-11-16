using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TT.Web.LocationIntelligence.Features.Chat;
using TT.Web.LocationIntelligence.Options;

namespace TT.Web.LocationIntelligence.Features.Operations;

/// <summary>Returns safe runtime information, never keys or connection strings.</summary>
public static class OperationsFeature
{
    public static void MapOperationsFeature(this WebApplication app)
    {
        app.MapGet("/api/runtime", (HttpContext context, IAntiforgery antiforgery,
            IHostEnvironment environment, IOptions<CosmosOptions> cosmos, IOptions<AgentOptions> agent,
            IOptions<ChatOptions> chat) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new
            {
                environment = environment.EnvironmentName,
                database = cosmos.Value.Database, container = cosmos.Value.Container,
                databaseHost = new Uri(cosmos.Value.Endpoint).Host,
                emulator = cosmos.Value.UseEmulator, model = agent.Value.Model,
                agentConfigured = !string.IsNullOrWhiteSpace(agent.Value.Endpoint)
                    && !string.IsNullOrWhiteSpace(agent.Value.Model) && !string.IsNullOrWhiteSpace(agent.Value.TomTomApiKey),
                maxMessageLength = chat.Value.MaxMessageLength, csrfToken = tokens.RequestToken
            });
        });
    }
}

/// <summary>Separates process liveness from Cosmos readiness.</summary>
public sealed class CosmosHealthCheck(IChatStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await store.CheckHealthAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return HealthCheckResult.Unhealthy("Cosmos DB is unavailable.", ex);
        }
    }
}

/// <summary>Logs failures and sends sanitized RFC 9457 responses to clients.</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            ChatException chat => chat.StatusCode,
            CosmosException cosmos when cosmos.StatusCode == HttpStatusCode.TooManyRequests => 429,
            CosmosException => 503,
            BadHttpRequestException => 400,
            _ => 500
        };
        logger.LogError(exception, "Request failed with status {StatusCode}; trace {TraceId}",
            status, context.TraceIdentifier);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new()
            {
                Status = status, Title = exception is ChatException ? exception.Message
                    : status == 503 ? "The database is unavailable." : "The request could not be completed.",
                Extensions = { ["traceId"] = context.TraceIdentifier }
            }
        });
    }
}
