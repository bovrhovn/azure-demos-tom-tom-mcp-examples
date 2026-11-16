# 🚀 Getting started

[Documentation index](README.md) · [Configuration reference](environment-variables.md) · [Troubleshooting](troubleshooting.md)

**Goal:** run one location question through the local C# application using a model
in Microsoft Foundry and tools from the hosted TomTom Maps MCP server.
Commands below assume the repository root and PowerShell 7.1+ (`pwsh`).

## 1. Prepare your workstation

- Install the **.NET 10 SDK**, not only the runtime:
  [Microsoft installation guide](https://learn.microsoft.com/en-us/dotnet/core/install/windows).
  Check `dotnet --list-sdks` for a `10.0.*` SDK. No IDE is required.
- For the sign-in path below, install the
  [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli).
- Use an interactive terminal with outbound HTTPS access to Microsoft Entra ID,
  your Foundry project and `mcp.tomtom.com`. Package restore also needs NuGet access.

```powershell
dotnet --list-sdks
az version
```

The project already declares its NuGet packages; do not add packages manually or
replace its versions with those shown in newer upstream tutorials.

## 2. Prepare Foundry

Use an existing project or follow Microsoft's
[Foundry resource and model setup](https://learn.microsoft.com/en-us/azure/foundry/tutorials/quickstart-create-foundry-resources).
Resource creation and inference can incur charges.

1. Select your project in the Foundry portal and copy its **project endpoint** from
   the welcome screen. Its shape is
   `https://<resource>.services.ai.azure.com/api/projects/<project>`.
2. Deploy a model that supports **Responses API inference and function/tool calling**
   in your project's region. Record the **deployment name**, which can differ from
   the model's catalog name. This repository does not mandate a particular model.
3. Have your administrator grant your signed-in identity access to the project and
   model. Microsoft's setup guide recommends **Foundry User** (formerly **Azure AI User**)
   for building and testing. An Azure subscription alone does not grant data-plane access.

This sample uses the code-first
[Foundry Responses Agent pattern](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry).
You do not need to create a named, server-managed Foundry agent or a hosted MCP connection.
It does not configure Application Insights tracing.

## 3. Obtain your TomTom API key

Follow [TomTom's API-key guide](https://docs.tomtom.com/platform/documentation/my-tomtom/how-to-get-a-tomtom-api-key).
Sign up or sign in, open **API & SDK Keys**, and copy an existing key or create one.

In the key's configuration, enable the **MCP Server** product and the APIs required
by the tools you plan to use. TomTom's
[MCP prerequisites](https://docs.tomtom.com/tomtom-maps-mcp/documentation/quick-setup)
list Map Display, Routing, Search, Geocoding, Reverse Geocoding and Traffic.
Follow your organization's least-privilege policy; enabling MCP alone may not grant
access to an underlying API. The hosted MCP service is currently public preview,
and API usage follows TomTom's charging terms.

No local TomTom server, Node.js or npm installation is needed.

## 4. Sign in and configure this terminal

Sign in to the tenant that owns your Foundry project:

```powershell
az login
```

For multiple tenants, use `az login --tenant '<tenant-id>'` instead. See
[Azure CLI interactive sign-in](https://learn.microsoft.com/en-us/cli/azure/authenticate-azure-cli-interactively).
The app's `DefaultAzureCredential` can use this login; it does not call `az login`
for you. Other existing credentials can take precedence. To use only the CLI for
this session, optionally set `$env:AZURE_TOKEN_CREDENTIALS = 'AzureCliCredential'`.

Set all three required application variables, replacing the placeholders:

```powershell
$env:ENDPOINT = 'https://<resource>.services.ai.azure.com/api/projects/<project>'
$env:DEPLOYMENT_NAME = '<your-model-deployment-name>'
$env:TT_APIKEY = Read-Host 'TomTom API key' -MaskInput
```

The masked prompt avoids placing the key in command history, but the environment
still holds it as plaintext. Run the application from this same terminal.
No Azure API key is used. `.env`, IDE MCP settings and .NET user-secrets are not
loaded by application code. See [the full reference](environment-variables.md).

## 5. Restore, build and run

From the repository root:

```powershell
dotnet restore .\src\TTSLN\TTSLN.slnx
dotnet build .\src\TTSLN\TTSLN.slnx --no-restore
dotnet run --project .\src\TTSLN\TT.Basic\TT.Basic.csproj --no-build
```

Restore and build do not test your cloud credentials or make a model request.
The run command connects to remote services and may incur usage charges.

Expected sequence:

1. A greeting, the hardcoded TomTom URL, your endpoint and deployment name, and a
   partially masked API key appear. Do not share screenshots containing configuration.
2. The app connects to TomTom, lists available tools, and prints their names. The
   count depends on the remote service and your access; it is not fixed.
3. At **Ask your question**, enter a location question or press Enter to accept:
   `Where is Paris located? Give me coordinates of Eiffel tower.`
4. The app awaits a complete answer, prints **Answer:** and exits.

The model decides whether to invoke a tool; a correct-looking answer alone does
not prove that TomTom was called. There is no tool-approval dialog, streaming output,
second-turn prompt or persistent conversation implemented by this sample.
See [architecture](architecture.md) for the execution boundary.

## 6. End your session safely

```powershell
Remove-Item Env:\TT_APIKEY -ErrorAction SilentlyContinue
```

This clears only the current terminal's variable, not any persistent copy.
Remove `ENDPOINT`, `DEPLOYMENT_NAME` and optional credential settings too if you no
longer need them. Do not delete shared cloud resources just to stop this console
process; manage resource cleanup and billing separately through your administrator.
