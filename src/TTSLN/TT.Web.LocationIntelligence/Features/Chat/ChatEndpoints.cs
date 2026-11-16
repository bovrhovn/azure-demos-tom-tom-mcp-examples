using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.SignalR;

namespace TT.Web.LocationIntelligence.Features.Chat;

/// <summary>Groups realtime subscribers by anonymous browser session.</summary>
public sealed class ChatHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId,
            BrowserSessionMiddleware.Owner(Context.GetHttpContext()
                ?? throw new InvalidOperationException("Chat connections require an HTTP context.")));
        await base.OnConnectedAsync();
    }
}

/// <summary>Minimal HTTP API for conversations. SignalR is notification-only.</summary>
public static class ChatEndpoints
{
    public static void MapChatFeature(this WebApplication app)
    {
        var group = app.MapGroup("/api/sessions");
        group.AddEndpointFilter(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.HttpContext.Request.Method) &&
                !HttpMethods.IsHead(context.HttpContext.Request.Method) &&
                !HttpMethods.IsOptions(context.HttpContext.Request.Method))
            {
                try
                {
                    await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>()
                        .ValidateRequestAsync(context.HttpContext);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.Problem(statusCode: 400, title: "Invalid request verification token.");
                }
            }
            return await next(context);
        });
        group.MapGet("/", (HttpContext context, IChatStore store, string? continuationToken, CancellationToken ct) =>
            store.ListAsync(BrowserSessionMiddleware.Owner(context), continuationToken, ct));
        group.MapPost("/", async (CreateSessionRequest request, HttpContext context, IChatStore store,
            ILogger<ChatSession> logger, CancellationToken ct) =>
        {
            if (request.Title?.Length > SessionTitleLimits.MaxLength)
                return Results.Problem(statusCode: 400, title: $"Titles must not exceed {SessionTitleLimits.MaxLength} characters.");
            var session = await store.CreateAsync(new()
            {
                OwnerId = BrowserSessionMiddleware.Owner(context),
                Title = string.IsNullOrWhiteSpace(request.Title) ? "New exploration" : request.Title.Trim()
            }, ct);
            logger.LogInformation("Created conversation {SessionId}", session.Id);
            return Results.Created($"/api/sessions/{session.Id}", View(session));
        });
        group.MapGet("/{id:guid}", async (string id, HttpContext context, IChatStore store, CancellationToken ct) =>
        {
            var session = await store.GetAsync(BrowserSessionMiddleware.Owner(context), Normalize(id), ct);
            return session is null ? Results.NotFound() : Results.Ok(View(session));
        });
        group.MapPut("/{id:guid}", async Task<Ok<SessionResponse>> (string id, UpdateSessionRequest request,
            HttpContext context, ChatService service, CancellationToken ct) =>
            TypedResults.Ok(View(await service.RenameAsync(
                BrowserSessionMiddleware.Owner(context), Normalize(id), request, ct))))
            .WithName("UpdateSession")
            .WithSummary("Rename a conversation")
            .WithDescription("Updates the browser-owned conversation title when no agent reply is in progress.")
            .ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        group.MapDelete("/{id:guid}", async Task<NoContent> (string id, HttpContext context,
            ChatService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(BrowserSessionMiddleware.Owner(context), Normalize(id), ct);
            return TypedResults.NoContent();
        })
            .WithName("DeleteSession")
            .WithSummary("Delete a conversation")
            .WithDescription("Permanently deletes the browser-owned conversation and its messages when no agent reply is in progress.")
            .ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        group.MapPost("/{id:guid}/messages", async (string id, SendMessageRequest request,
            HttpContext context, ChatService service, CancellationToken ct) =>
            Results.Ok(View(await service.SendAsync(BrowserSessionMiddleware.Owner(context), Normalize(id), request, ct))));
    }

    private static string Normalize(string id) => Guid.Parse(id).ToString("N");
    private static SessionResponse View(ChatSession session) => new(
        session.Id, session.Title, session.CreatedAt, session.UpdatedAt, session.BusyUntil, session.Messages);
}
