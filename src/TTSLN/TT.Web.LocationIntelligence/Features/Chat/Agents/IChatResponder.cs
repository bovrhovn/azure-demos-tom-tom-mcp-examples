using TT.Web.LocationIntelligence.Features.Chat.Models;

namespace TT.Web.LocationIntelligence.Features.Chat.Agents;

/// <summary>Isolates remote model execution for testing.</summary>
public interface IChatResponder
{
    Task<AgentReply> ReplyAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken);
}
