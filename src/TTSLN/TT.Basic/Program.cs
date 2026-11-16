using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using ModelContextProtocol.Client;
using Spectre.Console;
using TT.Basic;

AnsiConsole.MarkupLine("[yellow]Hello TOM TOM MCP server![/]");

#region ENV variables

const string TomTomMCPRemoteUrl = "https://mcp.tomtom.com/maps?apps=false";
AnsiConsole.MarkupLine("[green]MCP Remote URL:[/]" + TomTomMCPRemoteUrl);
var endpoint = Environment.GetEnvironmentVariable("ENDPOINT");
ArgumentException.ThrowIfNullOrEmpty(endpoint,
    "Please set the ENDPOINT environment variable to your Azure OpenAI endpoint URL.");
AnsiConsole.MarkupLine("[green]Endpoint:[/]" + endpoint);
var deploymentName = Environment.GetEnvironmentVariable("DEPLOYMENT_NAME");
ArgumentException.ThrowIfNullOrEmpty(deploymentName,
    "Please set the DEPLOYMENT_NAME environment variable to your Azure OpenAI endpoint URL.");
AnsiConsole.MarkupLine("[green]Deployment name:[/]" + deploymentName);
var ttApiKey = Environment.GetEnvironmentVariable("TT_APIKEY");
ArgumentException.ThrowIfNullOrEmpty(ttApiKey,
    "Please set the TT_APIKEY environment variable to your Azure OpenAI endpoint URL.");
AnsiConsole.MarkupLine(
    $"API Key: [grey]{CliHelpers.MaskSecret(ttApiKey)}[/]");

#endregion

//
// 1. Connect to TomTom MCP
//

var transportOptions = new HttpClientTransportOptions
{
    Endpoint = new Uri(TomTomMCPRemoteUrl),
    Name = "TomTom Maps MCP",
    AdditionalHeaders = new Dictionary<string, string>
    {
        ["tomtom-api-key"] = ttApiKey
    }
};

await using var mcpClient =
    await McpClient.CreateAsync(new HttpClientTransport(transportOptions));

//
// 2. Get MCP tools
//

var mcpTools = await mcpClient.ListToolsAsync();

AnsiConsole.MarkupLine(
    "[green]TomTom MCP connected[/] - {0} tools",
    mcpTools.Count);

foreach (var tool in mcpTools)
{
    AnsiConsole.MarkupLine("[grey]-[/] {0}", Markup.Escape(tool.Name));
}
//
// 3. Assign tools to an agent
//
AIAgent agent = new AIProjectClient(new Uri(endpoint),
        new DefaultAzureCredential())
    .AsAIAgent(
        model: deploymentName,
        instructions: "You are a friendly assistant with TomTom MCP remote server. Keep your answers brief.",
        tools: [.. mcpTools],
        name: "TomTomBasic");
//
// 4. Execute the agent with a question to invoke the tools and get an answer
//
var question = AnsiConsole.Ask<string>("Ask your question",
    "What's at the coordinates 52.3676, 4.9041?");
AnsiConsole.MarkupLine("[green]Question:[/]" + question);
var answer = await agent.RunAsync(question);
AnsiConsole.MarkupLine("[green]Answer:[/]" + answer);