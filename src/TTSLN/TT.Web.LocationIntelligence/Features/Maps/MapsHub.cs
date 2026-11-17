using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using TT.Web.LocationIntelligence.Features.Chat;
using TT.Web.LocationIntelligence.Options.Agents;

namespace TT.Web.LocationIntelligence.Features.Maps;

public sealed class MapsHub(IMapsResponder responder, IDataProtectionProvider protection,
    IOptions<MapsOptions> options, ILogger<MapsHub> logger) : Hub
{
    public async IAsyncEnumerable<MapStreamEvent> Query(string ticket,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var owner = BrowserSessionMiddleware.Owner(Context.GetHttpContext()!);
        using var delivery = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Context.ConnectionAborted);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(delivery.Token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        var channel = Channel.CreateBounded<MapStreamEvent>(new BoundedChannelOptions(32)
        {
            SingleReader = true, FullMode = BoundedChannelFullMode.Wait
        });
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
            try
            {
                var query = MapsEndpoints.ReadTicket(ticket, owner, protection);
                await responder.RunAsync(query.Prompt,
                    (item, ct) => channel.Writer.WriteAsync(item, ct).AsTask(), lifetime.Token);
                await channel.Writer.WriteAsync(new("completed", null), lifetime.Token);
            }
            catch (OperationCanceledException) when (delivery.IsCancellationRequested)
            {
                logger.LogInformation("Maps stream cancelled by its browser");
            }
            catch (Exception ex)
            {
                logger.LogWarning("Maps query ended with {ErrorType}", ex.GetType().Name);
                var message = ex is ChatException chat ? chat.Message :
                    ex is OperationCanceledException ? "The maps query timed out or was cancelled." :
                    "The maps agent is unavailable. Check the server configuration and try again.";
                try
                {
                    await channel.Writer.WriteAsync(new("error", message), delivery.Token);
                }
                catch (OperationCanceledException) when (delivery.IsCancellationRequested)
                {
                    logger.LogInformation("Maps stream closed before its terminal error could be delivered");
                }
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }
    }
}
