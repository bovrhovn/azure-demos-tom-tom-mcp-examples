using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using TT.Web.LocationIntelligence.Features.Operations;
using TT.Web.LocationIntelligence.Options.Agents;
using TT.Web.LocationIntelligence.Options.Chat;
using TT.Web.LocationIntelligence.Options.Persistence;

namespace TT.Web.LocationIntelligence.Pages;

public class IndexModel(
    ILogger<IndexModel> logger,
    IAntiforgery antiforgery,
    IHostEnvironment environment,
    IOptions<CosmosOptions> cosmos,
    IOptions<AgentOptions> agent,
    IOptions<ChatOptions> chat) : PageModel
{
    public string RuntimeJson { get; private set; } = "{}";

    public void OnGet()
    {
        logger.LogInformation("Index page requested at {DateLoaded}", DateTime.UtcNow);
        var runtime = OperationsFeature.CreateRuntimeResponse(
            HttpContext, antiforgery, environment, cosmos.Value, agent.Value, chat.Value);
        Response.Headers.CacheControl = "no-store";
        RuntimeJson = OperationsFeature.SerializeForScript(runtime);
    }
}