using System.Collections.Concurrent;
using TT.Web.LocationIntelligence.Features.Chat;

namespace TT.Web.LocationIntelligence.Features.Travel;

internal sealed class TravelEvidence(TravelRequest request)
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<WeatherSource>> sources = new(StringComparer.OrdinalIgnoreCase);
    internal ConcurrentDictionary<string, WeatherAssessment> Weather { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal bool RouteChecked { get; set; }
    internal bool ChargingChecked { get; set; }
    internal bool CorridorChecked { get; set; }
    private string[] itineraryLocations = [];

    internal void AddSources(string location, IReadOnlyList<WeatherSource> results) => sources[location] = results;

    internal WeatherAssessment Assess(string location, string condition, string summary, string[] urls)
    {
        if (condition is not ("sunny" or "mixed" or "bad" or "unknown") ||
            string.IsNullOrWhiteSpace(summary) || summary.Length > 3000 ||
            !sources.TryGetValue(location, out var found) || urls.Length == 0 ||
            urls.Any(url => !found.Any(source => source.Url == url)))
            throw new ChatException(502, "Weather assessment must cite retrieved WebIQ sources.", "weather_evidence_invalid");
        var assessment = new WeatherAssessment(location, condition, summary, urls);
        Weather[location] = assessment;
        return assessment;
    }

    internal bool RequestedWeatherChecked =>
        Weather.ContainsKey(EuCountries.Find(request.StartCountry).Capital) &&
        Weather.ContainsKey(EuCountries.Find(request.DestinationCountry).Capital);

    internal bool CanRecommendSunnyRoute => RequestedWeatherChecked && RouteChecked && ChargingChecked && CorridorChecked &&
        itineraryLocations.Length >= 2 &&
        itineraryLocations.All(location => Weather.TryGetValue(location, out var item) && item.Condition == "sunny");

    internal void StartRoute()
    {
        RouteChecked = true;
        CorridorChecked = false;
        ChargingChecked = false;
        itineraryLocations = [];
    }

    internal string RecordItinerary(string[] locations)
    {
        itineraryLocations = [];
        if (!RouteChecked || !ChargingChecked || !CorridorChecked || locations.Length < 2 ||
            !locations[0].Equals(EuCountries.Find(request.StartCountry).Capital, StringComparison.OrdinalIgnoreCase) ||
            locations.Any(location => !Weather.TryGetValue(location, out var item) || item.Condition != "sunny"))
            return "Not verified. Record sunny weather for the actual origin, destination and every proposed stop after TomTom route and charging checks. Otherwise recommend postponing.";
        itineraryLocations = [.. locations];
        return "Sunshine evidence recorded for the submitted route locations. Cite the forecasts and explain any destination change; forecasts and charging availability are not guarantees.";
    }

    internal void VerifyCompletion(bool planCreated, bool managerSatisfied = false)
    {
        if (!RequestedWeatherChecked)
            throw new ChatException(502, "The agents did not verify weather at both requested endpoints. No complete itinerary was produced.", "travel_incomplete");
        if (request.Scenario == "magentic" && !planCreated)
            throw new ChatException(502, "The manager did not produce a travel plan.", "travel_incomplete");
        if (request.Scenario == "magentic" && !managerSatisfied)
            throw new ChatException(502, "The manager reached an orchestration limit without completing its assessment. Results are incomplete.", "travel_incomplete");
        if (request.Scenario == "handoff" && (!RouteChecked || !ChargingChecked || !CorridorChecked))
            throw new ChatException(502, "The agents did not finish the TomTom route and charging checks. Results are incomplete.", "travel_incomplete");
    }
}
