using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Specialized.Magentic;
using Microsoft.Extensions.AI;
using Moq;
using TT.Web.LocationIntelligence.Features.Travel;
using TT.Web.LocationIntelligence.Options.Agents;

namespace TT.Web.LocationIntelligence.Tests;

#pragma warning disable MAAIW001
[TestClass]
public sealed class TravelWorkflowTests
{
    private static Mock<IChatClient> Client()
    {
        var client = new Mock<IChatClient>();
        client.Setup(chat => chat.GetService(typeof(ChatClientMetadata), null))
            .Returns(new ChatClientMetadata("test-provider"));
        return client;
    }

    private static ChatClientAgent Agent(IChatClient client, string name) =>
        new(client, new ChatClientAgentOptions { Id = name, Name = name, ChatOptions = new() { Instructions = "Test specialist" } });

    private static async IAsyncEnumerable<ChatResponseUpdate> Text(string text,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        yield return new(ChatRole.Assistant, text) { ResponseId = Guid.NewGuid().ToString("N") };
        await Task.CompletedTask;
    }

    private static ChatResponse Ledger(bool satisfied) => new(new ChatMessage(ChatRole.Assistant, $$"""
        {
          "is_request_satisfied": { "reason": "Test evidence", "answer": {{satisfied.ToString().ToLowerInvariant()}} },
          "is_in_loop": { "reason": "No loop", "answer": false },
          "is_progress_being_made": { "reason": "Checking evidence", "answer": true },
          "next_speaker": { "reason": "Needs weather", "answer": "WeatherChecker" },
          "instruction_or_question": { "reason": "Verify conditions", "answer": "Check both endpoint forecasts" }
        }
        """));

    private static async IAsyncEnumerable<ChatResponseUpdate> Handoff(Microsoft.Extensions.AI.ChatOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var tool = options.Tools!.Single(tool => tool.Name.StartsWith("handoff_to_", StringComparison.Ordinal));
        yield return new()
        {
            Role = ChatRole.Assistant, ResponseId = Guid.NewGuid().ToString("N"),
            Contents = [new FunctionCallContent(Guid.NewGuid().ToString("N"), tool.Name, new Dictionary<string, object?>())]
        };
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task HandoffActuallyTransfersFromTravelToWeatherToRouteAndPreservesTripContext()
    {
        var calls = new List<string>();
        ChatClientAgent Specialist(string name, string? target)
        {
            var client = Client();
            client.Setup(chat => chat.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<Microsoft.Extensions.AI.ChatOptions>(), It.IsAny<CancellationToken>()))
                .Returns((IEnumerable<ChatMessage> messages, Microsoft.Extensions.AI.ChatOptions? options, CancellationToken ct) =>
                {
                    string.Join("\n", messages.Select(message => message.Text)).Should().Contain("Amsterdam to Berlin");
                    calls.Add(name);
                    return target is not null && options?.Tools?.Any(tool =>
                        tool.Name.StartsWith("handoff_to_", StringComparison.Ordinal)) is true
                        ? Handoff(options, ct) : Text("Route and charging findings", ct);
                });
            return Agent(client.Object, name);
        }
        var travel = Specialist("TravelAssistant", "WeatherChecker");
        var weather = Specialist("WeatherChecker", "RoutePlanner");
        var route = Specialist("RoutePlanner", null);
        var workflow = FoundryTravelResponder.BuildWorkflow("handoff", travel, weather, route, travel, new());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var run = await InProcessExecution.RunStreamingAsync(workflow,
            new List<ChatMessage> { new(ChatRole.User, "Amsterdam to Berlin") }, cancellationToken: timeout.Token);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        var output = false;
        await foreach (var item in run.WatchStreamAsync(timeout.Token))
        {
            if (item is ExecutorFailedEvent failed) Assert.Fail(failed.Data?.ToString() ?? "Executor failed");
            if (item is WorkflowErrorEvent failedWorkflow) Assert.Fail(failedWorkflow.Exception?.ToString() ?? "Workflow failed");
            if (item is WorkflowOutputEvent) output = true;
        }
        calls.Should().ContainInOrder("TravelAssistant", "WeatherChecker", "RoutePlanner");
        output.Should().BeTrue();
    }

    [TestMethod]
    public async Task MagenticActuallyCreatesAPlanDelegatesToWeatherAndSynthesizesTheResult()
    {
        var managerClient = Client();
        var responses = new Queue<ChatResponse>(
        [
            new(new ChatMessage(ChatRole.Assistant, "Known: Amsterdam to Berlin. Need forecasts.")),
            new(new ChatMessage(ChatRole.Assistant, "1. Check both forecasts. 2. Postpone if not sunny.")),
            Ledger(false),
            Ledger(true),
            new(new ChatMessage(ChatRole.Assistant, "Postpone because the origin is rainy."))
        ]);
        var managerContext = new List<string>();
        managerClient.Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<Microsoft.Extensions.AI.ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> messages, Microsoft.Extensions.AI.ChatOptions? _, CancellationToken _) =>
            {
                managerContext.Add(string.Join("\n", messages.Select(message => message.Text)));
                return Task.FromResult(responses.Dequeue());
            });
        var workerClient = Client();
        workerClient.Setup(client => client.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<Microsoft.Extensions.AI.ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> _, Microsoft.Extensions.AI.ChatOptions? _, CancellationToken ct) =>
                Text("Origin is rainy. Destination is cloudy. Postpone.", ct));
        var manager = Agent(managerClient.Object, "TravelManager");
        var weather = Agent(workerClient.Object, "WeatherChecker");
        var route = Agent(workerClient.Object, "RoutePlanner");
        var travel = Agent(workerClient.Object, "TravelAssistant");
        var workflow = FoundryTravelResponder.BuildWorkflow("magentic", travel, weather, route, manager, new());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var run = await InProcessExecution.RunStreamingAsync(workflow,
            new List<ChatMessage> { new(ChatRole.User, "Sunny-only Amsterdam to Berlin") }, cancellationToken: timeout.Token);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        var events = new List<WorkflowEvent>();
        await foreach (var item in run.WatchStreamAsync(timeout.Token))
        {
            if (item is ExecutorFailedEvent failed) Assert.Fail($"{failed.ExecutorId}: {failed.Data}");
            if (item is WorkflowErrorEvent failedWorkflow) Assert.Fail(failedWorkflow.Exception?.ToString() ?? "Workflow failed");
            events.Add(item);
        }
        events.Should().Contain(item => item is MagenticPlanCreatedEvent);
        events.Should().Contain(item => item is MagenticProgressLedgerUpdatedEvent);
        events.Should().Contain(item => item is WorkflowOutputEvent);
        managerContext[3].Should().Contain("Origin is rainy.", "the manager must receive the specialist findings before deciding completion");
        workerClient.Verify(client => client.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<Microsoft.Extensions.AI.ChatOptions>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
#pragma warning restore MAAIW001
