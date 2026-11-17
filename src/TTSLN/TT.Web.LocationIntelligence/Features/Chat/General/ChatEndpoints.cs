using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using TT.Web.LocationIntelligence.Features.Chat.Contracts;
using TT.Web.LocationIntelligence.Features.Chat.Models;

namespace TT.Web.LocationIntelligence.Features.Chat;

/// <summary>Minimal HTTP API for conversations. SignalR is notification-only.</summary>
public static class ChatEndpoints
{
    public const string SessionsPath = "/api/sessions";
    public const string HubPath = "/hubs/chat";

    public static void MapChatFeature(this WebApplication app)
    {
        var group = app.MapGroup(SessionsPath);
        group.AddEndpointFilter(ValidateCsrfAsync);

        group.MapGet("", async Task<Ok<SessionPage>> (HttpContext context, ChatService service,
            string? continuationToken, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(BrowserSessionMiddleware.Owner(context), continuationToken, ct)))
            .WithName("ListSessions")
            .WithSummary("List browser-owned conversations")
            .WithDescription("Returns a content-free continuation page of conversation summaries.")
            .ProducesProblem(400);

        group.MapPost("", async Task<Created<SessionResponse>> (CreateSessionRequest request,
            HttpContext context, ChatService service, CancellationToken ct) =>
        {
            var session = await service.CreateAsync(BrowserSessionMiddleware.Owner(context), request.Title, ct);
            SetETag(context, session);
            return TypedResults.Created($"{SessionsPath}/{session.Id}", View(session));
        })
            .WithName("CreateSession")
            .WithSummary("Start a new conversation")
            .WithDescription("Creates an anonymous browser-owned conversation and publishes a realtime notification.")
            .ProducesProblem(400);

        group.MapGet("/{id:guid}", async Task<Results<Ok<SessionResponse>, StatusCodeHttpResult>> (string id,
            HttpContext context, ChatService service, CancellationToken ct) =>
        {
            var session = await service.GetAsync(BrowserSessionMiddleware.Owner(context), Normalize(id), ct);
            var etag = SetETag(context, session);
            context.Response.Headers.CacheControl = "private, no-cache";
            if (SessionETag.Matches(context.Request.Headers.IfNoneMatch, etag))
                return TypedResults.StatusCode(StatusCodes.Status304NotModified);
            return TypedResults.Ok(View(session));
        })
            .WithName("GetSession")
            .WithSummary("Get a conversation and its messages")
            .WithDescription("Returns the conversation when modified or 304 Not Modified when If-None-Match matches.")
            .ProducesProblem(404);

        group.MapPut("/{id:guid}", async Task<Ok<SessionResponse>> (string id, UpdateSessionRequest request,
            HttpContext context, ChatService service, CancellationToken ct) =>
        {
            var session = await service.RenameAsync(BrowserSessionMiddleware.Owner(context), Normalize(id), request, ct);
            SetETag(context, session);
            return TypedResults.Ok(View(session));
        })
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

        group.MapPost("/{id:guid}/messages", async Task<Ok<SessionResponse>> (string id, SendMessageRequest request,
            HttpContext context, ChatService service, CancellationToken ct) =>
        {
            var session = await service.SendAsync(BrowserSessionMiddleware.Owner(context), Normalize(id), request, ct);
            SetETag(context, session);
            return TypedResults.Ok(View(session));
        })
            .WithName("SendMessage")
            .WithSummary("Submit an idempotent conversation turn")
            .WithDescription("Persists the user message and lease before invoking Foundry, then saves the completed or failed reply.")
            .ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).ProducesProblem(502).ProducesProblem(503);
    }

    private static async ValueTask<object?> ValidateCsrfAsync(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method))
        {
            try
            {
                await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>()
                    .ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                throw new ChatException(StatusCodes.Status400BadRequest,
                    "Invalid request verification token.", "csrf_invalid");
            }
        }
        return await next(context);
    }

    private static string SetETag(HttpContext context, ChatSession session)
    {
        var etag = SessionETag.Compute(session);
        context.Response.Headers.ETag = etag;
        return etag;
    }

    private static string Normalize(string id) => Guid.Parse(id).ToString("N");
    private static SessionResponse View(ChatSession session) => new(
        session.Id, session.Title, session.CreatedAt, session.UpdatedAt, session.BusyUntil, session.Messages);
}
