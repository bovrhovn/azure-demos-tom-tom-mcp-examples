using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Azure.Cosmos;
using TT.Web.LocationIntelligence.Features.Chat;

namespace TT.Web.LocationIntelligence.Features.Operations.Errors;

/// <summary>Logs failures and sends sanitized RFC 9457 responses to clients.</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code) = exception switch
        {
            ChatException chat => (chat.StatusCode, chat.Code),
            CosmosException cosmos when cosmos.StatusCode == HttpStatusCode.TooManyRequests => (429, "rate_limited"),
            CosmosException => (503, "database_unavailable"),
            BadHttpRequestException => (400, "invalid_request"),
            _ => (500, "internal_error")
        };
        logger.LogError(exception, "Request failed with status {StatusCode} ({ErrorCode}); trace {TraceId}",
            status, code, context.TraceIdentifier);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new()
            {
                Status = status, Title = exception is ChatException ? exception.Message
                    : status == 503 ? "The database is unavailable." : "The request could not be completed.",
                Extensions =
                {
                    ["code"] = code,
                    ["traceId"] = context.TraceIdentifier
                }
            }
        });
    }
}
