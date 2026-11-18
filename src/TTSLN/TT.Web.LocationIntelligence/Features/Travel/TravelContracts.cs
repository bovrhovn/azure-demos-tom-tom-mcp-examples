using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace TT.Web.LocationIntelligence.Features.Travel;

public sealed record TravelRequest(
    [property: Required, Description("handoff or magentic")] string Scenario,
    [property: Required, Description("EU ISO 3166-1 alpha-2 origin code")] string StartCountry,
    [property: Required, Description("EU ISO 3166-1 alpha-2 destination code")] string DestinationCountry,
    [property: Description("Departure date, in UTC, within the configured forecast horizon")] DateOnly DepartureDate);

public sealed record TravelCountry(string Code, string Name, string Capital);
public sealed record TravelTicket(string Owner, TravelRequest Request, DateTimeOffset ExpiresAt);
public sealed record TravelEvent(string Type, string Agent, object? Data);
public sealed record WeatherAssessment(string Location, string Condition, string Summary, string[] Sources);

public interface ITravelResponder
{
    Task RunAsync(TravelRequest request, Func<TravelEvent, CancellationToken, Task> publish, CancellationToken ct);
}
