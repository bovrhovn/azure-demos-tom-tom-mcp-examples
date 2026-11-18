using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using TT.Web.LocationIntelligence.Features.Chat;
using TT.Web.LocationIntelligence.Options.Agents;

namespace TT.Web.LocationIntelligence.Features.Travel;

public sealed class TravelHub(ITravelResponder responder, IDataProtectionProvider protection,
    IOptions<TravelOptions> options, TravelRunLimiter limiter, ILogger<TravelHub> logger) : Hub
{
    public async IAsyncEnumerable<TravelEvent> Plan(string ticket,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var owner = BrowserSessionMiddleware.Owner(Context.GetHttpContext()!);
        using var delivery = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Context.ConnectionAborted);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(delivery.Token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        var channel = Channel.CreateBounded<TravelEvent>(32);
        var producer = ProduceAsync();
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(delivery.Token))
                yield return item;
        }
        finally
        {
            await lifetime.CancelAsync();
            await producer;
        }

        async Task ProduceAsync()
        {
            var acquired = false;
            try
            {
                var request = TravelEndpoints.ReadTicket(ticket, owner, protection);
                EuCountries.Validate(request, options.Value.ForecastDays, DateOnly.FromDateTime(DateTime.UtcNow));
                acquired = await limiter.Slots.WaitAsync(0, lifetime.Token);
                if (!acquired)
                    throw new ChatException(429, "Travel planners are busy. Please try again shortly.", "travel_busy");
                await responder.RunAsync(request,
                    (item, ct) => channel.Writer.WriteAsync(item, ct).AsTask(), lifetime.Token);
                await channel.Writer.WriteAsync(new("completed", "system", null), lifetime.Token);
            }
            catch (OperationCanceledException) when (delivery.IsCancellationRequested)
            {
                logger.LogInformation("Travel stream cancelled by its browser");
            }
            catch (Exception ex)
            {
                logger.LogWarning("Travel run failed with {ErrorType}", ex.GetType().Name);
                var message = ex is ChatException chat ? chat.Message :
                    ex is OperationCanceledException ? "Travel planning timed out. Partial results are not a complete itinerary." :
                    "Travel planning failed. Check Foundry, WebIQ and TomTom configuration; partial results are not a complete itinerary.";
                try { await channel.Writer.WriteAsync(new("error", "system", message), delivery.Token); }
                catch (OperationCanceledException) when (delivery.IsCancellationRequested)
                {
                    logger.LogInformation("Travel stream closed before its error could be delivered");
                }
            }
            finally
            {
                if (acquired) limiter.Slots.Release();
                channel.Writer.TryComplete();
            }
        }
    }
}
