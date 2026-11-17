using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TT.Web.LocationIntelligence.Configuration;
using TT.Web.LocationIntelligence.Features.Chat.Agents;
using TT.Web.LocationIntelligence.Options.Agents;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace TT.Web.LocationIntelligence.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AgentTelemetryTests
{
    private static AgentOptions Settings() => new()
    {
        Id = "stable-test-agent",
        Name = "Route assistant",
        Model = "test-deployment",
        Instructions = "private-system-instructions"
    };

    private static Mock<IChatClient> ChatClient()
    {
        var client = new Mock<IChatClient>();
        client.Setup(chat => chat.GetService(typeof(ChatClientMetadata), null))
            .Returns(new ChatClientMetadata("test-provider", new Uri("https://model.example"), "test-deployment"));
        return client;
    }

    private sealed class ActivityCollector : BaseProcessor<Activity>
    {
        public List<Activity> Activities { get; } = [];
        public override void OnEnd(Activity activity) => Activities.Add(activity);
    }

    [TestMethod]
    public async Task AgentModelAndToolSpansAreCorrelatedAndExcludeContent()
    {
        var capture = Environment.GetEnvironmentVariable("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT");
        Environment.SetEnvironmentVariable("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", "true");
        try
        {
            var collector = new ActivityCollector();
            using var provider = Sdk.CreateTracerProviderBuilder().AddSource(AgentTelemetry.SourceName)
                .AddProcessor(collector).Build();
            using var meter = new MeterListener();
            var measurements = new List<string>();
            meter.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AgentTelemetry.SourceName)
                    listener.EnableMeasurementEvents(instrument);
            };
            meter.SetMeasurementEventCallback<double>((instrument, _, _, _) => measurements.Add(instrument.Name));
            meter.SetMeasurementEventCallback<long>((instrument, _, _, _) => measurements.Add(instrument.Name));
            meter.SetMeasurementEventCallback<int>((instrument, _, _, _) => measurements.Add(instrument.Name));
            meter.Start();

            var client = ChatClient();
            client.SetupSequence(chat => chat.GetResponseAsync(It.IsAny<IEnumerable<AIChatMessage>>(),
                    It.IsAny<Microsoft.Extensions.AI.ChatOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatResponse(new AIChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent("call-1", "lookup", new Dictionary<string, object?>
                    {
                        ["location"] = "private-tool-argument"
                    })]))
                {
                    ModelId = "test-deployment",
                    Usage = new() { InputTokenCount = 10, OutputTokenCount = 2 }
                })
                .ReturnsAsync(new ChatResponse(new AIChatMessage(ChatRole.Assistant, "private-model-reply"))
                {
                    ModelId = "test-deployment",
                    Usage = new() { InputTokenCount = 20, OutputTokenCount = 4 }
                });
            string? invokedWith = null;
            var tool = AIFunctionFactory.Create((string location) =>
            {
                invokedWith = location;
                return "private-tool-result";
            }, "lookup");
            using var chatClient = AgentTelemetry.CreateChatClient(client.Object);
            using var agent = AgentTelemetry.CreateAgent(chatClient, Settings(), [tool]);

            using var request = new Activity("request");
            request.ActivityTraceFlags = ActivityTraceFlags.Recorded;
            request.Start();
            var reply = await agent.RunAsync("private-user-prompt");

            invokedWith.Should().Be("private-tool-argument");
            reply.Text.Should().Be("private-model-reply");
            var invocation = collector.Activities.Should().ContainSingle(activity =>
                Equals(activity.GetTagItem("gen_ai.operation.name"), "invoke_agent")).Subject;
            invocation.GetTagItem("gen_ai.agent.id").Should().Be("stable-test-agent");
            invocation.GetTagItem("gen_ai.agent.name").Should().Be("Route assistant");
            invocation.ParentSpanId.Should().Be(request.SpanId);
            invocation.GetTagItem("gen_ai.usage.input_tokens").Should().Be(30L);
            invocation.GetTagItem("gen_ai.usage.output_tokens").Should().Be(6L);
            collector.Activities.Should().Contain(activity =>
                Equals(activity.GetTagItem("gen_ai.operation.name"), "chat")
                && Equals(activity.GetTagItem("gen_ai.request.model"), "test-deployment"));
            collector.Activities.Should().Contain(activity =>
                Equals(activity.GetTagItem("gen_ai.operation.name"), "execute_tool")
                && Equals(activity.GetTagItem("gen_ai.tool.name"), "lookup"));
            collector.Activities.Should().OnlyContain(activity => activity.TraceId == invocation.TraceId);
            var exported = string.Join("\n", collector.Activities.Select(activity =>
                $"{activity.DisplayName} {string.Join(" ", activity.TagObjects)} " +
                string.Join(" ", activity.Events.Select(item => $"{item.Name} {string.Join(" ", item.Tags)}"))));
            exported.Should().NotContain("private-");
            measurements.Should().Contain("gen_ai.client.token.usage");
            measurements.Should().Contain("gen_ai.client.operation.duration");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", capture);
        }
    }

    [TestMethod]
    public async Task ProviderFailuresAreTracedAndRethrownWithoutInventingUsage()
    {
        var collector = new ActivityCollector();
        using var provider = Sdk.CreateTracerProviderBuilder().AddSource(AgentTelemetry.SourceName)
            .AddProcessor(collector).Build();
        var client = ChatClient();
        client.Setup(chat => chat.GetResponseAsync(It.IsAny<IEnumerable<AIChatMessage>>(),
                It.IsAny<Microsoft.Extensions.AI.ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Provider unavailable"));
        using var chatClient = AgentTelemetry.CreateChatClient(client.Object);
        using var agent = AgentTelemetry.CreateAgent(chatClient, Settings(), []);

        var run = () => agent.RunAsync("Find a route");
        await run.Should().ThrowAsync<HttpRequestException>();

        var invocation = collector.Activities.Should().ContainSingle(activity =>
            Equals(activity.GetTagItem("gen_ai.operation.name"), "invoke_agent")).Subject;
        invocation.Status.Should().Be(ActivityStatusCode.Error);
        invocation.GetTagItem("error.type").Should().NotBeNull();
        invocation.GetTagItem("gen_ai.usage.input_tokens").Should().BeNull();
        invocation.GetTagItem("gen_ai.usage.output_tokens").Should().BeNull();
    }

    [TestMethod]
    [DataRow("ApplicationInsights:ConnectionString")]
    [DataRow("APPLICATIONINSIGHTS_CONNECTION_STRING")]
    public void ConfiguredInsightsPipelineSubscribesToAgentSourceAndMeter(string key)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [key] = "InstrumentationKey=00000000-0000-0000-0000-000000000001;" +
                "IngestionEndpoint=http://127.0.0.1:1/;LiveEndpoint=http://127.0.0.1:1/"
        });
        builder.AddLocationIntelligence();
        builder.Services.Configure<ApplicationInsightsServiceOptions>(options =>
            options.EnableQuickPulseMetricStream = false);
        var collector = new ActivityCollector();
        builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddProcessor(collector));
        using var app = builder.Build();
        app.Services.GetRequiredService<TracerProvider>();
        app.Services.GetRequiredService<MeterProvider>();
        using var source = new ActivitySource(AgentTelemetry.SourceName);
        using var meter = new Meter(AgentTelemetry.SourceName);
        var histogram = meter.CreateHistogram<double>("test.agent.duration");

        using (source.StartActivity("test agent run")) { }

        collector.Activities.Should().ContainSingle(activity => activity.DisplayName == "test agent run");
        histogram.Enabled.Should().BeTrue();
    }

    [TestMethod]
    public void MissingConnectionStringLeavesTelemetryExportDisabled()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.AddLocationIntelligence();
        using var app = builder.Build();

        app.Services.GetService<TracerProvider>().Should().BeNull();
        app.Services.GetService<MeterProvider>().Should().BeNull();
    }
}
