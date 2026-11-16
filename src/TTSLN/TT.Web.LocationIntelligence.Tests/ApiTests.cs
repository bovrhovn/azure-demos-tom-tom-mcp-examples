using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using TT.Web.LocationIntelligence.Features.Chat;

namespace TT.Web.LocationIntelligence.Tests;

[TestClass]
public sealed class ApiTests
{
    private sealed class AppFactory(bool trustAllProxies = false) : WebApplicationFactory<Program>
    {
        public TestStore Store { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Cosmos:Endpoint"] = "https://tests.documents.azure.com",
                    ["Cosmos:UseEmulator"] = "false",
                    ["ReverseProxy:TrustAllProxies"] = trustAllProxies.ToString(),
                    ["BrowserSession:CookieName"] = "test-browser-session"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChatStore>();
                services.AddSingleton<IChatStore>(Store);
                var responder = new Mock<IChatResponder>();
                responder.Setup(provider => provider.ReplyAsync(It.IsAny<IReadOnlyList<ChatMessage>>(),
                    It.IsAny<CancellationToken>())).ReturnsAsync(new AgentReply("Amsterdam", "test-model", 12, 4));
                services.RemoveAll<IChatResponder>();
                services.AddSingleton(responder.Object);
            });
        }
    }

    private static async Task<JsonElement> Runtime(HttpClient client)
    {
        var runtime = await client.GetFromJsonAsync<JsonElement>("/api/runtime");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", runtime.GetProperty("csrfToken").GetString());
        return runtime;
    }

    [TestMethod]
    public async Task AnonymousConversationRoundTripIncludesModelAndUsageAndIsBrowserScoped()
    {
        using var factory = new AppFactory();
        using var alice = factory.CreateClient();
        var runtime = await Runtime(alice);
        factory.Services.GetRequiredService<TokenCredential>().Should().BeOfType<DefaultAzureCredential>();
        runtime.GetProperty("environment").GetString().Should().Be("Production");
        runtime.ToString().Should().NotContain("EmulatorKey").And.NotContain("TomTomApiKey");
        var created = await alice.PostAsJsonAsync("/api/sessions/", new { title = "Amsterdam trip" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = session.GetProperty("id").GetString();
        var reply = await alice.PostAsJsonAsync($"/api/sessions/{id}/messages",
            new { requestId = Guid.NewGuid(), content = "Where is Amsterdam?" });
        reply.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await alice.GetFromJsonAsync<JsonElement>($"/api/sessions/{id}");
        saved.GetProperty("messages")[1].GetProperty("model").GetString().Should().Be("test-model");
        saved.GetProperty("messages")[1].GetProperty("inputTokens").GetInt64().Should().Be(12);
        using var bob = factory.CreateClient();
        bob.DefaultRequestHeaders.Add("X-MS-CLIENT-PRINCIPAL", "ignored-platform-principal");
        (await bob.GetAsync($"/api/sessions/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var bobSessions = await bob.GetFromJsonAsync<JsonElement>("/api/sessions/");
        bobSessions.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [TestMethod]
    public async Task AnonymousRequestsStillRequireCsrfForMutations()
    {
        using var factory = new AppFactory();
        using var anonymous = factory.CreateClient();
        var sessions = await anonymous.GetFromJsonAsync<JsonElement>("/api/sessions/");
        sessions.GetProperty("items").GetArrayLength().Should().Be(0);
        var response = await anonymous.PostAsJsonAsync("/api/sessions/", new { title = "Unsafe" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("verification token");
    }

    [TestMethod]
    public async Task InvalidBrowserCookieStartsANewAnonymousSession()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", "test-browser-session=invalid-cookie");
        var response = await client.GetAsync("/api/runtime");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Set-Cookie").Should().Contain(value =>
            value.StartsWith("test-browser-session=") && !value.Contains("invalid-cookie"));
        var runtime = await response.Content.ReadFromJsonAsync<JsonElement>();
        runtime.GetProperty("csrfToken").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public async Task ReadinessFailsWhenDatabaseFailsWhileLivenessStaysHealthy()
    {
        using var factory = new AppFactory();
        using var anonymous = factory.CreateClient();
        factory.Store.Healthy = false;
        (await anonymous.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymous.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [TestMethod]
    public async Task ConversationCreationIsNotRateLimited()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateClient();
        await Runtime(client);
        for (var index = 0; index < 25; index++)
            (await client.PostAsJsonAsync("/api/sessions/", new { title = $"Exploration {index}" }))
                .StatusCode.Should().Be(HttpStatusCode.Created);
        var sessions = await client.GetFromJsonAsync<JsonElement>("/api/sessions/");
        sessions.GetProperty("items").GetArrayLength().Should().Be(25);
    }

    [TestMethod]
    [DataRow(true, "https", "203.0.113.10")]
    [DataRow(false, "http", "10.0.0.10")]
    public async Task ForwardedHeadersRespectConfiguredProxyTrust(bool trustAllProxies, string scheme, string address)
    {
        using var factory = new AppFactory(trustAllProxies);
        var context = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.10");
            context.Request.Method = HttpMethods.Get;
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("localhost");
            context.Request.Path = "/api/runtime";
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.10";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        });

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Request.Scheme.Should().Be(scheme);
        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse(address));
        var browserCookie = context.Response.Headers.SetCookie.Single(value =>
            value!.StartsWith("test-browser-session="))!;
        browserCookie.Contains("; secure", StringComparison.OrdinalIgnoreCase).Should().Be(trustAllProxies);
    }

    [TestMethod]
    public async Task AnonymousSignalRConnectionReceivesConversationNotifications()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateClient();
        using var runtimeResponse = await client.GetAsync("/api/runtime");
        var cookie = runtimeResponse.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("test-browser-session=")).Split(';')[0];
        await Runtime(client);
        var negotiation = await client.PostAsync("/hubs/chat/negotiate?negotiateVersion=1", null);
        negotiation.StatusCode.Should().Be(HttpStatusCode.OK);
        var negotiated = await negotiation.Content.ReadFromJsonAsync<JsonElement>();
        negotiated.GetProperty("availableTransports").EnumerateArray().Should().Contain(transport =>
            transport.GetProperty("transport").GetString() == "WebSockets");

        var sockets = factory.Server.CreateWebSocketClient();
        sockets.ConfigureRequest = request => request.Headers["Cookie"] = cookie;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = await sockets.ConnectAsync(new Uri("ws://localhost/hubs/chat"), timeout.Token);
        await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e")),
            WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[4096];
        var handshake = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        Encoding.UTF8.GetString(buffer, 0, handshake.Count).Should().Be("{}\u001e");

        var created = await client.PostAsJsonAsync("/api/sessions/", new { title = "Realtime exploration" });
        var session = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = session.GetProperty("id").GetString();
        var reply = await client.PostAsJsonAsync($"/api/sessions/{id}/messages",
            new { requestId = Guid.NewGuid(), content = "Where is Amsterdam?" });
        reply.StatusCode.Should().Be(HttpStatusCode.OK);

        var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        using var notification = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count).Split('\u001e')[0]);
        notification.RootElement.GetProperty("target").GetString().Should().Be("ConversationChanged");
        notification.RootElement.GetProperty("arguments")[0].GetString().Should().Be(id);
        socket.Abort();
    }
}
