using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TT.Web.LocationIntelligence.Features.Chat;
using TT.Web.LocationIntelligence.Options;

namespace TT.Web.LocationIntelligence.Tests;

[TestClass]
public sealed class CosmosTests
{
    [TestMethod]
    [TestCategory("Cosmos")]
    public async Task EmulatorPersistsAcrossClientsEnforcesOwnerPartitionAndRejectsStaleEtag()
    {
        if (Environment.GetEnvironmentVariable("RUN_COSMOS_TESTS") != "true")
            Assert.Inconclusive("Set RUN_COSMOS_TESTS=true with the HTTPS emulator running.");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cosmos:Database"] = "LocationIntelligenceTests",
                ["Cosmos:Container"] = "Conversations",
                ["Cosmos:Endpoint"] = Environment.GetEnvironmentVariable("TEST_COSMOS_ENDPOINT") ?? "https://localhost:8081"
            }).Build();
        WebApplication CreateApplication()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.Services.AddOptions<CosmosOptions>().Bind(configuration.GetSection("Cosmos"));
            builder.Services.AddOptions<ChatOptions>();
            builder.Services.AddSingleton(CosmosChatStore.CreateClient);
            builder.Services.AddSingleton<IChatStore, CosmosChatStore>();
            return builder.Build();
        }
        await using var application = CreateApplication();
        var store = application.Services.GetRequiredService<IChatStore>();
        var owner = Guid.NewGuid().ToString("N");
        var created = await store.CreateAsync(new() { OwnerId = owner, Title = "Persistence verification" }, default);
        try
        {
            await store.ReplaceAsync(created with
            {
                Messages = [new("turn-1", "assistant", "Persisted answer", DateTimeOffset.UtcNow,
                    Model: "test-model", InputTokens: 10, OutputTokens: 5)]
            }, default);
            await using var second = CreateApplication();
            var secondStore = second.Services.GetRequiredService<IChatStore>();
            var reloaded = await secondStore.GetAsync(owner, created.Id, default);
            reloaded!.Messages.Single().Content.Should().Be("Persisted answer");
            reloaded.Messages.Single().Model.Should().Be("test-model");
            reloaded.Messages.Single().OutputTokens.Should().Be(5);
            (await secondStore.GetAsync("different-owner", created.Id, default)).Should().BeNull();
            var page = await secondStore.ListAsync(owner, null, default);
            page.Items.Should().ContainSingle(item => item.Id == created.Id);
            var staleWrite = () => secondStore.ReplaceAsync(created with { Title = "Stale" }, default);
            (await staleWrite.Should().ThrowAsync<ChatException>()).Which.StatusCode.Should().Be(409);
        }
        finally
        {
            var client = application.Services.GetRequiredService<Microsoft.Azure.Cosmos.CosmosClient>();
            await client.GetContainer("LocationIntelligenceTests", "Conversations")
                .DeleteItemAsync<ChatSession>(created.Id, new(owner));
        }
    }
}
