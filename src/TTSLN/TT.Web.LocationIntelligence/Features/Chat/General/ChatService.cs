using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using TT.Web.LocationIntelligence.Features.Chat.Agents;
using TT.Web.LocationIntelligence.Features.Chat.Contracts;
using TT.Web.LocationIntelligence.Features.Chat.Models;
using TT.Web.LocationIntelligence.Features.Chat.Persistence;
using TT.Web.LocationIntelligence.Features.Chat.Realtime;
using TT.Web.LocationIntelligence.Options.Chat;

namespace TT.Web.LocationIntelligence.Features.Chat;

/// <summary>Persists each turn before publishing it, using a Cosmos lease to serialize replies.</summary>
public sealed class ChatService(IChatStore store, IChatResponder responder, IHubContext<ChatHub> hub,
    IOptions<ChatOptions> options, ILogger<ChatService> logger)
{
    public async Task<ChatSession> RenameAsync(string ownerId, string sessionId, UpdateSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > SessionTitleLimits.MaxLength)
            throw new ChatException(400, $"Supply a title of 1-{SessionTitleLimits.MaxLength} characters.");

        var session = await GetEditableAsync(ownerId, sessionId, cancellationToken);
        var saved = await store.ReplaceAsync(session with
        {
            Title = request.Title.Trim(), UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        logger.LogInformation("Renamed conversation {SessionId}", sessionId);
        await NotifyAsync(ownerId, sessionId, cancellationToken);
        return saved;
    }

    public async Task DeleteAsync(string ownerId, string sessionId, CancellationToken cancellationToken)
    {
        var session = await GetEditableAsync(ownerId, sessionId, cancellationToken);
        await store.DeleteAsync(session, cancellationToken);
        logger.LogInformation("Deleted conversation {SessionId}", sessionId);
        try { await hub.Clients.Group(ownerId).SendAsync("ConversationDeleted", sessionId, cancellationToken); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogWarning(ex, "Realtime deletion notification failed for conversation {SessionId}", sessionId);
        }
    }

    private async Task<ChatSession> GetEditableAsync(string ownerId, string sessionId, CancellationToken cancellationToken)
    {
        var session = await store.GetAsync(ownerId, sessionId, cancellationToken)
            ?? throw new ChatException(404, "Conversation not found.");
        if (session.BusyUntil > DateTimeOffset.UtcNow)
            throw new ChatException(409, "Wait for the agent reply to finish before changing this conversation.");
        return session;
    }

    public async Task<ChatSession> SendAsync(string ownerId, string sessionId, SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        var limits = options.Value;
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.Content) ||
            request.Content.Length > limits.MaxMessageLength)
            throw new ChatException(400, $"Supply a request ID and a message of 1-{limits.MaxMessageLength} characters.");

        var session = await store.GetAsync(ownerId, sessionId, cancellationToken)
            ?? throw new ChatException(404, "Conversation not found.");
        var requestId = request.RequestId.ToString("N");
        if (session.Messages.Any(message => message.Id == requestId)) return session;
        var now = DateTimeOffset.UtcNow;
        if (session.BusyUntil > now)
            throw new ChatException(409, "An agent reply is already in progress.");
        if (session.Messages.Count >= limits.MaxTurns * 2)
            throw new ChatException(400, "This conversation is full. Start a new conversation.");

        var messages = session.Messages.Select(message => message.Status == "pending"
            ? message with { Status = "failed", Content = "The previous reply was interrupted. Please send your question again." }
            : message).ToList();
        messages.Add(new(requestId, "user", request.Content.Trim(), now));
        messages.Add(new(requestId + "-agent", "assistant", "", now, "pending"));
        ChatStorageLimits.Validate(session with { Messages = messages }, reserveReply: true);
        session = await store.ReplaceAsync(session with
        {
            Messages = messages, UpdatedAt = now,
            BusyUntil = now.AddSeconds(limits.ReplyTimeoutSeconds + 30)
        }, cancellationToken);
        await NotifyAsync(ownerId, sessionId, cancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(limits.ReplyTimeoutSeconds));
        // A disconnected browser must not leave the persisted turn permanently pending.
        try
        {
            var reply = await responder.ReplyAsync(messages.Where(message =>
                    message.Role == "user" || message.Status == "completed").ToList(),
                timeout.Token);
            messages[^1] = messages[^1] with
            {
                Content = reply.Content, Model = reply.Model, InputTokens = reply.InputTokens,
                OutputTokens = reply.OutputTokens, Status = "completed", CreatedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogError(ex, "Agent reply failed for conversation {SessionId}", sessionId);
            messages[^1] = messages[^1] with
            {
                Status = "failed", Content = "The agent could not complete this reply. Please send your question again."
            };
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await store.ReplaceAsync(session with
            {
                Messages = messages, BusyUntil = null, UpdatedAt = DateTimeOffset.UtcNow
            }, cleanup.Token);
            await NotifyAsync(ownerId, sessionId, cleanup.Token);
            if (ex is ChatException) throw;
            throw new ChatException(502, "The agent is unavailable or timed out. Your conversation has been saved.");
        }

        using var saveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var saved = await store.ReplaceAsync(session with
        {
            Messages = messages, BusyUntil = null, UpdatedAt = DateTimeOffset.UtcNow
        }, saveTimeout.Token);
        logger.LogInformation("Saved completed turn for conversation {SessionId}", sessionId);
        await NotifyAsync(ownerId, sessionId, saveTimeout.Token);
        return saved;
    }

    private async Task NotifyAsync(string ownerId, string sessionId, CancellationToken cancellationToken)
    {
        try { await hub.Clients.Group(ownerId).SendAsync("ConversationChanged", sessionId, cancellationToken); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A realtime outage must not abandon an already persisted turn; HTTP remains authoritative.
            logger.LogWarning(ex, "Realtime notification failed for conversation {SessionId}", sessionId);
        }
    }
}
