using System.Net;
using Azure.Core;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using TT.Web.LocationIntelligence.Options;

namespace TT.Web.LocationIntelligence.Features.Chat;

/// <summary>Uses owner-scoped point reads, projections and conditional writes.</summary>
public sealed class CosmosChatStore(
    CosmosClient client, IOptions<CosmosOptions> options, IOptions<ChatOptions> chatOptions,
    ILogger<CosmosChatStore> logger) : IChatStore
{
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private bool initialized;
    private Container Container => client.GetContainer(options.Value.Database, options.Value.Container);

    /// <summary>Creates one shared client; production never uses account keys or bypasses TLS.</summary>
    public static CosmosClient CreateClient(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<CosmosOptions>>().Value;
        var development = services.GetRequiredService<IHostEnvironment>().IsDevelopment();
        var clientOptions = new CosmosClientOptions
        {
            ApplicationName = "TT.LocationIntelligence",
            UseSystemTextJsonSerializerWithOptions = new(System.Text.Json.JsonSerializerDefaults.Web),
            ConnectionMode = options.UseEmulator ? ConnectionMode.Gateway : ConnectionMode.Direct
        };
        if (development && options.UseEmulator)
        {
            // The Linux emulator exposes a development-only self-signed certificate.
            clientOptions.HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
            ArgumentException.ThrowIfNullOrWhiteSpace(options.EmulatorKey);
            return new CosmosClient(options.Endpoint, options.EmulatorKey, clientOptions);
        }
        return new CosmosClient(options.Endpoint, services.GetRequiredService<TokenCredential>(), clientOptions);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (initialized) return;
        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (initialized) return;
            if (options.Value.UseEmulator)
            {
                var database = await client.CreateDatabaseIfNotExistsAsync(options.Value.Database,
                    cancellationToken: cancellationToken);
                await database.Database.CreateContainerIfNotExistsAsync(
                    new ContainerProperties(options.Value.Container, "/ownerId"),
                    cancellationToken: cancellationToken);
                logger.LogInformation("Initialized development Cosmos database {Database}", options.Value.Database);
            }
            initialized = true;
        }
        finally { initializationLock.Release(); }
    }

    public async Task<SessionPage> ListAsync(string ownerId, string? continuationToken, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        var query = new QueryDefinition(
            "SELECT c.id, c.title, c.updatedAt FROM c WHERE c.ownerId = @owner ORDER BY c.updatedAt DESC")
            .WithParameter("@owner", ownerId);
        using var iterator = Container.GetItemQueryIterator<SessionSummary>(query, continuationToken,
            new QueryRequestOptions { PartitionKey = new(ownerId), MaxItemCount = chatOptions.Value.PageSize });
        var page = await iterator.ReadNextAsync(cancellationToken);
        return new(page.ToList(), page.ContinuationToken);
    }

    public async Task<ChatSession?> GetAsync(string ownerId, string id, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        try
        {
            return (await Container.ReadItemAsync<ChatSession>(id, new(ownerId),
                cancellationToken: cancellationToken)).Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    public async Task<ChatSession> CreateAsync(ChatSession session, CancellationToken cancellationToken)
    {
        ChatStorageLimits.Validate(session);
        await InitializeAsync(cancellationToken);
        return (await Container.CreateItemAsync(session, new(session.OwnerId),
            cancellationToken: cancellationToken)).Resource;
    }

    public async Task<ChatSession> ReplaceAsync(ChatSession session, CancellationToken cancellationToken)
    {
        ChatStorageLimits.Validate(session);
        try
        {
            return (await Container.ReplaceItemAsync(session, session.Id, new(session.OwnerId),
                new ItemRequestOptions { IfMatchEtag = session.ETag }, cancellationToken)).Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new ChatException(409, "This conversation changed. Reload it and try again.");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ChatException(404, "Conversation not found.");
        }
    }

    public async Task DeleteAsync(ChatSession session, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        try
        {
            await Container.DeleteItemAsync<ChatSession>(session.Id, new(session.OwnerId),
                new ItemRequestOptions { IfMatchEtag = session.ETag }, cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new ChatException(409, "This conversation changed. Reload it and try again.");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ChatException(404, "Conversation not found.");
        }
    }

    public async Task CheckHealthAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await Container.ReadContainerAsync(cancellationToken: cancellationToken);
    }
}
