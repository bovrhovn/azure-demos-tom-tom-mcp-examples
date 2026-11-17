namespace TT.Web.LocationIntelligence.Features.Chat.Contracts;

/// <summary>A client-generated ID makes turn submissions idempotent.</summary>
public sealed record SendMessageRequest(Guid RequestId, string Content);
