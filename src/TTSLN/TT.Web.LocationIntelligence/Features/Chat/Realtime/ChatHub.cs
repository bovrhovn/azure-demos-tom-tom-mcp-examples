using Microsoft.AspNetCore.SignalR;

namespace TT.Web.LocationIntelligence.Features.Chat.Realtime;

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
