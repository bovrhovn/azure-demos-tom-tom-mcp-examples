using System.Text.Json;
using Azure.AI.Projects;
using Azure.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TT.Web.LocationIntelligence.Features.Chat;
using TT.Web.LocationIntelligence.Features.Chat.Agents;
using TT.Web.LocationIntelligence.Options.Agents;

namespace TT.Web.LocationIntelligence.Features.Maps;

public sealed class FoundryMapsResponder(TokenCredential credential, IOptions<AgentOptions> agentOptions,
    IOptions<MapsOptions> mapsOptions) : IMapsResponder
{
    public async Task RunAsync(string prompt, Func<MapStreamEvent, CancellationToken, Task> publish, CancellationToken ct)
    {
        var settings = agentOptions.Value;
        ValidateConfiguration(settings);
        var maps = mapsOptions.Value;
        await publish(new("status", "Connecting to TomTom Maps"), ct);
        await using var mcp = await ConnectAsync(ct);
        var tools = await mcp.ListToolsAsync(cancellationToken: ct);
        var wrapped = tools.Where(tool => ModelVisible(tool.ProtocolTool))
            .Select(tool => (AITool)new StreamingMapTool(tool, mcp, publish)).ToList();
#pragma warning disable OPENAI001
        using var chatClient = AgentTelemetry.CreateChatClient(new AIProjectClient(new(settings.Endpoint), credential)
            .GetProjectOpenAIClient().GetProjectResponsesClient().AsIChatClient(settings.Model));
#pragma warning restore OPENAI001
        using var agent = AgentTelemetry.CreateAgent(chatClient, new AgentOptions
        {
            Id = maps.Name, Name = maps.Name, Model = settings.Model, Instructions = maps.Instructions
        }, wrapped);
        await publish(new("status", "Planning your journey"), ct);
        var hasText = false;
        await foreach (var update in agent.RunStreamingAsync(prompt, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                hasText = true;
                await publish(new("text", update.Text), ct);
            }
            foreach (var usage in update.Contents.OfType<UsageContent>())
                await publish(new("usage", usage.Details), ct);
        }
        if (!hasText)
            throw new ChatException(502, "The maps agent returned an empty answer.", "maps_unavailable");
    }

    public async Task<object> CallAppToolAsync(string name, Dictionary<string, object?> arguments, CancellationToken ct)
    {
        ValidateConfiguration(agentOptions.Value);
        await using var mcp = await ConnectAsync(ct);
        return await mcp.CallToolAsync(name, arguments, cancellationToken: ct);
    }

    private Task<McpClient> ConnectAsync(CancellationToken ct) =>
        McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = new(mapsOptions.Value.McpEndpoint),
            Name = mapsOptions.Value.Name,
            AdditionalHeaders = new Dictionary<string, string> { ["tomtom-api-key"] = agentOptions.Value.TomTomApiKey }
        }), cancellationToken: ct);

    internal static bool ModelVisible(Tool tool) =>
        tool.Meta?["ui"]?["visibility"] is not System.Text.Json.Nodes.JsonArray visibility ||
        visibility.Any(value => value?.GetValue<string>() == "model");

    private static void ValidateConfiguration(AgentOptions settings)
    {
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != "https" || string.IsNullOrWhiteSpace(settings.Model) ||
            string.IsNullOrWhiteSpace(settings.TomTomApiKey))
            throw new ChatException(503, "Configure the Foundry endpoint, model and TomTom API key on the server.",
                "agent_not_configured");
    }

    internal sealed class StreamingMapTool(McpClientTool tool, McpClient mcp,
        Func<MapStreamEvent, CancellationToken, Task> publish) : DelegatingAIFunction(tool)
    {
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken ct)
        {
            var callId = Guid.NewGuid().ToString("N");
            if (tool.JsonSchema.TryGetProperty("properties", out var properties) &&
                properties.TryGetProperty("show_ui", out _))
                arguments["show_ui"] = true;
            await publish(new("tool-start", new { callId, name = tool.Name, arguments }), ct);
            var result = await tool.CallAsync(arguments, cancellationToken: ct);
            await publish(new("tool-result", new { callId, name = tool.Name, result }), ct);
            var uri = tool.ProtocolTool.Meta?["ui"]?["resourceUri"]?.GetValue<string>()
                ?? tool.ProtocolTool.Meta?["ui/resourceUri"]?.GetValue<string>();
            if (result.IsError is not true && uri is not null && uri.StartsWith("ui://", StringComparison.Ordinal))
            {
                var resource = await mcp.ReadResourceAsync(uri, cancellationToken: ct);
                var content = resource.Contents.OfType<TextResourceContents>()
                    .FirstOrDefault(item => item.MimeType == "text/html;profile=mcp-app");
                if (content is null)
                    throw new ChatException(502, "TomTom returned an unsupported map app resource.", "maps_app_unavailable");
                await publish(new("app", new
                {
                    callId, name = tool.Name, arguments, result,
                    html = content.Text, meta = content.Meta
                }), ct);
            }
            // UI-only metadata can contain large geometry and is not model context.
            return JsonSerializer.SerializeToElement(new
            {
                content = result.Content, structuredContent = result.StructuredContent, isError = result.IsError
            });
        }
    }
}
