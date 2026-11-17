namespace TT.Web.LocationIntelligence.Features.Chat.Contracts;

/// <summary>A continuation-token page of sessions.</summary>
public sealed record SessionPage(IReadOnlyList<SessionSummary> Items, string? ContinuationToken);
