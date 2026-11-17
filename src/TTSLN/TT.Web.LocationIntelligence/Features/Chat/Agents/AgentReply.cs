namespace TT.Web.LocationIntelligence.Features.Chat.Agents;

/// <summary>A provider response containing the actual deployment and optional usage.</summary>
public sealed record AgentReply(string Content, string Model, long? InputTokens, long? OutputTokens);
