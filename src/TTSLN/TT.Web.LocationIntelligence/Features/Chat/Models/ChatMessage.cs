namespace TT.Web.LocationIntelligence.Features.Chat.Models;

/// <summary>A persisted turn with model-provided usage, never estimated tokens.</summary>
public sealed record ChatMessage(
    string Id, string Role, string Content, DateTimeOffset CreatedAt,
    string Status = "completed", string? Model = null, long? InputTokens = null, long? OutputTokens = null);
