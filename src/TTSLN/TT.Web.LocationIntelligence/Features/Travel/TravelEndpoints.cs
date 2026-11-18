using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using TT.Web.LocationIntelligence.Features.Chat;
using TT.Web.LocationIntelligence.Options.Agents;

namespace TT.Web.LocationIntelligence.Features.Travel;

public static class TravelEndpoints
{
    public const string ApiPath = "/api/travel";
    public const string HubPath = "/hubs/travel";
    private const string Purpose = "TomTom.Travel.Query";

    public static void MapTravelFeature(this WebApplication app)
    {
        var group = app.MapGroup(ApiPath).WithTags("Travel").AddEndpointFilter(ChatEndpoints.ValidateCsrfAsync);
        group.MapGet("/config", (HttpContext context, IOptions<TravelOptions> options,
            IOptions<AgentOptions> agent, IOptions<WebIQOptions> web) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return TypedResults.Ok(new
            {
                countries = EuCountries.All, countrySource = EuCountries.Source,
                minDate = today, maxDate = today.AddDays(options.Value.ForecastDays - 1),
                configured = !string.IsNullOrWhiteSpace(web.Value.Key) &&
                    !string.IsNullOrWhiteSpace(agent.Value.TomTomApiKey) &&
                    !string.IsNullOrWhiteSpace(Endpoint(options.Value, agent.Value)) &&
                    !string.IsNullOrWhiteSpace(Model(options.Value, agent.Value)),
                queriesPath = $"{ApiPath}/queries", hubPath = HubPath
            });
        }).WithName("TravelConfiguration").WithSummary("Get EU countries and public travel planner settings");

        group.MapPost("/queries", (TravelRequest request, HttpContext context,
            IDataProtectionProvider protection, IOptions<TravelOptions> options) =>
        {
            EuCountries.Validate(request, options.Value.ForecastDays, DateOnly.FromDateTime(DateTime.UtcNow));
            var query = new TravelTicket(BrowserSessionMiddleware.Owner(context), request,
                DateTimeOffset.UtcNow.AddSeconds(options.Value.TicketLifetimeSeconds));
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(new
            {
                ticket = protection.CreateProtector(Purpose).Protect(JsonSerializer.Serialize(query))
            });
        }).WithName("StartTravelQuery").WithSummary("Validate a travel request and issue a browser-bound stream ticket")
            .WithDescription("Requires antiforgery. Start the ephemeral Plan(ticket) SignalR stream before the ticket expires.")
            .ProducesProblem(400);
        app.MapHub<TravelHub>(HubPath);
    }

    internal static string Endpoint(TravelOptions travel, AgentOptions agent) =>
        string.IsNullOrWhiteSpace(travel.Endpoint) ? agent.Endpoint : travel.Endpoint;
    internal static string Model(TravelOptions travel, AgentOptions agent) =>
        string.IsNullOrWhiteSpace(travel.Model) ? agent.Model : travel.Model;

    internal static TravelRequest ReadTicket(string ticket, string owner, IDataProtectionProvider protection)
    {
        TravelTicket? query;
        try
        {
            query = JsonSerializer.Deserialize<TravelTicket>(protection.CreateProtector(Purpose).Unprotect(ticket));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException)
        {
            throw new ChatException(400, "The travel request is invalid or expired. Search again.", "travel_ticket_invalid");
        }
        if (query is null || query.Owner != owner || query.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ChatException(400, "The travel request is invalid or expired. Search again.", "travel_ticket_invalid");
        return query.Request;
    }
}
