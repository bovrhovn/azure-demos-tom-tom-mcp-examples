using TT.Web.LocationIntelligence.Features.Chat.Models;

namespace TT.Web.LocationIntelligence.Features.Chat.Contracts;

/// <summary>Public conversation representation without ownership or storage metadata.</summary>
public sealed record SessionResponse(string Id, string Title, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, DateTimeOffset? BusyUntil, IReadOnlyList<ChatMessage> Messages);
