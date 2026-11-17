# Location Intelligence web workspace

[Documentation index](README.md) | [Repository overview](../README.md)

`src\TTSLN\TT.Web.LocationIntelligence` is an ASP.NET Core 10 application with a
TomTom-inspired Vue 3 workspace, inline SVG icons, Cosmos DB conversations, and
SignalR notifications. It reuses the console sample's Microsoft Agent Framework,
Foundry project endpoint, and remote TomTom Maps MCP integration.
The **Live Maps** header link opens a separate, stateless MCP Apps workspace at
`/Maps`, with streamed answers, live tool activity, and interactive maps.

## Run locally

You need .NET 10, Node.js 22.12 or later, Azure CLI, WSLC, a Foundry project with a
tool-capable deployment, and a TomTom MCP-enabled API key. Model and tool calls
use hosted services and can incur charges; the Cosmos emulator is local.

From the repository root in PowerShell:

```powershell
az login
$project = '.\src\TTSLN\TT.Web.LocationIntelligence\TT.Web.LocationIntelligence.csproj'
dotnet user-secrets set 'Agent:Endpoint' 'https://<resource>.services.ai.azure.com/api/projects/<project>' --project $project
dotnet user-secrets set 'Agent:Model' '<deployment-name>' --project $project
$key = Read-Host 'TomTom API key' -MaskInput
dotnet user-secrets set 'Agent:TomTomApiKey' $key --project $project
Remove-Variable key
```

Start the HTTPS emulator, with its published endpoint restricted to loopback:

```powershell
wslc volume create tt-location-cosmos-data
wslc run -d --name tt-location-cosmos -p 127.0.0.1:8081:8081 `
  --mount type=volume,source=tt-location-cosmos-data,target=/data `
  mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-latest `
  --protocol https --enable-explorer false
```

Build the client and run the web app:

```powershell
Push-Location .\src\TTSLN\TT.Web.LocationIntelligence
npm ci
npm run build
Pop-Location
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project $project --urls http://127.0.0.1:5088
```

Open `http://127.0.0.1:5088`, create a conversation, and send a question. Your
messages appear on the right, and agent replies on the left. Each completed
reply shows its configured model deployment and provider-reported input/output
token counts. Missing usage is displayed as **not reported**, never estimated.
The options dialog controls compact spacing and usage display; these preferences
stay in browser local storage. Credentials are never editable in the browser. The Live Maps app has an
additional browser-key requirement described below.

Foundry calls use **DefaultAzureCredential**, including Azure CLI credentials
from `az login` during local development. Conversations are grouped by
an anonymous, HTTP-only browser session cookie, not an authenticated user.
Clearing that cookie starts a new workspace; previous identity-based partitions
are not migrated. The app has no in-app authentication, authorization, or rate
limiting, including on SignalR. Configure access restrictions manually in Azure
before exposing the demo publicly. Browser session separation is not a user
authentication boundary.

The development settings contain only the documented, public emulator key, not
an Azure account secret. The Linux emulator uses a self-signed certificate;
certificate validation is bypassed **only in Development with UseEmulator=true**.
The vNext emulator does not enforce authentication. Never expose its ports to
untrusted networks.

## Configuration reference

Use user secrets locally and Container Apps environment variables/secret
references in production. ASP.NET configuration uses `__` in environment
variables, for example `Cosmos__Endpoint`. No `.env` loading is implemented.
The console's `ENDPOINT`, `DEPLOYMENT_NAME`, and `TT_APIKEY` variables are not
automatically mapped into the web application's options.

| Setting | Purpose |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` loads emulator settings; `Production` uses the real Cosmos account and `DefaultAzureCredential`. Other non-development environments use the same secure credential path. |
| `Cosmos:Endpoint` | Emulator defaults to `https://localhost:8081`; production must be a non-loopback HTTPS Cosmos endpoint. |
| `Cosmos:Database` | Database name, default `LocationIntelligence`. |
| `Cosmos:Container` | Container name, default `Conversations`, partition key **`/ownerId`**. |
| `Cosmos:UseEmulator` | Must be `true` in Development and `false` outside Development. |
| `Cosmos:EmulatorKey` | Public emulator credential; never used by the production client. |
| `Agent:Endpoint` | Foundry **project** endpoint, not an Azure OpenAI resource endpoint. |
| `Agent:Model` | Tool-capable model deployment name. |
| `Agent:Id` | Stable logical agent ID for monitoring, default `LocationIntelligence`. Keep consistent across restarts and replicas. |
| `Agent:Name` | Agent display name in monitoring, default `LocationIntelligence`. |
| `Agent:TomTomApiKey` | Supplied through server configuration and the `tomtom-api-key` MCP header. Live Maps also exposes it to TomTom's sandboxed app for browser map requests; see below. |
| `Agent:McpEndpoint` | Default `https://mcp.tomtom.com/maps?apps=false`. |
| `Agent:Instructions` | Server-managed assistant instructions. |
| `Maps:McpEndpoint` | Separate HTTPS MCP Apps endpoint, default `https://mcp.tomtom.com/maps?apps=true`. Does not change the chat agent's data-only endpoint. |
| `Maps:Name` | Stateless maps agent ID/name, default `TomTomInteractiveMaps`. |
| `Maps:Instructions` | Maps agent instructions; request accurate tool-backed answers and interactive visualizations. |
| `Maps:DefaultPrompt` | Editable initial question, default Amsterdam to Berlin avoiding toll roads with gas stations along the way. |
| `Maps:TimeoutSeconds` | Maps query/app-tool timeout, default 180, supported range 30-600 seconds. |
| `Maps:TicketLifetimeSeconds` | Browser-bound encrypted query ticket lifetime before starting a stream, default 60 seconds. |
| `Maps:AppInitializationTimeoutSeconds` | Time before reporting an uninitialized iframe, default 30 seconds. |
| `Chat:MaxMessageLength` | User message limit, default/max 8,000 characters. |
| `Chat:MaxTitleLength` | Conversation title limit, default 120, supported range 1-200 characters. |
| `Chat:MaxTurns` | Conversation bound, default/max 40 turns. Start a new conversation when full. |
| `Chat:ReplyTimeoutSeconds` | Model/tool timeout, default 180, supported range 30-600 seconds. |
| `Chat:PageSize` | Session listing page size, default 30, maximum 100. |
| `BrowserSession:CookieName` | Anonymous workspace cookie name, default `tt-browser-session`. |
| `ReverseProxy:ForwardedHeaders` | Defaults to `XForwardedFor, XForwardedProto`, restoring the original client IP and scheme. |
| `ReverseProxy:TrustAllProxies` | Defaults to `false` (loopback proxies only); container images set it to `true` for Azure ingress. |
| `ApplicationInsights:ConnectionString` | Optional Application Insights connection string; enables request/dependency telemetry, agent/model/tool traces, agent metrics, and `ILogger` export. Falls back to `APPLICATIONINSIGHTS_CONNECTION_STRING`. |

Application options are bound in `Configuration\ServiceRegistration.cs`, with
each options class in its own file under `Options`. Database options are
validated at startup. Foundry settings can initially
be empty so you can test the workspace and database without a model: the UI
displays a setup banner, and sends return an explicit 503 while preserving the
question and a failed reply. It does not generate demo answers.

### Agent monitoring

Set `ApplicationInsights__ConnectionString` (or the standard
`APPLICATIONINSIGHTS_CONNECTION_STRING`) through a Container Apps secret reference
or local user secrets. No telemetry exporter is registered without a connection
string. The existing Application Insights OpenTelemetry pipeline also collects
the `TT.Web.LocationIntelligence.Agent` activity source and meter.

After sending questions, open the connected Application Insights resource and
select **Agents (Preview)**, then **View Traces with Agent Runs** to inspect
agent executions, model calls, tool calls, durations, failures, and actual token
usage when reported by the provider. Telemetry ingestion can take a few minutes.
See [Monitor AI agents with Application Insights](https://learn.microsoft.com/azure/azure-monitor/app/agents-view).

Agent and model instrumentation explicitly disables sensitive-data capture,
including when `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` is enabled
in the environment. Prompts, replies, system instructions, tool arguments, and
tool results are not intentionally recorded. Unknown token usage is not
estimated. This instruments the local Agent Framework/MCP execution; it does not
register a hosted Foundry agent or provision Azure resources.

## Live Maps and MCP Apps

Select **Live Maps** in the header or visit `http://127.0.0.1:5088/Maps`.
The textbox is prefilled with:

> Find the fastest route from Amsterdam to Berlin, avoiding toll roads,
> and show me gas stations along the way.

Edit the question or select **Explore with TomTom**. The page shows tool progress,
answer text as it arrives, and each returned TomTom MCP App in an interactive
map card. **Under the hood** exposes the complete received events, including
tool arguments, full tool results with `_meta`, app resources, and provider
usage when reported. This diagnostic panel renders JSON as text, not HTML.
Agent Markdown uses the same sanitization as the conversation workspace.

The backend follows the console sample's approach: connect to remote MCP over
HTTP with `Agent:TomTomApiKey`, discover tools, attach them to a Foundry-backed
agent, and execute the question. The maps agent uses its own instructions and
`Maps:McpEndpoint`, so the persisted chat agent remains unchanged. Tools that
support `show_ui` receive `true`. App-only tools are excluded from model tools.
The host reads the `ui://` HTML resource declared in each tool's MCP metadata
and sends the original result, including UI-only metadata, to the iframe.

The stateless minimal API and SignalR contract is:

| Route | Behavior |
|---|---|
| `GET /api/maps/config` | Public UI settings and endpoint paths; no credentials. |
| `POST /api/maps/queries` | Validates CSRF and prompt length; returns a short-lived encrypted ticket bound to the browser cookie. |
| `/hubs/maps`, streaming method `Query(ticket)` | Validates the ticket and streams status, text deltas, tool starts/results, app HTML/metadata, actual usage, and an explicit completion or sanitized error. |
| `POST /api/maps/app-tools` | CSRF-protected proxy restricted to `tomtom-get-api-key`, `tomtom-get-app-config`, `tomtom-get-app-data`, and `tomtom-report-app-status`. |

No query, result, map, or conversation is written to Cosmos, browser storage,
or application-owned result storage. The browser holds results only for the
current page. Query tickets contain an encrypted prompt and expire quickly;
avoid recording request bodies or SignalR payloads in external diagnostics.
Foundry and TomTom remain external services subject to their own data policies.
**Stop exploration**, leaving the page, or losing the SignalR connection
cancels provider work; partial output stays visible. Reconnection does not
replay queries or recover results. Resubmission is a new billed operation.
The existing application-level Cosmos configuration validation still applies,
but this feature does not access the database.

### Map sandbox, credentials, and network access

Each third-party map runs in a scripts-only iframe with an opaque origin:
no same-origin access, parent DOM, cookies, browser storage, popups, top-level navigation,
camera, microphone, or geolocation grants. The host checks the message source,
implements the MCP Apps initialization/input/result protocol, and proxies only
the four app tools above. A restrictive Content Security Policy is inserted
before app scripts; external resources/connections are limited to the
resource's declared CSP domains. Unsupported host operations are rejected.

**Important:** TomTom's official MCP App calls `tomtom-get-api-key` and uses
the returned key directly for browser map requests. The configured TomTom
key is consequently visible to the browser and its developer tools. It is
not a server-only secret when Live Maps is enabled. Use a dedicated,
least-privilege TomTom key with appropriate service/domain restrictions,
quotas, and rotation. Do not expose this demo publicly without access
restrictions: the anonymous cookie and CSRF validation are not authentication.
Foundry credentials remain server-side.

The browser must support WebGL and reach `https://cdn.jsdelivr.net`,
`https://api.tomtom.com`, and TomTom subdomains declared by the resource.
Fonts, Vue, and SignalR are still bundled locally; the third-party MCP App
loads its own map dependencies. Network/CSP/API-key failures are surfaced
in the map card rather than replaced with a static demo map.

See TomTom's [interactive maps documentation](https://docs.tomtom.com/tomtom-maps-mcp/documentation/interactive-maps),
[map tools](https://docs.tomtom.com/tomtom-maps-mcp/documentation/tools#maps--data-visualization),
and the [MCP Apps protocol](https://modelcontextprotocol.io/docs/extensions/apps).

## Build and run containers with WSLC

The repository-root `containers\LocationIntelligence.Dockerfile` has a Node build
stage, a .NET publish stage, a non-root production image, and a separate
development target containing Azure CLI. The production image does not include
the SDK, npm, or Azure CLI. Fonts, inline SVG icons, Vue, and SignalR are bundled locally.

For a fresh local stack, optionally export the web settings into your current
shell and run:

```powershell
$env:Agent__Endpoint = 'https://<resource>.services.ai.azure.com/api/projects/<project>'
$env:Agent__Model = '<deployment-name>'
$env:Agent__TomTomApiKey = Read-Host 'TomTom API key' -MaskInput
.\containers\Start-Local.ps1
```

The script builds the development image, creates a private bridge network and
persistent Cosmos, Azure CLI, and data-protection volumes, starts both
containers, and waits for database readiness. It publishes only loopback ports,
defaults to `http://localhost:8088`, and fails rather than removing an existing
container with the same name. Stop/remove an existing, intentionally replaceable
instance before starting a fresh stack; the named volumes retain data.

Windows Azure CLI caches cannot simply be mounted into Linux. Sign in once
**inside the development container**, using its persisted CLI volume:

```powershell
wslc exec -it tt-location-intelligence az login --use-device-code
```

Complete the sign-in in your browser, then reload the workspace. Neither
Windows user secrets nor Windows CLI tokens are copied into the image.

If your network requires package mirrors, pass them without editing the
Dockerfile or committing machine-specific registry configuration:

```powershell
.\containers\Start-Local.ps1 -NpmRegistry '<npm-registry-url>' -NugetSource '<nuget-v3-index-url>'
```

To build the production target independently:

```powershell
wslc build --target production -f .\containers\LocationIntelligence.Dockerfile `
  -t tt-location-intelligence:production .
```

The equivalent build arguments are `NPM_REGISTRY` and `NUGET_SOURCE`, which
default to the public registries. If a custom registry requires credentials,
use your build system's secret facilities rather than build arguments.

Forwarded headers run before the rest of the request pipeline, so HTTPS ingress
produces secure browser/CSRF cookies even when the proxy talks HTTP to the app.
The images enable `ReverseProxy__TrustAllProxies=true` because Azure ingress
addresses are dynamic. Deploy only behind a trusted ingress that sanitizes
forwarded headers and prevents direct backend access. Set this variable to
`false` to retain loopback-only proxy trust; `Start-Local.ps1` passes through
either reverse-proxy environment setting from your shell. `X-Forwarded-Host` is
not enabled by default.

## Deploy to Azure Container Apps

1. Provision the Cosmos database and container in advance with `/ownerId` as
   the partition key. Production never creates databases/containers.
1. Enable a managed identity for the container app. Give it Cosmos DB
   **data-plane** access, including metadata, item read/write, and queries
   (for example Cosmos DB Built-in Data Contributor scoped appropriately).
   Grant the Foundry project/model permissions required by your deployment.
1. Configure HTTPS-only ingress and any platform authentication/access policies
   manually in Azure. The app does not check Easy Auth settings or read platform
   principal headers.
1. Set `ASPNETCORE_ENVIRONMENT=Production`, the production `Cosmos__Endpoint`,
   `Cosmos__Database`, `Cosmos__Container`, and `Cosmos__UseEmulator=false`.
   Configure the agent settings and optional
   Application Insights connection string through secret references.
1. Expose target port **8080**, enable WebSockets, and configure liveness on
   `/health` and readiness on `/health/ready`. Allow anonymous health probes at
   the platform level if your probe path traverses Easy Auth.

`DefaultAzureCredential` authenticates Cosmos and Foundry service calls,
independently of browser access. No production Cosmos account
key is required. For a user-assigned managed identity, configure its client ID
using the standard Azure Identity environment settings.

Run **one replica** initially: the built-in SignalR lifetime manager is
process-local. Before scaling out, configure Azure SignalR Service or a shared
backplane and connection affinity as appropriate. Cosmos ETags still prevent
cross-replica lost updates, but do not distribute SignalR notifications.
Persist/share ASP.NET data-protection keys for restarts or multiple replicas
and encrypt those keys at rest using your deployment's key-management policy.
For long model calls, align ingress/client request timeouts with the configured
reply timeout; generation is request-driven, not a durable background queue.

## Architecture and API reference

`Pages\Index.cshtml` renders the document shell (`Cache-Control: no-store`),
embeds serialized `RuntimeResponse` bootstrap JSON in `<script id="tt-runtime" type="application/json">`,
and loads `~/dist/app.js` alongside `~/dist/app.css` from `_Layout.cshtml`. Vite
emits stable `app.js` and `app.css` entry files plus hashed vendor chunks
(`vendor-vue`, `vendor-signalr`, `vendor-markdown`). `Client\ChatApp.vue`
composes a modular Vue 3 workspace across `Client\services\` (`httpClient.js`—re-exported
by `Client\api.js` as `createApi`—`runtimeApi.js`, and `sessionsApi.js`),
`Client\composables\` (`useRuntimeConfig.js`, `useChatWorkspace.js`,
`useChatRealtime.js`, `useLeaseClock.js`, and `usePreferences.js`), and
`Client\components\` (including `AppIcon.vue` inline SVG icons instead of Font
Awesome webfonts). All browser data operations call minimal APIs, errors return
sanitized RFC 9457 `ProblemDetails` with `code` and `traceId` extensions
(`httpClient.js` refreshes `/api/runtime` and retries once on `400`
`csrf_invalid`). `/hubs/chat` carries only conversation notifications.
`Pages\Maps.cshtml` selects `Client\MapsApp.vue` through the same entry bundle;
that separate workspace streams transient agent results through `/hubs/maps`
and hosts sandboxed MCP Apps with `Client\services\mcpAppHost.js`.

| Feature | Location |
|---|---|
| Middleware pipeline and endpoint mapping | `Program.cs` |
| Service registration, option binding, proxy settings and telemetry | `Configuration\ServiceRegistration.cs` |
| Configuration types (one per file) | `Options` |
| DTOs, anonymous browser sessions, persistence, turn orchestration, Foundry/MCP and hub | `Features\Chat` |
| Stateless maps agent, MCP App resources, query tickets, minimal APIs and streaming hub | `Features\Maps` |
| Sanitized errors, runtime information and readiness | `Features\Operations` |
| Modular Vue 3 UI (`services\`, `composables\`, `components\`), inline SVG icons, styles and client tests | `Client` |
| MSTest unit/API/emulator tests | `..\TT.Web.LocationIntelligence.Tests` |

| Endpoint | Contract |
|---|---|
| `GET /` | Razor shell (`Cache-Control: no-store`) with inline `#tt-runtime` `RuntimeResponse` JSON (`maxMessageLength`, `maxTitleLength`, `maxTurns`, `antiforgeryHeaderName`, `csrfToken`, `endpoints`). |
| `GET /Maps` | Live Maps Razor shell (`Cache-Control: no-store`); see the [MCP Apps contract](#live-maps-and-mcp-apps) for maps APIs and streaming. |
| `GET /api/runtime` | Anonymous typed `RuntimeResponse` (`Cache-Control: no-store`) for fallback and CSRF refresh (`endpoints`: `/api/runtime`, `/api/sessions`, `/hubs/chat`). Never returns credentials. |
| `GET /api/sessions?continuationToken=...` | Browser-scoped summaries and Cosmos continuation token; message content is excluded. |
| `POST /api/sessions` | JSON `{ "title": "Amsterdam trip" }` (optional, defaults to `"New exploration"`, bounded by `Chat:MaxTitleLength`); requires CSRF verification and returns `201 Created` with an opaque `ETag`. |
| `GET /api/sessions/{id}` | Browser-scoped conversation (`{id:guid}`) and saved messages with `Cache-Control: private, no-cache` and an opaque `ETag`; returns `304 Not Modified` when `If-None-Match` matches, or `404` for unknown/other-browser IDs. |
| `PUT /api/sessions/{id}` | JSON `{ "title": "Updated title" }` (`1`–`Chat:MaxTitleLength` characters); requires CSRF verification, rejects edits with `409` (`session_busy`) during an active reply, and returns `200 OK` with an updated `ETag`. |
| `DELETE /api/sessions/{id}` | Permanently deletes a browser-owned conversation when not busy (`409` `session_busy` otherwise); requires CSRF verification and returns `204 No Content`. |
| `POST /api/sessions/{id}/messages` | JSON `{ "requestId": "<new-guid>", "content": "Your question" }`; requires CSRF verification and returns `200 OK` with an updated `ETag`. |
| `/hubs/chat` | Anonymous SignalR endpoint; joins the browser session's notification group and emits `ConversationChanged(sessionId, updatedAt)` and `ConversationDeleted(sessionId)`. |
| `GET /health` | Anonymous process liveness, independent of Cosmos. |
| `GET /health/ready` | Anonymous Cosmos connectivity/readiness; 503 when unavailable. |

Sessions embed bounded messages so each turn-state change is one atomic Cosmos
item replacement. Cosmos ETags reject stale storage writes (`409`
`concurrency_conflict`), while HTTP session responses expose only a derived,
opaque SHA-256 `ETag` (`SessionETag`) rather than raw storage metadata. A timed lease serializes agent
replies; an interrupted lease expires so a later send can recover. Requests
with the same ID do not invoke the model twice. User messages and a pending
assistant entry are saved before model execution, then replaced with the
completed response or an explicit failure. Failed assistant output is excluded
from future model context. Browser disconnection does not cancel the provider
timeout or cleanup write, but process termination can interrupt a pending turn.

The implementation limits document size to 1.8 MB, with reply headroom and
JSON escaping accounted for, below Cosmos DB's 2 MB item limit. Replies are
bounded to 16,000 characters. Start a new conversation once it reaches either
the turn or size limit. There is no unbounded message array, automatic retention
policy, or full conversation export.

Structured `ILogger` events include session IDs, deployment and token counts,
not intentional prompt/reply logging. Application Insights receives logs when
its connection string is configured. Treat provider exception diagnostics and
telemetry as potentially sensitive and apply appropriate access and retention
policies.

## Tests and verification

From the repository root:

```powershell
dotnet test .\src\TTSLN\TT.Web.LocationIntelligence.Tests
Push-Location .\src\TTSLN\TT.Web.LocationIntelligence
npm ci
npm test
npm run build
Pop-Location
```

The Cosmos integration test is opt-in because it requires a running emulator:

```powershell
$env:RUN_COSMOS_TESTS = 'true'
# Optional: $env:TEST_COSMOS_ENDPOINT = 'https://localhost:8081'
dotnet test .\src\TTSLN\TT.Web.LocationIntelligence.Tests
Remove-Item Env:\RUN_COSMOS_TESTS
```

It uses `LocationIntelligenceTests`, verifies reads across independent clients,
owner isolation and stale ETags, and deletes its own test conversation. Without
the flag it is explicitly reported as inconclusive/skipped. Other tests cover
turn ordering, duplicate sends, provider failures, concurrent sends, size limits,
anonymous browser separation, CSRF, HTTP persistence contracts, usage metadata,
forwarded-header trust, anonymous SignalR notifications, and health.
The provider in unit/API tests is mocked; those tests do not claim a live
Foundry/TomTom response.
Maps tests additionally exercise ordered SignalR streaming, full MCP result
metadata, app HTML, ticket tampering/expiry/browser isolation, cancellation,
CSRF, app-tool allowlisting, sanitized failures, and zero conversation writes.
An in-process MCP server tests the actual SDK tool-call/resource-read path,
including modern and legacy UI metadata. Vue tests cover streamed rendering,
stale-event guards, reconnect behavior, iframe handshakes, source checks, CSP,
and explicit initialization/tool errors. These do not replace a live validation
with your own Foundry deployment and TomTom key.

Check running containers:

```powershell
wslc list
wslc logs --tail 40 tt-location-intelligence
Invoke-WebRequest http://localhost:8088/health
Invoke-WebRequest http://localhost:8088/health/ready
```

## Troubleshooting

| Symptom | Action |
|---|---|
| Model credential failure in Development | Run `az login` in the same OS/container as the app; do not copy Windows credential caches into Linux. Browser access itself needs no sign-in. |
| Agent setup banner / 503 on send | Configure the web app's `Agent` options; console environment-variable names do not apply. |
| Readiness 503 | Check emulator startup, HTTPS, endpoint/DNS and volume permissions; in production check Cosmos data-plane RBAC. |
| Production startup validation fails | Supply a real HTTPS Cosmos endpoint and disable emulator mode. |
| Send returns 409 | Another reply or tab owns the turn; reload the conversation. Do not automatically retry with a new request ID. |
| Browser loses its HTTP response | Reload the session before resubmitting; the turn may already be persisted. |
| Live indicator disconnects | Click Reconnect. Reconnection reloads persisted state; HTTP responses remain authoritative. |
| Turn stays pending after a process crash | After the lease expires, the composer re-enables; the next send marks the interrupted reply as failed. |
| NuGet/npm cannot be reached from WSLC | Supply reachable registry build arguments. Do not disable package TLS verification. |
| CSS or JavaScript missing | Run `npm ci` then `npm run build`; `dotnet publish` also builds assets unless `SkipClientBuild=true` is explicitly supplied. |
| Live Maps iframe stays blank | Check WebGL, browser console/CSP errors, and access to jsDelivr and TomTom APIs. Confirm the key permits the map services. |
| Live Maps query was interrupted | Partial results are not saved. Wait for reconnection and submit explicitly; the original query is never replayed automatically. |
