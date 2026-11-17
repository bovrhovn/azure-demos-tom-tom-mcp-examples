using TT.Web.LocationIntelligence.Features.Chat.Contracts;
using TT.Web.LocationIntelligence.Features.Chat.Models;

namespace TT.Web.LocationIntelligence.Features.Chat.Persistence;

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
