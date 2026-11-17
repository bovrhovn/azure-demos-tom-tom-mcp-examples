using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using TT.Web.LocationIntelligence.Features.Chat;
using TT.Web.LocationIntelligence.Options.Agents;
using TT.Web.LocationIntelligence.Options.Chat;

namespace TT.Web.LocationIntelligence.Features.Maps;

public sealed record MapQueryRequest(string? Prompt);
public sealed record MapAppToolRequest(string Name, Dictionary<string, object?> Arguments);
public sealed record MapQueryTicket(string Owner, string Prompt, DateTimeOffset ExpiresAt);

public static class MapsEndpoints
{
    public const string ApiPath = "/api/maps";
    public const string HubPath = "/hubs/maps";
    private const string ProtectionPurpose = "TomTom.Maps.Query";
    private static readonly HashSet<string> AppTools = new(StringComparer.Ordinal)
    {
        "tomtom-get-api-key", "tomtom-get-app-config", "tomtom-get-app-data", "tomtom-report-app-status"
    };

    public static void MapMapsFeature(this WebApplication app)
    {
        var group = app.MapGroup(ApiPath).AddEndpointFilter(ChatEndpoints.ValidateCsrfAsync);
        group.MapGet("/config", (HttpContext context, IOptions<MapsOptions> maps, IOptions<ChatOptions> chat) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new
            {
                maps.Value.DefaultPrompt, chat.Value.MaxMessageLength,
                appInitializationTimeoutMilliseconds = maps.Value.AppInitializationTimeoutSeconds * 1000,
                queriesPath = $"{ApiPath}/queries", appToolsPath = $"{ApiPath}/app-tools", hubPath = HubPath
            });
        });
        group.MapPost("/queries", (MapQueryRequest request, HttpContext context,
            IDataProtectionProvider protection, IOptions<ChatOptions> chat, IOptions<MapsOptions> maps) =>
        {
            if (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > chat.Value.MaxMessageLength)
                throw new ChatException(400, $"Enter a location question of up to {chat.Value.MaxMessageLength} characters.",
                    "maps_prompt_invalid");
            var ticket = new MapQueryTicket(BrowserSessionMiddleware.Owner(context), request.Prompt.Trim(),
                DateTimeOffset.UtcNow.AddSeconds(maps.Value.TicketLifetimeSeconds));
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { ticket = protection.CreateProtector(ProtectionPurpose).Protect(JsonSerializer.Serialize(ticket)) });
        });
        group.MapPost("/app-tools", async (MapAppToolRequest request, HttpContext context,
            IMapsResponder responder, IOptions<MapsOptions> maps, CancellationToken ct) =>
        {
            if (!AppTools.Contains(request.Name))
                throw new ChatException(400, "This tool is not available to map apps.", "maps_tool_forbidden");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(maps.Value.TimeoutSeconds));
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                return Results.Ok(await responder.CallAppToolAsync(request.Name, request.Arguments, timeout.Token));
            }
            catch (Exception ex) when (ex is not ChatException)
            {
                context.RequestServices.GetRequiredService<ILogger<FoundryMapsResponder>>()
                    .LogWarning("Maps app tool ended with {ErrorType}", ex.GetType().Name);
                throw new ChatException(502, "The TomTom map app request failed.", "maps_app_unavailable");
            }
        });
        app.MapHub<MapsHub>(HubPath);
    }

    internal static MapQueryTicket ReadTicket(string ticket, string owner, IDataProtectionProvider protection)
    {
        MapQueryTicket? query;
        try
        {
            query = JsonSerializer.Deserialize<MapQueryTicket>(protection.CreateProtector(ProtectionPurpose).Unprotect(ticket));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException)
        {
            throw new ChatException(400, "The maps request is invalid or expired. Submit your question again.", "maps_ticket_invalid");
        }
        if (query is null || query.Owner != owner || query.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ChatException(400, "The maps request is invalid or expired. Submit your question again.", "maps_ticket_invalid");
        return query;
    }
}
