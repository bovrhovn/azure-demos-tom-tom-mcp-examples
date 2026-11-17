using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using TT.Web.LocationIntelligence.Features.Chat;
using TT.Web.LocationIntelligence.Options.Agents;
using TT.Web.LocationIntelligence.Options.Chat;
using TT.Web.LocationIntelligence.Options.Persistence;

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
                maxMessageLength = chat.Value.MaxMessageLength, maxTitleLength = SessionTitleLimits.MaxLength,
                csrfToken = tokens.RequestToken
            });
        });
    }
}
