# 🧭 Architecture

[Documentation index](README.md) · [Getting started](getting-started.md) · [Environment variables](environment-variables.md)

## Source map

| File | Responsibility |
|---|---|
| [`src\TTSLN\TTSLN.slnx`](../src/TTSLN/TTSLN.slnx) | Solution containing `TT.Basic` in the `Initial` folder; `Scenarios` is an empty solution folder, not another sample |
| [`Program.cs`](../src/TTSLN/TT.Basic/Program.cs) | Top-level entry point: configuration, HTTP MCP client, tool discovery, agent and one console interaction |
| [`CliHelpers.cs`](../src/TTSLN/TT.Basic/CliHelpers.cs) | `MaskSecret`, used only to partially hide the TomTom key in console output |
| [`TT.Basic.csproj`](../src/TTSLN/TT.Basic/TT.Basic.csproj) | .NET 10 executable, implicit usings and nullable reference types enabled |

Direct package references in the project:

| Package | Version | Role |
|---|---|---|
| `ModelContextProtocol` | `2.4.0` | MCP client and HTTP transport |
| `ModelContextProtocol.Core` | `2.4.0` | MCP core abstractions |
| `Spectre.Console` | `0.57.2` | Styled output and interactive text prompt |
| `Azure.AI.Projects` | `2.0.0` | Foundry project client |
| `Azure.Identity` | `1.21.0` | Microsoft Entra credential chain |
| `Microsoft.Agents.AI.Foundry` | `1.1.0` | Foundry-backed `AIAgent` integration |

These are source-declared versions, not a claim that they are the latest releases.

## Runtime flow

```text
Interactive terminal
        |
        v
TT.Basic (.NET process)
  |-- process environment: ENDPOINT, DEPLOYMENT_NAME, TT_APIKEY
  |-- DefaultAzureCredential --> Microsoft Entra ID
  |-- AIProjectClient / AIAgent --> Foundry project model inference
  `-- MCP client over HTTPS --> hosted TomTom Maps MCP --> TomTom APIs
```

1. Print a greeting and the hardcoded MCP URL.
2. Read and check each required variable, printing the project endpoint,
   deployment name and partially masked key.
3. Build `HttpClientTransportOptions` for
   `https://mcp.tomtom.com/maps?apps=false`, adding `tomtom-api-key`.
4. Connect with `McpClient.CreateAsync`, call `ListToolsAsync`, then print each
   discovered tool name with markup escaping.
5. Construct `AIProjectClient` with the configured project URI and
   `DefaultAzureCredential`. Call `AsAIAgent` with the model deployment,
   fixed name `TomTomBasic`, fixed instructions and **all** discovered MCP tools.
6. Ask for one question using `AnsiConsole.Ask<string>`, with the Eiffel Tower
   question as its default.
7. Await `agent.RunAsync(question)`, then print the complete response.
8. Leave the top-level scope; `await using` asynchronously disposes the MCP client.

TomTom connection and discovery happen **before** the question prompt and before
model inference. A failure during discovery prevents the prompt from appearing.
An invalid Foundry URI may also fail before the prompt, when its client is constructed.

## Remote server, locally executed tool integration

The TomTom server is remote. The application does not start a server, spawn a
stdio subprocess or read editor MCP settings. The `apps=false` query selects
data tools instead of interactive map rendering, per
[TomTom's setup guide](https://docs.tomtom.com/tomtom-maps-mcp/documentation/quick-setup).
HTTP framing is handled by the MCP SDK; transport options are otherwise left at
their defaults in this source.

The discovered MCP tools are supplied as callable tools to the **local agent
framework**. The model can request calls; the application-side framework invokes
the MCP client, which calls the hosted TomTom server. Tool results can then inform
the model's answer. This is not Foundry's server-side hosted-MCP integration:
the source does not register the TomTom endpoint or key as a hosted tool in Foundry.
See Microsoft's
[code-first Responses Agent pattern](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry).
That pattern does not create a server-managed agent definition.

Tool selection is model-dependent; no particular tool call or fixed tool count is
guaranteed. The source does not filter the discovered tool list or add approval
wrappers. **There is no per-call approval prompt in this sample.** Use only a
trusted MCP service and appropriately scoped credentials. Prompts go to Foundry,
tool arguments go to TomTom when called, and tool results may be sent back to
Foundry. Do not put sensitive personal or confidential data in questions.

## Authentication boundaries

- **TomTom:** `TT_APIKEY` in the `tomtom-api-key` header. The masking helper affects
  display only; it does not encrypt the variable or change the transmitted key.
- **Foundry:** Microsoft Entra access tokens from `DefaultAzureCredential`. A local
  Azure CLI login is one supported path; no Azure API key is passed by this code.
- **Authorization:** the selected identity needs project/model access independently
  of the TomTom key. See [SDK credential inputs](environment-variables.md#azure-authentication-sdk-dependencies-not-application-settings).

## Interaction and lifecycle limits

- One question and one `RunAsync` call per process; no chat loop or explicitly
  maintained multi-turn session.
- No token streaming: the program does not call `RunStreamingAsync`.
  Async I/O is not the same as incremental console output.
- No explicit client-side transcript persistence, telemetry setup, cancellation
  handler or application retry loop. SDK/service defaults still apply.
- No application-level exception handling or custom cleanup of remote inference
  records. Normal scope exit disposes the MCP client; forceful termination has
  no application-defined graceful-shutdown guarantee.
- `Spectre.Console` renders the UI; it is not an HTTP API, web UI or command-line
  option parser in this sample.
- Some dynamic strings, including the question and answer, are passed to markup
  output without escaping. See [console troubleshooting](troubleshooting.md#console-markup-errors).

This is a minimal integration example, not a production-hardened agent host.
