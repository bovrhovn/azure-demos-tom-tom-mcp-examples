using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TT.Web.LocationIntelligence.Features.Chat;
using TT.Web.LocationIntelligence.Options.Agents;

namespace TT.Web.LocationIntelligence.Features.Travel;

public sealed record WeatherSource(string Title, string Url, string Content, string? LastUpdatedAt);

public sealed class WebIQWeatherClient(HttpClient http, IOptions<WebIQOptions> options)
{
    public async Task<IReadOnlyList<WeatherSource>> SearchAsync(string location, DateOnly date, CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Key))
            throw new ChatException(503, "Configure WebIQ:Key on the server to check weather.", "webiq_not_configured");
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
        request.Headers.Add("x-apikey", settings.Key);
        request.Content = JsonContent.Create(new
        {
            query = $"{location} weather forecast {date:yyyy-MM-dd} sunshine rain snow wind warnings",
            maxResults = settings.MaxResults, maxLength = settings.MaxContentLength
        });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new ChatException(502, $"WebIQ weather search failed (HTTP {(int)response.StatusCode}).", "webiq_unavailable");
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!document.RootElement.TryGetProperty("webResults", out var results) || results.ValueKind != JsonValueKind.Array)
            throw new ChatException(502, "WebIQ returned no weather evidence.", "weather_unavailable");
        var sources = new List<WeatherSource>();
        foreach (var result in results.EnumerateArray().Take(settings.MaxResults))
        {
            var url = Read(result, "url");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) continue;
            sources.Add(new(Read(result, "title"), url, Read(result, "content"), Read(result, "lastUpdatedAt")));
        }
        if (sources.Count == 0)
            throw new ChatException(502, "WebIQ returned no usable weather sources. Sunshine cannot be verified.", "weather_unavailable");
        return sources;
    }

    private static string Read(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
}
