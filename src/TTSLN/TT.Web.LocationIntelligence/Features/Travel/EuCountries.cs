using TT.Web.LocationIntelligence.Features.Chat;

namespace TT.Web.LocationIntelligence.Features.Travel;

public static class EuCountries
{
    public const string Source = "https://european-union.europa.eu/principles-countries-history/eu-countries_en";
    // EU membership is domain data, not a provider setting. Keep this catalog aligned with the cited source.
    public static IReadOnlyList<TravelCountry> All { get; } = Array.AsReadOnly<TravelCountry>(
    [
        new("AT", "Austria", "Vienna"), new("BE", "Belgium", "Brussels"),
        new("BG", "Bulgaria", "Sofia"), new("HR", "Croatia", "Zagreb"),
        new("CY", "Cyprus", "Nicosia"), new("CZ", "Czechia", "Prague"),
        new("DK", "Denmark", "Copenhagen"), new("EE", "Estonia", "Tallinn"),
        new("FI", "Finland", "Helsinki"), new("FR", "France", "Paris"),
        new("DE", "Germany", "Berlin"), new("GR", "Greece", "Athens"),
        new("HU", "Hungary", "Budapest"), new("IE", "Ireland", "Dublin"),
        new("IT", "Italy", "Rome"), new("LV", "Latvia", "Riga"),
        new("LT", "Lithuania", "Vilnius"), new("LU", "Luxembourg", "Luxembourg"),
        new("MT", "Malta", "Valletta"), new("NL", "Netherlands", "Amsterdam"),
        new("PL", "Poland", "Warsaw"), new("PT", "Portugal", "Lisbon"),
        new("RO", "Romania", "Bucharest"), new("SK", "Slovakia", "Bratislava"),
        new("SI", "Slovenia", "Ljubljana"), new("ES", "Spain", "Madrid"),
        new("SE", "Sweden", "Stockholm")
    ]);

    public static TravelCountry Find(string? code) =>
        All.FirstOrDefault(country => country.Code == code)
        ?? throw new ChatException(400, "Select a country from the EU country list.", "travel_country_invalid");

    public static void Validate(TravelRequest request, int forecastDays, DateOnly today)
    {
        if (request.Scenario is not ("handoff" or "magentic"))
            throw new ChatException(400, "Select Handoff or Magentic.", "travel_scenario_invalid");
        Find(request.StartCountry);
        Find(request.DestinationCountry);
        if (request.StartCountry == request.DestinationCountry)
            throw new ChatException(400, "Choose different start and destination countries.", "travel_route_invalid");
        if (request.DepartureDate < today || request.DepartureDate > today.AddDays(forecastDays - 1))
            throw new ChatException(400, $"Choose a departure within the next {forecastDays} days.", "travel_date_invalid");
    }
}
