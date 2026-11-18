using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TT.Web.LocationIntelligence.Features.Chat;
using TT.Web.LocationIntelligence.Features.Chat.Persistence;
using TT.Web.LocationIntelligence.Features.Travel;
using TT.Web.LocationIntelligence.Options.Agents;

namespace TT.Web.LocationIntelligence.Tests;

[TestClass]
public sealed class TravelTests
{
    private sealed class Responder : ITravelResponder
    {
        public List<TravelRequest> Requests { get; } = [];
        public bool Fail { get; set; }
        public bool Wait { get; set; }
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task RunAsync(TravelRequest request, Func<TravelEvent, CancellationToken, Task> publish, CancellationToken ct)
        {
            Requests.Add(request);
            await publish(new("status", "TravelAssistant", "Trip received"), ct);
            if (Wait)
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                finally { Cancelled.TrySetResult(); }
            }
            if (Fail) throw new InvalidOperationException("private-provider-details");
            if (request.Scenario == "magentic") await publish(new("plan", "TravelManager", "Check weather, then route"), ct);
            await publish(new("weather", "WeatherChecker", new WeatherAssessment("Amsterdam", "bad", "Rain warning", ["https://weather.example"])), ct);
            await publish(new("result", "RoutePlanner", new { text = "Verified findings", outcome = request.Scenario == "magentic" ? "postpone" : "itinerary" }), ct);
        }
    }

    private sealed class AppFactory : WebApplicationFactory<Program>
    {
        public Responder Responder { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cosmos:Endpoint"] = "https://tests.documents.azure.com", ["Cosmos:UseEmulator"] = "false",
                ["Agent:TomTomApiKey"] = "private-tomtom-key", ["WebIQ:Key"] = "private-webiq-key",
                ["Travel:Endpoint"] = "https://foundry.example/api/projects/test", ["Travel:Model"] = "test-deployment",
                ["Travel:MaxConcurrentRuns"] = "1"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITravelResponder>();
                services.AddSingleton<ITravelResponder>(Responder);
                services.RemoveAll<IChatStore>();
                services.AddSingleton<IChatStore>(new TestStore());
            });
        }
    }

    private static TravelRequest Request(string scenario = "handoff") =>
        new(scenario, "NL", "DE", DateOnly.FromDateTime(DateTime.UtcNow));

    private static async Task<string> InitializeAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/runtime");
        var runtime = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", runtime.GetProperty("csrfToken").GetString());
        return string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';')[0]));
    }

    private static async Task<string> TicketAsync(HttpClient client, string scenario = "handoff")
    {
        using var response = await client.PostAsJsonAsync("/api/travel/queries", Request(scenario));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticket").GetString()!;
    }

    private static async Task<WebSocket> SocketAsync(AppFactory factory, string cookies, CancellationToken ct)
    {
        var sockets = factory.Server.CreateWebSocketClient();
        sockets.ConfigureRequest = request => request.Headers["Cookie"] = cookies;
        var socket = await sockets.ConnectAsync(new("ws://localhost/hubs/travel"), ct);
        await SendAsync(socket, new { protocol = "json", version = 1 }, ct);
        var buffer = new byte[4096];
        var handshake = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
        Encoding.UTF8.GetString(buffer, 0, handshake.Count).Should().Be("{}\u001e");
        return socket;
    }

    private static Task SendAsync(WebSocket socket, object value, CancellationToken ct) =>
        socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value) + "\u001e")),
            WebSocketMessageType.Text, true, ct);

    private static async IAsyncEnumerable<JsonElement> ReadAsync(WebSocket socket, [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[8192];
        var pending = "";
        while (!ct.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (received.MessageType == WebSocketMessageType.Close) yield break;
                message.Write(buffer, 0, received.Count);
            } while (!received.EndOfMessage);
            pending += Encoding.UTF8.GetString(message.ToArray());
            int delimiter;
            while ((delimiter = pending.IndexOf('\u001e')) >= 0)
            {
                using var json = JsonDocument.Parse(pending[..delimiter]);
                var item = json.RootElement.Clone();
                pending = pending[(delimiter + 1)..];
                yield return item;
                if (item.TryGetProperty("type", out var type) && type.GetInt32() == 3) yield break;
            }
        }
    }

    [TestMethod]
    public async Task TravelPagesAndPublicCountryCatalogDoNotExposeCredentials()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateClient();
        foreach (var route in new[] { "/Travel", "/Travel/Handoff", "/Travel/Magentic" })
        {
            var response = await client.GetAsync(route);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Contain("data-workspace=\"travel\"");
        }
        var config = await client.GetFromJsonAsync<JsonElement>("/api/travel/config");
        config.GetProperty("countries").GetArrayLength().Should().Be(27);
        config.GetProperty("countries").EnumerateArray().Should().NotContain(country => country.GetProperty("code").GetString() == "GB");
        config.GetProperty("configured").GetBoolean().Should().BeTrue();
        config.ToString().Should().NotContain("private-").And.NotContain("TomTomApiKey");
        factory.Responder.Requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task TravelQueriesRequireCsrfAndRejectInvalidTripsBeforeRunningAgents()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/travel/queries", Request())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await InitializeAsync(client);
        foreach (var request in new[]
        {
            Request() with { Scenario = "unknown" }, Request() with { StartCountry = "GB" },
            Request() with { DestinationCountry = "NL" }, Request() with { StartCountry = null! },
            Request() with { DepartureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1) },
            Request() with { DepartureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7) }
        })
            (await client.PostAsJsonAsync("/api/travel/queries", request)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Responder.Requests.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("handoff")]
    [DataRow("magentic")]
    public async Task BothScenariosStreamFindingsAndAnExplicitTerminalEventWithoutPersistence(string scenario)
    {
        using var factory = new AppFactory();
        using var client = factory.CreateClient();
        var cookies = await InitializeAsync(client);
        var ticket = await TicketAsync(client, scenario);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var socket = await SocketAsync(factory, cookies, timeout.Token);
        await SendAsync(socket, new { type = 4, invocationId = "q", target = "Plan", arguments = new[] { ticket } }, timeout.Token);
        var events = new List<JsonElement>();
        await foreach (var item in ReadAsync(socket, timeout.Token))
            if (item.GetProperty("type").GetInt32() == 2) events.Add(item.GetProperty("item"));
        events.Select(item => item.GetProperty("type").GetString()).Should().Equal(scenario == "magentic"
            ? ["status", "plan", "weather", "result", "completed"] : new[] { "status", "weather", "result", "completed" });
        events.Single(item => item.GetProperty("type").GetString() == "weather").GetProperty("data")
            .GetProperty("condition").GetString().Should().Be("bad");
        factory.Responder.Requests.Should().ContainSingle().Which.Scenario.Should().Be(scenario);
        (await client.GetFromJsonAsync<JsonElement>("/api/sessions")).GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [TestMethod]
    [DataRow("other-browser")]
    [DataRow("tampered")]
    [DataRow("expired")]
    public async Task InvalidOrForeignTicketsCannotExecuteTravelAgents(string mode)
    {
        using var factory = new AppFactory();
        using var client = factory.CreateClient();
        var cookies = await InitializeAsync(client);
        var ticket = await TicketAsync(client);
        if (mode == "other-browser")
        {
            using var other = factory.CreateClient();
            cookies = await InitializeAsync(other);
        }
        if (mode == "tampered") ticket = "invalid-ticket";
        if (mode == "expired")
        {
            var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("TomTom.Travel.Query");
            var query = JsonSerializer.Deserialize<TravelTicket>(protector.Unprotect(ticket))!;
            ticket = protector.Protect(JsonSerializer.Serialize(query with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) }));
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var socket = await SocketAsync(factory, cookies, timeout.Token);
        await SendAsync(socket, new { type = 4, invocationId = "q", target = "Plan", arguments = new[] { ticket } }, timeout.Token);
        var events = new List<JsonElement>();
        await foreach (var item in ReadAsync(socket, timeout.Token))
            if (item.GetProperty("type").GetInt32() == 2) events.Add(item.GetProperty("item"));
        events.Should().ContainSingle().Which.GetProperty("type").GetString().Should().Be("error");
        factory.Responder.Requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ProviderFailureIsSanitizedAndNeverFollowedByCompleted()
    {
        using var factory = new AppFactory();
        factory.Responder.Fail = true;
        using var client = factory.CreateClient();
        var cookies = await InitializeAsync(client);
        var ticket = await TicketAsync(client);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var socket = await SocketAsync(factory, cookies, timeout.Token);
        await SendAsync(socket, new { type = 4, invocationId = "q", target = "Plan", arguments = new[] { ticket } }, timeout.Token);
        var events = new List<JsonElement>();
        await foreach (var item in ReadAsync(socket, timeout.Token))
            if (item.GetProperty("type").GetInt32() == 2) events.Add(item.GetProperty("item"));
        events.Last().GetProperty("type").GetString().Should().Be("error");
        JsonSerializer.Serialize(events).Should().NotContain("private-provider-details").And.NotContain("\"completed\"");
    }

    [TestMethod]
    public async Task CancellingAStreamCancelsProviderWorkAndReleasesCapacity()
    {
        using var factory = new AppFactory();
        factory.Responder.Wait = true;
        using var client = factory.CreateClient();
        var cookies = await InitializeAsync(client);
        var ticket = await TicketAsync(client);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var socket = await SocketAsync(factory, cookies, timeout.Token);
        await SendAsync(socket, new { type = 4, invocationId = "q", target = "Plan", arguments = new[] { ticket } }, timeout.Token);
        await foreach (var item in ReadAsync(socket, timeout.Token))
        {
            if (item.GetProperty("type").GetInt32() != 2) continue;
            await SendAsync(socket, new { type = 5, invocationId = "q" }, timeout.Token);
            break;
        }
        await factory.Responder.Cancelled.Task.WaitAsync(timeout.Token);
        var limiter = factory.Services.GetRequiredService<TravelRunLimiter>();
        await limiter.Slots.WaitAsync(timeout.Token);
        limiter.Slots.Release();
    }

    [TestMethod]
    public void SunshineRequiresRouteEvidenceAndAllActualStopsButAllowsRejectingABadDestination()
    {
        var evidence = new TravelEvidence(Request("magentic"));
        void Record(string city, string condition)
        {
            evidence.AddSources(city, [new("Forecast", "https://weather.example/" + city, "forecast", null)]);
            evidence.Assess(city, condition, "Dated forecast", ["https://weather.example/" + city]);
        }
        Record("Amsterdam", "sunny");
        Record("Berlin", "bad");
        Record("Paris", "sunny");
        evidence.RecordItinerary(["Amsterdam", "Paris"]).Should().StartWith("Not verified");
        evidence.CanRecommendSunnyRoute.Should().BeFalse();
        evidence.RouteChecked = true;
        evidence.ChargingChecked = true;
        evidence.CorridorChecked = true;
        evidence.RecordItinerary(["Amsterdam", "Berlin"]).Should().StartWith("Not verified");
        evidence.RecordItinerary(["Amsterdam", "Unknown stop", "Paris"]).Should().StartWith("Not verified");
        evidence.RecordItinerary(["Paris", "Amsterdam"]).Should().StartWith("Not verified");
        evidence.RecordItinerary(["Amsterdam", "Paris"]).Should().StartWith("Sunshine evidence");
        evidence.CanRecommendSunnyRoute.Should().BeTrue();
        evidence.StartRoute();
        evidence.CorridorChecked = true;
        evidence.ChargingChecked = true;
        evidence.CanRecommendSunnyRoute.Should().BeFalse("a new route invalidates the previous itinerary review");
        evidence.RecordItinerary(["Amsterdam", "Paris"]);
        Record("Paris", "unknown");
        evidence.CanRecommendSunnyRoute.Should().BeFalse();
    }

    [TestMethod]
    public void WeatherRequiresRetrievedSourcesAndHandoffCannotFinishWithoutRouteAndCharging()
    {
        var evidence = new TravelEvidence(Request());
        var invented = () => evidence.Assess("Amsterdam", "sunny", "Fine", ["https://invented.example"]);
        invented.Should().Throw<ChatException>();
        foreach (var city in new[] { "Amsterdam", "Berlin" })
        {
            evidence.AddSources(city, [new("Forecast", "https://weather.example", "Rain", null)]);
            evidence.Assess(city, "bad", "Rain warning", ["https://weather.example"]);
        }
        var incomplete = () => evidence.VerifyCompletion(false);
        incomplete.Should().Throw<ChatException>().Which.Code.Should().Be("travel_incomplete");
        evidence.RouteChecked = true;
        evidence.ChargingChecked = true;
        evidence.CorridorChecked = true;
        incomplete.Should().NotThrow();
    }

    [TestMethod]
    public void MagenticLimitsAreNotSuccessButACompletedWeatherBasedPostponementIs()
    {
        var evidence = new TravelEvidence(Request("magentic"));
        foreach (var city in new[] { "Amsterdam", "Berlin" })
        {
            evidence.AddSources(city, [new("Forecast", "https://weather.example", "Rain", null)]);
            evidence.Assess(city, "bad", "Rain warning", ["https://weather.example"]);
        }
        var incomplete = () => evidence.VerifyCompletion(true, false);
        incomplete.Should().Throw<ChatException>().Which.Code.Should().Be("travel_incomplete");
        var complete = () => evidence.VerifyCompletion(true, true);
        complete.Should().NotThrow();
        evidence.CanRecommendSunnyRoute.Should().BeFalse();
    }

    [TestMethod]
    [DataRow("tomtom-search-businesses", false)]
    [DataRow("tomtom-poi-categories", false)]
    [DataRow("tomtom-get-api-key", false)]
    [DataRow("tomtom-get-app-data", false)]
    [DataRow("tomtom-routing", true)]
    [DataRow("tomtom-geocode", true)]
    [DataRow("tomtom-search-along-route", true)]
    [DataRow("tomtom-ev-charging-stations", true)]
    public void TravelToolsExcludeBusinessCategoriesAndCredentialHelpers(string name, bool allowed) =>
        FoundryTravelResponder.ToolAllowed(name).Should().Be(allowed);

    [TestMethod]
    public void LiveWeatherUsesCamelCaseAndRedactsEscapedSecretsBeforeSignalRDelivery()
    {
        var item = FoundryTravelResponder.SanitizeEvent(new("weather", "WeatherChecker",
            new WeatherAssessment("Amsterdam", "bad", "Rain: test+key", ["https://weather.example"])), "test+key");
        var data = (JsonElement)item.Data!;
        data.GetProperty("location").GetString().Should().Be("Amsterdam");
        data.GetProperty("condition").GetString().Should().Be("bad");
        data.GetProperty("summary").GetString().Should().Be("Rain: [redacted]");
        data.GetProperty("sources")[0].GetString().Should().Be("https://weather.example");
    }

    private sealed class WeatherHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? Key { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Key = request.Headers.GetValues("x-apikey").Single();
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new(status)
            {
                Content = JsonContent.Create(new { webResults = new[]
                {
                    new { title = "Dated forecast", url = "https://weather.example", content = "Rain", lastUpdatedAt = "2026-11-18" },
                    new { title = "Unsafe link", url = "javascript:alert(1)", content = "Ignore", lastUpdatedAt = "" }
                } })
            };
        }
    }

    [TestMethod]
    public async Task WebIQUsesDocumentedSearchContractAndRejectsUnsafeSourceUrls()
    {
        using var handler = new WeatherHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var client = new WebIQWeatherClient(http, Microsoft.Extensions.Options.Options.Create(new WebIQOptions { Key = "test-key" }));
        var result = await client.SearchAsync("Amsterdam", new(2026, 11, 18), CancellationToken.None);
        handler.Key.Should().Be("test-key");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("query").GetString().Should().Contain("Amsterdam").And.Contain("2026-11-18");
        result.Should().ContainSingle().Which.Url.Should().Be("https://weather.example");
    }

    [TestMethod]
    public async Task WebIQProviderErrorsAreExplicitAndDoNotIncludeProviderBodies()
    {
        using var handler = new WeatherHandler(HttpStatusCode.Unauthorized);
        using var http = new HttpClient(handler);
        var client = new WebIQWeatherClient(http, Microsoft.Extensions.Options.Options.Create(new WebIQOptions { Key = "test-key" }));
        var run = () => client.SearchAsync("Amsterdam", new(2026, 11, 18), CancellationToken.None);
        (await run.Should().ThrowAsync<ChatException>()).Which.Code.Should().Be("webiq_unavailable");
    }
}
