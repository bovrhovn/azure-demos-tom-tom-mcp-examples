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
using Moq;
using TT.Web.LocationIntelligence.Features.Chat.Agents;
using TT.Web.LocationIntelligence.Features.Chat.Models;
using TT.Web.LocationIntelligence.Features.Chat.Persistence;
using TT.Web.LocationIntelligence.Features.Operations.Contracts;

namespace TT.Web.LocationIntelligence.Tests;

[TestClass]
public sealed class ApiTests
{
    private sealed class AppFactory(
        bool trustAllProxies = false,
        string? modelName = null,
        int? maxTurns = null,
        int? maxTitleLength = null,
        Func<IReadOnlyList<ChatMessage>, CancellationToken, Task<AgentReply>>? onReply = null) : WebApplicationFactory<Program>
    {
        public TestStore Store { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["Cosmos:Endpoint"] = "https://tests.documents.azure.com",
                    ["Cosmos:UseEmulator"] = "false",
                    ["ReverseProxy:TrustAllProxies"] = trustAllProxies.ToString(),
                    ["BrowserSession:CookieName"] = "test-browser-session",
                    ["Agent:Model"] = modelName ?? ""
                };
                if (maxTurns is not null) settings["Chat:MaxTurns"] = maxTurns.Value.ToString();
                if (maxTitleLength is not null) settings["Chat:MaxTitleLength"] = maxTitleLength.Value.ToString();
                configuration.AddInMemoryCollection(settings);
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChatStore>();
                services.AddSingleton<IChatStore>(Store);
                var responder = new Mock<IChatResponder>();
                responder.Setup(provider => provider.ReplyAsync(It.IsAny<IReadOnlyList<ChatMessage>>(),
                    It.IsAny<CancellationToken>())).Returns((IReadOnlyList<ChatMessage> history, CancellationToken ct) =>
                        onReply is not null
                            ? onReply(history, ct)
                            : Task.FromResult(new AgentReply("Amsterdam", "test-model", 12, 4)));
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
    public async Task RenamePreservesHistoryAndDeleteRemovesConversationAndMessages()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateClient();
        await Runtime(client);
        var created = await client.PostAsJsonAsync("/api/sessions/", new { title = "Original" });
        var original = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = original.GetProperty("id").GetString()!;
        await client.PostAsJsonAsync($"/api/sessions/{id}/messages",
            new { requestId = Guid.NewGuid(), content = "Where is Amsterdam?" });
        var before = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/{id}");

        var renamed = await client.PutAsJsonAsync($"/api/sessions/{Guid.Parse(id):D}", new { title = "  Weekend routes  " });
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await renamed.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("title").GetString().Should().Be("Weekend routes");
        updated.TryGetProperty("ownerId", out _).Should().BeFalse();
        updated.TryGetProperty("eTag", out _).Should().BeFalse();
        updated.GetProperty("messages").ToString().Should().Be(before.GetProperty("messages").ToString());
        updated.GetProperty("createdAt").GetDateTimeOffset().Should().Be(before.GetProperty("createdAt").GetDateTimeOffset());
        var persisted = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/{id}");
        persisted.GetProperty("title").GetString().Should().Be("Weekend routes");
        var page = await client.GetFromJsonAsync<JsonElement>("/api/sessions/");
        page.GetProperty("items")[0].GetProperty("title").GetString().Should().Be("Weekend routes");

        (await client.DeleteAsync($"/api/sessions/{Guid.Parse(id):D}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/sessions/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        page = await client.GetFromJsonAsync<JsonElement>("/api/sessions/");
        page.GetProperty("items").GetArrayLength().Should().Be(0);
        (await client.PostAsJsonAsync($"/api/sessions/{id}/messages",
            new { requestId = Guid.NewGuid(), content = "Gone?" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("over-limit")]
    public async Task InvalidRenameDoesNotChangeStoredConversation(string? title)
    {
        using var factory = new AppFactory();
        using var client = factory.CreateClient();
        var runtime = await Runtime(client);
        var created = await client.PostAsJsonAsync("/api/sessions/", new { title = "Keep me" });
        var session = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = session.GetProperty("id").GetString();
        if (title == "over-limit") title = new string('x', runtime.GetProperty("maxTitleLength").GetInt32() + 1);
        var response = await client.PutAsJsonAsync($"/api/sessions/{id}", new { title });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Contain("Supply a title");
        problem.GetProperty("code").GetString().Should().Be("invalid_title");
        (await client.GetFromJsonAsync<JsonElement>($"/api/sessions/{id}"))
            .GetProperty("title").GetString().Should().Be("Keep me");
    }

    [TestMethod]
    public async Task SessionMutationsRequireCsrfAndCannotModifyAnotherBrowserOrMissingSession()
    {
        using var factory = new AppFactory();
        using var alice = factory.CreateClient();
        await Runtime(alice);
        var created = await alice.PostAsJsonAsync("/api/sessions/", new { title = "Private" });
        var session = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = session.GetProperty("id").GetString();
        foreach (var badToken in new string?[] { null, "tampered-csrf-token" })
        {
            alice.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            if (badToken is not null)
                alice.DefaultRequestHeaders.Add("X-CSRF-TOKEN", badToken);

            foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete })
            {
                using var request = new HttpRequestMessage(method, $"/api/sessions/{id}")
                {
                    Content = method == HttpMethod.Put ? JsonContent.Create(new { title = "Unsafe" }) : null
                };
                var response = await alice.SendAsync(request);
                response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
                problem.GetProperty("code").GetString().Should().Be("csrf_invalid");
            }
        }
        using var bob = factory.CreateClient();
        await Runtime(bob);
        foreach (var target in new[] { id, Guid.NewGuid().ToString("N") })
        {
            var putMissing = await bob.PutAsJsonAsync($"/api/sessions/{target}", new { title = "Foreign" });
            putMissing.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await putMissing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("session_not_found");

            var deleteMissing = await bob.DeleteAsync($"/api/sessions/{target}");
            deleteMissing.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await deleteMissing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("session_not_found");
        }
        (await alice.GetFromJsonAsync<JsonElement>($"/api/sessions/{id}"))
            .GetProperty("title").GetString().Should().Be("Private");
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
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("csrf_invalid");
        problem.GetProperty("title").GetString().Should().Contain("verification token");
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
        // Ensure ChatHub.OnConnectedAsync has finished registering the owner group before publishing HTTP mutations.
        await socket.SendAsync(
            new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":1,\"invocationId\":\"0\",\"target\":\"__sync\",\"arguments\":[]}\u001e")),
            WebSocketMessageType.Text, true, timeout.Token);
        var sync = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        Encoding.UTF8.GetString(buffer, 0, sync.Count).Should().Contain("\"invocationId\":\"0\"");

        var created = await client.PostAsJsonAsync("/api/sessions/", new { title = "Realtime exploration" });
        var session = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = session.GetProperty("id").GetString();
        var reply = await client.PostAsJsonAsync($"/api/sessions/{id}/messages",
            new { requestId = Guid.NewGuid(), content = "Where is Amsterdam?" });
        reply.StatusCode.Should().Be(HttpStatusCode.OK);

        var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        using var notification = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count).Split('\u001e')[0]);
        notification.RootElement.GetProperty("target").GetString().Should().Be("ConversationChanged");
        var arguments = notification.RootElement.GetProperty("arguments");
        arguments.GetArrayLength().Should().Be(2);
        arguments[0].GetString().Should().Be(id);
        arguments[1].GetDateTimeOffset().Should().BeAfter(DateTimeOffset.MinValue);
        socket.Abort();
    }

    [TestMethod]
    public async Task IndexBootstrapEscapesScriptJsonAndSessionsSupportCanonicalPathsETagsAndProblemCodes()
    {
        using var factory = new AppFactory(
            modelName: "gpt-4o</script><script>alert(1)</script><!--",
            maxTurns: 3,
            maxTitleLength: 24);
        using var client = factory.CreateClient();

        using var pageResponse = await client.GetAsync("/");
        pageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        pageResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
        var html = await pageResponse.Content.ReadAsStringAsync();
        html.Should().NotContain("</script><script>");

        const string marker = "<script id=\"tt-runtime\" type=\"application/json\">";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var jsonStart = start + marker.Length;
        var jsonEnd = html.IndexOf("</script>", jsonStart, StringComparison.Ordinal);
        var rawScriptJson = html[jsonStart..jsonEnd];
        rawScriptJson.Should().NotContainEquivalentOf("</script");

        var runtime = JsonSerializer.Deserialize<RuntimeResponse>(
            rawScriptJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        runtime.Should().NotBeNull();
        runtime.Model.Should().Be("gpt-4o</script><script>alert(1)</script><!--");
        runtime.MaxTurns.Should().Be(3);
        runtime.MaxTitleLength.Should().Be(24);
        runtime.Endpoints.Runtime.Should().Be("/api/runtime");
        runtime.Endpoints.Sessions.Should().Be("/api/sessions");
        runtime.Endpoints.ChatHub.Should().Be("/hubs/chat");
        var csrfHeader = runtime.AntiforgeryHeaderName;
        var csrfToken = runtime.CsrfToken;
        csrfToken.Should().NotBeNullOrWhiteSpace();

        using var missingCsrfPost = await client.PostAsJsonAsync("/api/sessions", new { title = "No token" });
        missingCsrfPost.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missingCsrfPost.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("csrf_invalid");

        client.DefaultRequestHeaders.Add(csrfHeader, "invalid-csrf-token");
        using var invalidCsrfPost = await client.PostAsJsonAsync("/api/sessions", new { title = "Bad token" });
        invalidCsrfPost.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalidCsrfPost.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("csrf_invalid");

        client.DefaultRequestHeaders.Remove(csrfHeader);
        client.DefaultRequestHeaders.Add(csrfHeader, csrfToken);

        foreach (var route in new[] { "/api/sessions", "/api/sessions/" })
        {
            using var overlong = await client.PostAsJsonAsync(route, new { title = new string('x', 25) });
            overlong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await overlong.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code").GetString().Should().Be("invalid_title");
        }

        using var created = await client.PostAsJsonAsync("/api/sessions", new { title = "  Canonical route  " });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var initialEtag = created.Headers.ETag?.Tag;
        initialEtag.Should().MatchRegex("^\"[0-9a-f]{16}\"$");
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        createdBody.GetProperty("title").GetString().Should().Be("Canonical route");
        var id = createdBody.GetProperty("id").GetString()!;

        var storedSession = factory.Store.FindById(id)!;
        storedSession.ETag.Should().NotBeNullOrWhiteSpace();
        initialEtag.Should().NotContain(storedSession.ETag!);

        using var unconditionalGet = await client.GetAsync($"/api/sessions/{id}");
        unconditionalGet.StatusCode.Should().Be(HttpStatusCode.OK);
        unconditionalGet.Headers.ETag?.Tag.Should().Be(initialEtag);
        var rawGetBody = await unconditionalGet.Content.ReadAsStringAsync();
        rawGetBody.Should().NotContain(storedSession.ETag!);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/sessions");
        list.GetProperty("items").GetArrayLength().Should().Be(1);

        using var conditionalGet = new HttpRequestMessage(HttpMethod.Get, $"/api/sessions/{id}");
        conditionalGet.Headers.TryAddWithoutValidation("If-None-Match", initialEtag);
        using var notModified = await client.SendAsync(conditionalGet);
        notModified.StatusCode.Should().Be(HttpStatusCode.NotModified);
        notModified.Headers.ETag?.Tag.Should().Be(initialEtag);
        (await notModified.Content.ReadAsStringAsync()).Should().BeEmpty();

        using var renamed = await client.PutAsJsonAsync($"/api/sessions/{id}", new { title = "Updated route" });
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        var etagAfterRename = renamed.Headers.ETag?.Tag;
        etagAfterRename.Should().MatchRegex("^\"[0-9a-f]{16}\"$").And.NotBe(initialEtag);

        using var getAfterRename = new HttpRequestMessage(HttpMethod.Get, $"/api/sessions/{id}");
        getAfterRename.Headers.TryAddWithoutValidation("If-None-Match", initialEtag);
        using var modifiedAfterRename = await client.SendAsync(getAfterRename);
        modifiedAfterRename.StatusCode.Should().Be(HttpStatusCode.OK);
        modifiedAfterRename.Headers.ETag?.Tag.Should().Be(etagAfterRename);

        var requestId = Guid.NewGuid();
        using var sent = await client.PostAsJsonAsync($"/api/sessions/{id}/messages",
            new { requestId, content = "Where is Utrecht?" });
        sent.StatusCode.Should().Be(HttpStatusCode.OK);
        var etagAfterSend = sent.Headers.ETag?.Tag;
        etagAfterSend.Should().MatchRegex("^\"[0-9a-f]{16}\"$")
            .And.NotBe(initialEtag)
            .And.NotBe(etagAfterRename);

        using var getAfterSend = new HttpRequestMessage(HttpMethod.Get, $"/api/sessions/{id}");
        getAfterSend.Headers.TryAddWithoutValidation("If-None-Match", etagAfterRename);
        using var modifiedAfterSend = await client.SendAsync(getAfterSend);
        modifiedAfterSend.StatusCode.Should().Be(HttpStatusCode.OK);
        modifiedAfterSend.Headers.ETag?.Tag.Should().Be(etagAfterSend);

        using var currentConditionalGet = new HttpRequestMessage(HttpMethod.Get, $"/api/sessions/{id}");
        currentConditionalGet.Headers.TryAddWithoutValidation("If-None-Match", etagAfterSend);
        using var notModifiedAfterSend = await client.SendAsync(currentConditionalGet);
        notModifiedAfterSend.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await notModifiedAfterSend.Content.ReadAsStringAsync()).Should().BeEmpty();

        using var missing = await client.GetAsync($"/api/sessions/{Guid.NewGuid():N}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missing.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("session_not_found");
    }

    [TestMethod]
    public async Task CreateSessionEndpointsWithAndWithoutTrailingSlashPublishSignalRNotifications()
    {
        using var factory = new AppFactory(maxTitleLength: 20);
        using var client = factory.CreateClient();
        using var runtimeResponse = await client.GetAsync("/api/runtime");
        var cookie = runtimeResponse.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("test-browser-session=")).Split(';')[0];
        await Runtime(client);

        var sockets = factory.Server.CreateWebSocketClient();
        sockets.ConfigureRequest = request => request.Headers["Cookie"] = cookie;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = await sockets.ConnectAsync(new Uri("ws://localhost/hubs/chat"), timeout.Token);
        await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e")),
            WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[4096];
        _ = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        await socket.SendAsync(
            new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":1,\"invocationId\":\"0\",\"target\":\"__sync\",\"arguments\":[]}\u001e")),
            WebSocketMessageType.Text, true, timeout.Token);
        _ = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);

        foreach (var (route, inputTitle, expectedTitle) in new[]
        {
            ("/api/sessions", "   ", "New exploration"),
            ("/api/sessions/", "  Delft canal  ", "Delft canal")
        })
        {
            using var created = await client.PostAsJsonAsync(route, new { title = inputTitle });
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            var body = await created.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("title").GetString().Should().Be(expectedTitle);
            var sessionId = body.GetProperty("id").GetString()!;
            var updatedAt = body.GetProperty("updatedAt").GetDateTimeOffset();

            var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
            using var notification = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count).Split('\u001e')[0]);
            notification.RootElement.GetProperty("target").GetString().Should().Be("ConversationChanged");
            var args = notification.RootElement.GetProperty("arguments");
            args[0].GetString().Should().Be(sessionId);
            args[1].GetDateTimeOffset().Should().Be(updatedAt);
        }

        socket.Abort();
    }

    [TestMethod]
    public async Task ProblemDetailsExposeStructuredErrorCodesForBusyFullConflictAndMissingSessions()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AgentReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockNext = true;

        using var factory = new AppFactory(
            maxTurns: 1,
            onReply: async (_, _) =>
            {
                if (blockNext)
                {
                    entered.TrySetResult();
                    return await release.Task;
                }
                return new AgentReply("Done", "test-model", 8, 4);
            });
        using var client = factory.CreateClient();
        await Runtime(client);

        using var created = await client.PostAsJsonAsync("/api/sessions", new { title = "Error codes" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var firstRequestId = Guid.NewGuid();

        var inFlightSend = client.PostAsJsonAsync($"/api/sessions/{id}/messages",
            new { requestId = firstRequestId, content = "First turn" });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            using var busySend = await client.PostAsJsonAsync($"/api/sessions/{id}/messages",
                new { requestId = Guid.NewGuid(), content = "Concurrent turn" });
            busySend.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var busyProblem = await busySend.Content.ReadFromJsonAsync<JsonElement>();
            busyProblem.GetProperty("code").GetString().Should().Be("session_busy");
            busyProblem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();

            using var busyRename = await client.PutAsJsonAsync($"/api/sessions/{id}", new { title = "While busy" });
            busyRename.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await busyRename.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code").GetString().Should().Be("session_busy");
        }
        finally
        {
            blockNext = false;
            release.TrySetResult(new AgentReply("First reply", "test-model", 10, 5));
        }

        (await inFlightSend).StatusCode.Should().Be(HttpStatusCode.OK);

        using var conflict = await client.PostAsJsonAsync($"/api/sessions/{id}/messages",
            new { requestId = firstRequestId, content = "Different prompt with reused requestId" });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("request_conflict");

        using var full = await client.PostAsJsonAsync($"/api/sessions/{id}/messages",
            new { requestId = Guid.NewGuid(), content = "Second turn past maxTurns=1" });
        full.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await full.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("session_full");

        using var notFound = await client.PostAsJsonAsync($"/api/sessions/{Guid.NewGuid():N}/messages",
            new { requestId = Guid.NewGuid(), content = "No such session" });
        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await notFound.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("session_not_found");
    }
}
