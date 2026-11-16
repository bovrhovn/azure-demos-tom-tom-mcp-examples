using Azure.AI.Projects;
using Azure.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using TT.Web.LocationIntelligence.Options;

namespace TT.Web.LocationIntelligence.Features.Chat;

/// <summary>Runs a Foundry agent with TomTom tools and persisted conversation context.</summary>
public sealed class FoundryChatResponder(TokenCredential credential, IOptions<AgentOptions> options,
    ILogger<FoundryChatResponder> logger) : IChatResponder
{
    public async Task<AgentReply> ReplyAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != "https" || string.IsNullOrWhiteSpace(settings.Model) ||
            string.IsNullOrWhiteSpace(settings.TomTomApiKey))
        {
            throw new ChatException(503, "Configure the Foundry endpoint, model and TomTom API key on the server.");
        }

        await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = new(settings.McpEndpoint),
            Name = "TomTom Maps",
            AdditionalHeaders = new Dictionary<string, string> { ["tomtom-api-key"] = settings.TomTomApiKey }
        }), cancellationToken: cancellationToken);
        var tools = await mcp.ListToolsAsync(cancellationToken: cancellationToken);
        var agent = new AIProjectClient(endpoint, credential).AsAIAgent(
            model: settings.Model, instructions: settings.Instructions, tools: [.. tools], name: "LocationIntelligence");
        var history = messages.Where(message => message.Role == "user" || message.Status == "completed")
            .Select(message => new Microsoft.Extensions.AI.ChatMessage(
                message.Role == "user" ? ChatRole.User : ChatRole.Assistant, message.Content));
        var response = await agent.RunAsync(history, cancellationToken: cancellationToken);
        var text = response.ToString();
        if (string.IsNullOrWhiteSpace(text))
            throw new ChatException(502, "The agent returned an empty reply.");
        if (text.Length > ChatStorageLimits.MaxReplyCharacters)
            throw new ChatException(502, "The agent reply exceeded the conversation storage limit.");
        logger.LogInformation("Agent completed with deployment {Model}; input {InputTokens}, output {OutputTokens}",
            settings.Model, response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount);
        return new(text, settings.Model, response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount);
    }
}
