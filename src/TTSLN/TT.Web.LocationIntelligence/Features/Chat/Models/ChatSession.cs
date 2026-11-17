using System.Text.Json.Serialization;

namespace TT.Web.LocationIntelligence.Features.Chat.Models;

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
