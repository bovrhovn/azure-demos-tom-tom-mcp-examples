using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using TT.Web.LocationIntelligence.Features.Operations.Contracts;
using TT.Web.LocationIntelligence.Options.Agents;
using TT.Web.LocationIntelligence.Options.Chat;
using TT.Web.LocationIntelligence.Options.Persistence;

namespace TT.Web.LocationIntelligence.Features.Operations;

/// <summary>Returns safe runtime information, never keys or connection strings.</summary>
public static class OperationsFeature
{
    public const string RuntimePath = "/api/runtime";

    private static readonly JsonSerializerOptions ScriptJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Default
    };

    public static void MapOperationsFeature(this WebApplication app)
    {
        app.MapGet(RuntimePath, Ok<RuntimeResponse> (HttpContext context, IAntiforgery antiforgery,
            IHostEnvironment environment, IOptions<CosmosOptions> cosmos, IOptions<AgentOptions> agent,
            IOptions<ChatOptions> chat) =>
        {
            var response = CreateRuntimeResponse(
                context, antiforgery, environment, cosmos.Value, agent.Value, chat.Value);
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(response);
        })
        .WithName("GetRuntime")
        .WithSummary("Retrieve safe workspace runtime metadata and CSRF token")
        .WithDescription("Issues an antiforgery token and returns bounded client limits, environment info, and API endpoints.");
    }

    public static RuntimeResponse CreateRuntimeResponse(HttpContext context, IAntiforgery antiforgery,
        IHostEnvironment environment, CosmosOptions cosmos, AgentOptions agent, ChatOptions chat)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        return new RuntimeResponse(
            Environment: environment.EnvironmentName,
            Application: environment.ApplicationName,
            Database: cosmos.Database,
            Container: cosmos.Container,
            DatabaseHost: Uri.TryCreate(cosmos.Endpoint, UriKind.Absolute, out var endpoint) ? endpoint.Host : "",
            Emulator: cosmos.UseEmulator,
            Model: agent.Model,
            AgentConfigured: !string.IsNullOrWhiteSpace(agent.Endpoint)
                && !string.IsNullOrWhiteSpace(agent.Model)
                && !string.IsNullOrWhiteSpace(agent.TomTomApiKey),
            MaxMessageLength: chat.MaxMessageLength,
            MaxTitleLength: chat.MaxTitleLength,
            MaxTurns: chat.MaxTurns,
            AntiforgeryHeaderName: tokens.HeaderName ?? "X-CSRF-TOKEN",
            CsrfToken: tokens.RequestToken ?? "",
            Endpoints: new RuntimeEndpoints());
    }

    public static string SerializeForScript(RuntimeResponse runtime) =>
        JsonSerializer.Serialize(runtime, ScriptJsonOptions);
}
