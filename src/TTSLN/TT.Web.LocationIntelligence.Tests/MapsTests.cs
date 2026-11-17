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
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TT.Web.LocationIntelligence.Features.Chat.Persistence;
using TT.Web.LocationIntelligence.Features.Maps;

namespace TT.Web.LocationIntelligence.Tests;

[TestClass]
public sealed class MapsTests
{
    private sealed class Responder : IMapsResponder
    {
        public List<string> Prompts { get; } = [];
        public List<string> AppTools { get; } = [];
        public bool Fail { get; set; }
        public bool WaitForCancellation { get; set; }
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunAsync(string prompt, Func<MapStreamEvent, CancellationToken, Task> publish, CancellationToken ct)
        {
            Prompts.Add(prompt);
            await publish(new("status", "Routing"), ct);
            await publish(new("tool-start", new { callId = "route", name = "tomtom-routing", arguments = new { avoid = "tollRoads" } }), ct);
            await publish(new("tool-result", new
            {
                callId = "route", name = "tomtom-routing",
                result = new { content = new[] { new { type = "text", text = "Route result" } }, _meta = new { appData = "full geometry" } }
            }), ct);
            await publish(new("app", new
            {
                callId = "route", name = "tomtom-routing", arguments = new { avoid = "tollRoads" },
                html = "<!doctype html><h1>Interactive route</h1>",
                meta = new { ui = new { csp = new { connectDomains = new[] { "https://api.tomtom.com" } } } }
            }), ct);
            await publish(new("text", "Amsterdam "), ct);
            if (WaitForCancellation)
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                finally { Cancelled.TrySetResult(); }
            }
            if (Fail) throw new InvalidOperationException("private-provider-details");
            await publish(new("text", "to Berlin"), ct);
        }

        public Task<object> CallAppToolAsync(string name, Dictionary<string, object?> arguments, CancellationToken ct)
        {
            AppTools.Add(name);
            if (Fail) throw new InvalidOperationException("private-provider-details");
            return Task.FromResult<object>(new { content = new[] { new { type = "text", text = "app-only-value" } } });
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
                ["BrowserSession:CookieName"] = "test-maps-browser", ["Agent:TomTomApiKey"] = "not-a-real-key"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMapsResponder>();
                services.AddSingleton<IMapsResponder>(Responder);
                services.RemoveAll<IChatStore>();
                services.AddSingleton<IChatStore>(new TestStore());
            });
        }
    }

    private static async Task<string> InitializeAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/runtime");
        var runtime = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", runtime.GetProperty("csrfToken").GetString());
        return string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';')[0]));
    }

    private static async Task<string> TicketAsync(HttpClient client, string prompt = "Amsterdam to Berlin, avoiding toll roads")
    {
        using var response = await client.PostAsJsonAsync("/api/maps/queries", new { prompt });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("ticket").GetString()!;
    }

    private static async Task<WebSocket> SocketAsync(AppFactory factory, string cookies, CancellationToken ct)
    {
        var sockets = factory.Server.CreateWebSocketClient();
        sockets.ConfigureRequest = request => request.Headers["Cookie"] = cookies;
        var socket = await sockets.ConnectAsync(new("ws://localhost/hubs/maps"), ct);
        await SendAsync(socket, new { protocol = "json", version = 1 }, ct);
        var buffer = new byte[4096];
        var handshake = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
        Encoding.UTF8.GetString(buffer, 0, handshake.Count).Should().Be("{}\u001e");
        return socket;
    }

    private static Task SendAsync(WebSocket socket, object value, CancellationToken ct) =>
        socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value) + "\u001e")),
            WebSocketMessageType.Text, true, ct);

    private static async IAsyncEnumerable<JsonElement> ReadAsync(WebSocket socket,
        [EnumeratorCancellation] CancellationToken ct)
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
    public async Task MapsPageAndConfigurationAreAvailableWithoutCreatingConversations()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateClient();
        var page = await client.GetAsync("/Maps");
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync()).Should().Contain("data-workspace=\"maps\"").And.Contain("/dist/app.js");
        var config = await client.GetFromJsonAsync<JsonElement>("/api/maps/config");
        config.GetProperty("defaultPrompt").GetString().Should().Be(
            "Find the fastest route from Amsterdam to Berlin, avoiding toll roads,\nand show me gas stations along the way.");
        config.GetProperty("hubPath").GetString().Should().Be("/hubs/maps");
        config.ToString().Should().NotContain("not-a-real-key").And.NotContain("TomTomApiKey");
        var sessions = await client.GetFromJsonAsync<JsonElement>("/api/sessions");
        sessions.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [TestMethod]
    public async Task QueryAndAppToolsRequireCsrfAndValidateInputsBeforeCallingProviders()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/maps/queries", new { prompt = "Amsterdam" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/maps/app-tools", new { name = "tomtom-get-api-key", arguments = new { } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await InitializeAsync(client);
        foreach (var prompt in new[] { "", "  ", new string('x', 8001) })
        {
            var response = await client.PostAsJsonAsync("/api/maps/queries", new { prompt });
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("maps_prompt_invalid");
        }
        var forbidden = await client.PostAsJsonAsync("/api/maps/app-tools", new { name = "tomtom-routing", arguments = new { } });
        forbidden.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Responder.Prompts.Should().BeEmpty();
        factory.Responder.AppTools.Should().BeEmpty();
        var allowed = await client.PostAsJsonAsync("/api/maps/app-tools", new { name = "tomtom-get-app-config", arguments = new { } });
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        allowed.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await allowed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("content")[0]
            .GetProperty("text").GetString().Should().Be("app-only-value");
        factory.Responder.AppTools.Should().Equal("tomtom-get-app-config");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SignalRStreamsOrderedTextFullToolResultsAndAppHtmlWithoutPersistence(bool maximumUnicodePrompt)
    {
        using var factory = new AppFactory();
        using var client = factory.CreateClient();
        var cookies = await InitializeAsync(client);
        var prompt = maximumUnicodePrompt ? new string('\u754c', 8000) : "Amsterdam to Berlin, avoiding toll roads";
        var ticket = await TicketAsync(client, prompt);
        factory.Services.GetRequiredService<IOptions<HubOptions<MapsHub>>>().Value.MaximumReceiveMessageSize
            .Should().BeGreaterThan(ticket.Length + 256);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var socket = await SocketAsync(factory, cookies, timeout.Token);
        await SendAsync(socket, new { type = 4, invocationId = "q", target = "Query", arguments = new[] { ticket } }, timeout.Token);
        var events = new List<JsonElement>();
        await foreach (var item in ReadAsync(socket, timeout.Token))
        {
            if (item.GetProperty("type").GetInt32() == 3)
                item.TryGetProperty("error", out var streamError).Should().BeFalse(
                    streamError.ValueKind == JsonValueKind.String ? streamError.GetString() : "the stream must complete normally");
            item.GetProperty("type").GetInt32().Should().NotBe(7, item.ToString());
            if (item.GetProperty("type").GetInt32() == 2) events.Add(item.GetProperty("item"));
        }
        events.Select(item => item.GetProperty("type").GetString()).Should().Equal(
            "status", "tool-start", "tool-result", "app", "text", "text", "completed");
        events[2].GetProperty("data").GetProperty("result").GetProperty("_meta")
            .GetProperty("appData").GetString().Should().Be("full geometry");
        events[3].GetProperty("data").GetProperty("html").GetString().Should().Contain("Interactive route");
        string.Concat(events.Where(item => item.GetProperty("type").GetString() == "text")
            .Select(item => item.GetProperty("data").GetString())).Should().Be("Amsterdam to Berlin");
        factory.Responder.Prompts.Should().Equal(prompt);
        var sessions = await client.GetFromJsonAsync<JsonElement>("/api/sessions");
        sessions.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [TestMethod]
    [DataRow("other-browser")]
    [DataRow("tampered")]
    [DataRow("expired")]
    public async Task InvalidTicketsCannotStartTheAgent(string mode)
    {
        using var factory = new AppFactory();
        using var alice = factory.CreateClient();
        var cookies = await InitializeAsync(alice);
        var ticket = await TicketAsync(alice);
        if (mode == "other-browser")
        {
            using var bob = factory.CreateClient();
            cookies = await InitializeAsync(bob);
        }
        if (mode == "tampered") ticket = "invalid-ticket";
        if (mode == "expired")
        {
            var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("TomTom.Maps.Query");
            var query = JsonSerializer.Deserialize<MapQueryTicket>(protector.Unprotect(ticket))!;
            ticket = protector.Protect(JsonSerializer.Serialize(query with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) }));
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var socket = await SocketAsync(factory, cookies, timeout.Token);
        await SendAsync(socket, new { type = 4, invocationId = "q", target = "Query", arguments = new[] { ticket } }, timeout.Token);
        var events = new List<JsonElement>();
        await foreach (var item in ReadAsync(socket, timeout.Token))
            if (item.GetProperty("type").GetInt32() == 2) events.Add(item.GetProperty("item"));
        events.Should().ContainSingle();
        events[0].GetProperty("type").GetString().Should().Be("error");
        events[0].GetProperty("data").GetString().Should().Contain("invalid or expired");
        factory.Responder.Prompts.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ProviderFailuresAreExplicitSanitizedAndNeverFollowedByCompleted()
    {
        using var factory = new AppFactory();
        factory.Responder.Fail = true;
        using var client = factory.CreateClient();
        var cookies = await InitializeAsync(client);
        var ticket = await TicketAsync(client);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var socket = await SocketAsync(factory, cookies, timeout.Token);
        await SendAsync(socket, new { type = 4, invocationId = "q", target = "Query", arguments = new[] { ticket } }, timeout.Token);
        var events = new List<JsonElement>();
        await foreach (var item in ReadAsync(socket, timeout.Token))
            if (item.GetProperty("type").GetInt32() == 2) events.Add(item.GetProperty("item"));
        events.Last().GetProperty("type").GetString().Should().Be("error");
        JsonSerializer.Serialize(events).Should().NotContain("private-provider-details").And.NotContain("\"completed\"");
        var response = await client.PostAsJsonAsync("/api/maps/app-tools", new { name = "tomtom-get-app-config", arguments = new { } });
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("private-provider-details").And.Contain("maps_app_unavailable");
    }

    [TestMethod]
    public async Task CancellingTheSignalRStreamCancelsProviderWork()
    {
        using var factory = new AppFactory();
        factory.Responder.WaitForCancellation = true;
        using var client = factory.CreateClient();
        var cookies = await InitializeAsync(client);
        var ticket = await TicketAsync(client);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var socket = await SocketAsync(factory, cookies, timeout.Token);
        await SendAsync(socket, new { type = 4, invocationId = "q", target = "Query", arguments = new[] { ticket } }, timeout.Token);
        await foreach (var item in ReadAsync(socket, timeout.Token))
        {
            if (item.GetProperty("type").GetInt32() == 2 &&
                item.GetProperty("item").GetProperty("type").GetString() == "text")
            {
                await SendAsync(socket, new { type = 5, invocationId = "q" }, timeout.Token);
                break;
            }
        }
        await factory.Responder.Cancelled.Task.WaitAsync(timeout.Token);
    }
}
