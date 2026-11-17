using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using TT.Web.LocationIntelligence.Options;

namespace TT.Web.LocationIntelligence.Features.Chat;

public static class AgentTelemetry
{
    public const string SourceName = "TT.Web.LocationIntelligence.Agent";

    public static IChatClient CreateChatClient(IChatClient chatClient) =>
        chatClient.AsBuilder()
            .UseOpenTelemetry(sourceName: SourceName, configure: telemetry => telemetry.EnableSensitiveData = false)
            .Build();

    public static OpenTelemetryAgent CreateAgent(IChatClient chatClient, AgentOptions settings, IList<AITool> tools) =>
        new(new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Id = settings.Id,
            Name = settings.Name,
            ChatOptions = new()
            {
                ModelId = settings.Model,
                Instructions = settings.Instructions,
                Tools = tools
            }
        }), SourceName) { EnableSensitiveData = false };
}
