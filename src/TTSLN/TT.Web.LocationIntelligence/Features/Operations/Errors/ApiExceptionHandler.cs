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
        var status = exception switch
        {
            ChatException chat => chat.StatusCode,
            CosmosException cosmos when cosmos.StatusCode == HttpStatusCode.TooManyRequests => 429,
            CosmosException => 503,
            BadHttpRequestException => 400,
            _ => 500
        };
        logger.LogError(exception, "Request failed with status {StatusCode}; trace {TraceId}",
            status, context.TraceIdentifier);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new()
            {
                Status = status, Title = exception is ChatException ? exception.Message
                    : status == 503 ? "The database is unavailable." : "The request could not be completed.",
                Extensions = { ["traceId"] = context.TraceIdentifier }
            }
        });
    }
}
