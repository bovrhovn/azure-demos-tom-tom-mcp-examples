using System.Text.Json;
using TT.Web.LocationIntelligence.Features.Chat;

namespace TT.Web.LocationIntelligence.Tests;

internal sealed class TestStore : IChatStore
{
    private readonly Dictionary<(string Owner, string Id), ChatSession> sessions = [];
    private readonly object gate = new();
    public bool Healthy { get; set; } = true;

    public Task<SessionPage> ListAsync(string ownerId, string? continuationToken, CancellationToken cancellationToken)
    {
        lock (gate)
            return Task.FromResult(new SessionPage(sessions.Values.Where(session => session.OwnerId == ownerId)
                .Select(session => new SessionSummary(session.Id, session.Title, session.UpdatedAt)).ToList(), null));
    }

    public Task<ChatSession?> GetAsync(string ownerId, string id, CancellationToken cancellationToken)
    {
        lock (gate)
            return Task.FromResult(sessions.TryGetValue((ownerId, id), out var session) ? Copy(session) : null);
    }

    public Task<ChatSession> CreateAsync(ChatSession session, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var saved = Copy(session with { ETag = Guid.NewGuid().ToString() });
            sessions.Add((session.OwnerId, session.Id), saved);
            return Task.FromResult(Copy(saved));
        }
    }

    public Task<ChatSession> ReplaceAsync(ChatSession session, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var key = (session.OwnerId, session.Id);
            if (!sessions.ContainsKey(key)) throw new ChatException(404, "Conversation not found.");
            if (sessions[key].ETag != session.ETag) throw new ChatException(409, "ETag conflict");
            sessions[key] = Copy(session with { ETag = Guid.NewGuid().ToString() });
            return Task.FromResult(Copy(sessions[key]));
        }
    }

    public Task DeleteAsync(ChatSession session, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var key = (session.OwnerId, session.Id);
            if (!sessions.TryGetValue(key, out var saved)) throw new ChatException(404, "Conversation not found.");
            if (saved.ETag != session.ETag) throw new ChatException(409, "ETag conflict");
            sessions.Remove(key);
            return Task.CompletedTask;
        }
    }

    public Task CheckHealthAsync(CancellationToken cancellationToken) =>
        Healthy ? Task.CompletedTask : Task.FromException(new IOException("Offline"));

    private static ChatSession Copy(ChatSession session) =>
        JsonSerializer.Deserialize<ChatSession>(JsonSerializer.Serialize(session))!;
}
