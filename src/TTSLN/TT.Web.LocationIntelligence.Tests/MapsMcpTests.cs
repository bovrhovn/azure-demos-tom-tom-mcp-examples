using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TT.Web.LocationIntelligence.Features.Maps;

namespace TT.Web.LocationIntelligence.Tests;

[TestClass]
public sealed class MapsMcpTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task RealMcpToolInvocationPreservesUiMetadataAndFetchesDeclaredApp(bool legacyMetadata, bool toolError)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        IDictionary<string, JsonElement>? receivedArguments = null;
        var readUris = new List<string>();
        var uiMeta = new JsonObject
        {
            ["ui"] = new JsonObject
            {
                ["csp"] = new JsonObject { ["resourceDomains"] = new JsonArray("https://cdn.jsdelivr.net") }
            }
        };
        var protocolTool = new Tool
        {
            Name = "tomtom-routing",
            Description = "Calculate a route",
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object", properties = new { show_ui = new { type = "boolean" } }
            }),
            Meta = legacyMetadata
                ? new JsonObject { ["ui/resourceUri"] = "ui://test/route.html" }
                : new JsonObject { ["ui"] = new JsonObject { ["resourceUri"] = "ui://test/route.html" } }
        };
        await using var server = McpServer.Create(new StreamServerTransport(
            clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()), new McpServerOptions
        {
            ServerInfo = new() { Name = "maps-test", Version = "1.0.0" },
            Handlers = new()
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = [protocolTool] }),
                CallToolHandler = (request, _) =>
                {
                    receivedArguments = request.Params!.Arguments;
                    return ValueTask.FromResult(new CallToolResult
                    {
                        IsError = toolError,
                        Content = [new TextContentBlock { Text = "{\"distance\":650}" }],
                        Meta = new JsonObject { ["appData"] = "full route geometry" }
                    });
                },
                ReadResourceHandler = (request, _) =>
                {
                    readUris.Add(request.Params!.Uri);
                    return ValueTask.FromResult(new ReadResourceResult
                    {
                        Contents = [new TextResourceContents
                        {
                            Uri = request.Params.Uri, MimeType = "text/html;profile=mcp-app",
                            Text = "<!doctype html><html><body>Real MCP resource</body></html>", Meta = uiMeta
                        }]
                    });
                }
            }
        });
        var running = server.RunAsync(lifetime.Token);
        try
        {
            await using var client = await McpClient.CreateAsync(new StreamClientTransport(
                clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()), cancellationToken: lifetime.Token);
            var tool = (await client.ListToolsAsync(cancellationToken: lifetime.Token)).Single();
            var events = new List<MapStreamEvent>();
            var wrapped = new FoundryMapsResponder.StreamingMapTool(tool, client, (item, _) =>
            {
                events.Add(item);
                return Task.CompletedTask;
            });
            var modelResult = (JsonElement)(await wrapped.InvokeAsync(
                new AIFunctionArguments { ["show_ui"] = false }, lifetime.Token))!;

            receivedArguments!["show_ui"].GetBoolean().Should().BeTrue();
            events.Select(item => item.Type).Should().Equal(toolError
                ? ["tool-start", "tool-result"] : ["tool-start", "tool-result", "app"]);
            var streamedResult = JsonSerializer.SerializeToElement(events[1].Data).GetProperty("result");
            streamedResult.GetProperty("_meta").GetProperty("appData").GetString().Should().Be("full route geometry");
            modelResult.GetProperty("isError").GetBoolean().Should().Be(toolError);
            modelResult.TryGetProperty("_meta", out _).Should().BeFalse();
            if (toolError)
                readUris.Should().BeEmpty();
            else
            {
                readUris.Should().Equal("ui://test/route.html");
                var app = JsonSerializer.SerializeToElement(events[2].Data);
                app.GetProperty("html").GetString().Should().Contain("Real MCP resource");
                app.GetProperty("meta").GetProperty("ui").GetProperty("csp").GetProperty("resourceDomains")[0]
                    .GetString().Should().Be("https://cdn.jsdelivr.net");
                app.GetProperty("result").GetProperty("_meta").GetProperty("appData").GetString()
                    .Should().Be("full route geometry");
            }
        }
        finally
        {
            await lifetime.CancelAsync();
            await running;
        }
    }

    [TestMethod]
    public void AppOnlyToolsAreNotPresentedToTheModel()
    {
        var tool = new Tool
        {
            Name = "tomtom-get-api-key",
            Meta = new JsonObject { ["ui"] = new JsonObject { ["visibility"] = new JsonArray("app") } }
        };
        FoundryMapsResponder.ModelVisible(tool).Should().BeFalse();
        tool.Meta = null;
        FoundryMapsResponder.ModelVisible(tool).Should().BeTrue();
        tool.Meta = new JsonObject { ["ui"] = new JsonObject { ["visibility"] = new JsonArray("model", "app") } };
        FoundryMapsResponder.ModelVisible(tool).Should().BeTrue();
    }
}
