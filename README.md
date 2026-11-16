# 🗺️ TomTom MCP + Microsoft Foundry

A C# console sample that connects a **Microsoft Foundry-backed agent** to the
**remote TomTom Maps MCP server**, discovers its tools, and answers one location question.

**.NET 10 · C# · MCP over HTTP · Spectre.Console · MIT**

## ⚡ Quick start

You need the [.NET 10 SDK](https://learn.microsoft.com/en-us/dotnet/core/install/windows),
the [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) for the sign-in below,
an interactive terminal, a Foundry project with a tool-capable model deployment and
access permissions, and a TomTom API key enabled for MCP and the APIs you use.
Azure and TomTom usage can incur charges.

After [obtaining your configuration values](docs/environment-variables.md), run from
the repository root in **PowerShell 7.1+**:

```powershell
az login
$env:ENDPOINT = 'https://<resource>.services.ai.azure.com/api/projects/<project>'
$env:DEPLOYMENT_NAME = '<your-model-deployment-name>'
$env:TT_APIKEY = Read-Host 'TomTom API key' -MaskInput
dotnet run --project .\src\TTSLN\TT.Basic\TT.Basic.csproj
```

Replace the placeholders; the endpoint must be a **Foundry project endpoint**, not an
Azure OpenAI resource URL. The sample does **not** load `.env` files.
Press Enter at the question prompt to use the Eiffel Tower example. It prints one
completed answer and exits; there is no chat loop or token streaming.

## 📚 Documentation

| Start here | What you will find |
|---|---|
| [Documentation index](docs/README.md) | Choose a guide |
| [Getting started](docs/getting-started.md) | Prerequisites, authentication, setup and first run |
| [Environment variables](docs/environment-variables.md) | Every application variable, value acquisition and optional SDK credentials |
| [Architecture](docs/architecture.md) | Source map, tool execution and lifecycle |
| [Troubleshooting](docs/troubleshooting.md) | Configuration, connectivity and console limitations |

## Official resources

- [TomTom Maps MCP setup](https://docs.tomtom.com/tomtom-maps-mcp/documentation/quick-setup)
  · [Get a TomTom API key](https://docs.tomtom.com/platform/documentation/my-tomtom/how-to-get-a-tomtom-api-key)
- [Microsoft Foundry setup](https://learn.microsoft.com/en-us/azure/foundry/tutorials/quickstart-create-foundry-resources)
  · [Agent Framework Foundry integration](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry)
- [C# documentation](https://learn.microsoft.com/en-us/dotnet/csharp/tour-of-csharp/overview)
  · [Spectre.Console documentation](https://spectreconsole.net/console/)

## ⚖️ License

This repository is licensed under the [MIT License](LICENSE).
Third-party packages and hosted services retain their own licenses and terms.
