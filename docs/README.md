# 📚 Developer documentation

[Repository overview](../README.md)

Run the `TT.Basic` console sample, understand its configuration, and follow how a
Foundry-backed agent uses remote TomTom tools.

| Your goal | Document | Type |
|---|---|---|
| Run your first question | [Getting started](getting-started.md) | Tutorial |
| Run and deploy the web chat workspace | [Location Intelligence](location-intelligence.md) | Developer guide |
| Explore streamed routes and interactive MCP Apps | [Live Maps](location-intelligence.md#live-maps-and-mcp-apps) | Developer guide |
| Compare Handoff and Magentic travel orchestration | [Travel Lab](location-intelligence.md#travel-lab-handoff-and-magentic) | Developer guide |
| Find a setting or obtain its value | [Environment variables](environment-variables.md) | Reference |
| Understand the components and tool boundary | [Architecture](architecture.md) | Explanation |
| Resolve a setup or runtime problem | [Troubleshooting](troubleshooting.md) | How-to |

## Before you begin

- Both `src\TTSLN\TT.Basic` and `src\TTSLN\TT.Web.LocationIntelligence` target .NET 10.
- It calls hosted services; it is not an offline model or a local MCP server.
- Keep secrets out of source control and shared terminal output.
- Configuration is read from the process environment, not automatically from `.env`.
- See the [MIT License](../LICENSE) and [official technology links](../README.md#official-resources).
