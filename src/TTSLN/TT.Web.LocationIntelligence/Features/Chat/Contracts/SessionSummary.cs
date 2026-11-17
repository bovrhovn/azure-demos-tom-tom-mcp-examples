namespace TT.Web.LocationIntelligence.Features.Chat.Contracts;

/// <summary>Session list projection excluding conversation content.</summary>
public sealed record SessionSummary(string Id, string Title, DateTimeOffset UpdatedAt);
