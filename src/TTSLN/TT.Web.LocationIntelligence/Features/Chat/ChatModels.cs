using System.Text.Json.Serialization;
using System.Text.Json;

namespace TT.Web.LocationIntelligence.Features.Chat;

/// <summary>A bounded conversation document partitioned by its authenticated owner.</summary>
public sealed record ChatSession
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string OwnerId { get; init; }
    public required string Title { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? BusyUntil { get; init; }
    public List<ChatMessage> Messages { get; init; } = [];
    [JsonPropertyName("_etag")] public string? ETag { get; init; }
}

/// <summary>A persisted turn with model-provided usage, never estimated tokens.</summary>
public sealed record ChatMessage(
    string Id, string Role, string Content, DateTimeOffset CreatedAt,
    string Status = "completed", string? Model = null, long? InputTokens = null, long? OutputTokens = null);

/// <summary>Session list projection excluding conversation content.</summary>
public sealed record SessionSummary(string Id, string Title, DateTimeOffset UpdatedAt);

/// <summary>A continuation-token page of sessions.</summary>
public sealed record SessionPage(IReadOnlyList<SessionSummary> Items, string? ContinuationToken);

/// <summary>Input for a new conversation.</summary>
public sealed record CreateSessionRequest(string? Title);

/// <summary>Input for renaming an existing conversation.</summary>
public sealed record UpdateSessionRequest(string? Title);

/// <summary>Public conversation representation without ownership or storage metadata.</summary>
public sealed record SessionResponse(string Id, string Title, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, DateTimeOffset? BusyUntil, IReadOnlyList<ChatMessage> Messages);

public static class SessionTitleLimits
{
    public const int MaxLength = 120;
}

/// <summary>A client-generated ID makes turn submissions idempotent.</summary>
public sealed record SendMessageRequest(Guid RequestId, string Content);

/// <summary>A provider response containing the actual deployment and optional usage.</summary>
public sealed record AgentReply(string Content, string Model, long? InputTokens, long? OutputTokens);

/// <summary>Persistence boundary with optimistic concurrency.</summary>
public interface IChatStore
{
    Task<SessionPage> ListAsync(string ownerId, string? continuationToken, CancellationToken cancellationToken);
    Task<ChatSession?> GetAsync(string ownerId, string id, CancellationToken cancellationToken);
    Task<ChatSession> CreateAsync(ChatSession session, CancellationToken cancellationToken);
    Task<ChatSession> ReplaceAsync(ChatSession session, CancellationToken cancellationToken);
    Task DeleteAsync(ChatSession session, CancellationToken cancellationToken);
    Task CheckHealthAsync(CancellationToken cancellationToken);
}

/// <summary>Isolates remote model execution for testing.</summary>
public interface IChatResponder
{
    Task<AgentReply> ReplyAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken);
}

/// <summary>A safe, explicit application failure mapped to Problem Details.</summary>
public sealed class ChatException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>Leaves headroom below Cosmos DB's 2 MB item limit, including JSON escaping.</summary>
public static class ChatStorageLimits
{
    public const int MaxReplyCharacters = 16000;
    private const int MaxDocumentBytes = 1_800_000;
    private const int MaxEscapedCharacterBytes = 6;

    public static void Validate(ChatSession session, bool reserveReply = false)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length;
        if (bytes + (reserveReply ? MaxReplyCharacters * MaxEscapedCharacterBytes : 0) > MaxDocumentBytes)
            throw new ChatException(400, "This conversation is full. Start a new conversation.");
    }
}
