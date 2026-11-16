using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
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
    private sealed class AppFactory : WebApplicationFactory<Program>
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
                    ["Identity:TrustEasyAuth"] = "true"
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

    private static HttpClient Client(AppFactory factory, string? subject = "alice")
    {
        var client = factory.CreateClient();
        if (subject is not null)
        {
            var principal = new { auth_typ = "aad", claims = new[]
            {
                new { typ = "oid", val = subject }, new { typ = "tid", val = "test-tenant" }
            }};
            client.DefaultRequestHeaders.Add("X-MS-CLIENT-PRINCIPAL",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(principal))));
        }
        return client;
    }

    private static async Task<JsonElement> Runtime(HttpClient client)
    {
        var runtime = await client.GetFromJsonAsync<JsonElement>("/api/runtime");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", runtime.GetProperty("csrfToken").GetString());
        return runtime;
    }

    [TestMethod]
    public async Task HttpConversationRoundTripIncludesModelAndUsageAndIsOwnerScoped()
    {
        using var factory = new AppFactory();
        using var alice = Client(factory);
        var runtime = await Runtime(alice);
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
        using var bob = Client(factory, "bob");
        (await bob.GetAsync($"/api/sessions/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var bobSessions = await bob.GetFromJsonAsync<JsonElement>("/api/sessions/");
        bobSessions.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [TestMethod]
    public async Task RejectsMissingAuthenticationAndMissingCsrf()
    {
        using var factory = new AppFactory();
        using var anonymous = Client(factory, null);
        (await anonymous.GetAsync("/api/sessions/")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync("/hubs/chat/negotiate?negotiateVersion=1", null)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        using var authenticated = Client(factory);
        var response = await authenticated.PostAsJsonAsync("/api/sessions/", new { title = "Unsafe" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("verification token");
    }

    [TestMethod]
    public async Task MalformedPlatformPrincipalIsRejected()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-MS-CLIENT-PRINCIPAL", "not-base64");
        (await client.GetAsync("/api/runtime")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task ReadinessFailsWhenDatabaseFailsWhileLivenessStaysHealthy()
    {
        using var factory = new AppFactory();
        using var anonymous = Client(factory, null);
        factory.Store.Healthy = false;
        (await anonymous.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymous.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
