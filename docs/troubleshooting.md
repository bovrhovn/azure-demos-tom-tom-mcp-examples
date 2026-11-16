# 🛠️ Troubleshooting

[Documentation index](README.md) · [Getting started](getting-started.md) · [Environment variables](environment-variables.md)

Diagnose the earliest failing stage. Configuration validation and TomTom discovery
run before the question prompt; a completed build does not verify either cloud service.
Never paste keys, access tokens or unredacted terminal captures into issue reports.

## SDK or package restore fails

1. From the repository root, run `dotnet --list-sdks` and confirm a .NET 10 SDK.
   A runtime-only install cannot build `net10.0`.
2. Use the existing solution/project rather than running `dotnet run` at the root
   without a project path:

   ```powershell
   dotnet restore .\src\TTSLN\TTSLN.slnx
   dotnet build .\src\TTSLN\TTSLN.slnx --no-restore
   ```

3. For restore failures, check NuGet connectivity, proxy policy and the exact error.
   Do not blindly upgrade packages or change the target framework.
   [Install the SDK](https://learn.microsoft.com/en-us/dotnet/core/install/windows).

## Missing configuration or misleading startup exception

Use the [presence-only check](environment-variables.md#process-variables-versus-persistent-variables).
Set `ENDPOINT`, `DEPLOYMENT_NAME` and `TT_APIKEY` in the same terminal that launches
the application, with no surrounding whitespace.

The current exception strings misleadingly describe deployment name and TomTom key
as an Azure OpenAI endpoint. They are also passed as the exception's parameter-name
argument, not a custom error message. Follow the actual variable name and
[configuration table](environment-variables.md#application-variables-complete-list),
not that wording. A TomTom key is not a URL.

If you used Windows persistent environment settings or `setx`, reopen the terminal
or explicitly set the current process variables. Creating `.env` alone does nothing;
there is no automatic file loader.

## Foundry endpoint or model is rejected

- **Malformed URI:** copy the complete HTTPS project endpoint without placeholder
  brackets, quotes inside the value, or whitespace.
- **404 / wrong endpoint:** use
  `https://<resource>.services.ai.azure.com/api/projects/<project>`, not a bare
  Azure OpenAI resource URL or a `/models` endpoint.
- **Deployment not found:** use the deployment name associated with that project,
  not an arbitrary model catalog name or a deployment in another resource.
- **Unsupported operation or tools:** select a deployment supporting Responses API
  and function/tool calling, with capacity in its region.

See [Foundry setup and connection details](https://learn.microsoft.com/en-us/azure/foundry/tutorials/quickstart-create-foundry-resources).
Other upstream examples use different environment-variable names; this sample
requires the exact names above.

## Azure authentication or authorization fails

1. Sign in with `az login`, or `az login --tenant '<tenant-id>'` for the project's tenant.
2. For a CLI-only local session, optionally set:

   ```powershell
   $env:AZURE_TOKEN_CREDENTIALS = 'AzureCliCredential'
   ```

3. Ensure that identity has project/model permissions. A successful login is not
   authorization. Ask an administrator to verify the **Foundry User** role
   (formerly **Azure AI User**) or equivalent permissions at the appropriate scope.
4. Check whether stale service-principal variables select another identity. Do not
   dump the environment to debug this. Inspect only the presence of the relevant
   [SDK settings](environment-variables.md#azure-authentication-sdk-dependencies-not-application-settings).

An interactive browser is not enabled by the sample's default credential constructor.
IDE and CLI credentials can refer to different accounts. Credentials from deployed
environments can fail without falling back to your developer-tool login.
See [Microsoft's credential-chain guide](https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/credential-chains).

## TomTom connection or tool discovery fails

- **401 / 403:** verify the key is current and enabled for **MCP Server** and the
  APIs you need. Check the key's configuration in your TomTom account.
- **Quota / throttling:** check TomTom usage and plan limits. Model rate limits
  belong to Foundry and are separate.
- **Network/TLS errors:** allow outbound HTTPS to `mcp.tomtom.com` and the relevant
  Azure endpoints. Follow your proxy/certificate policy; do not disable TLS validation.
- **Unexpected or missing tools:** the hosted service and key entitlements determine
  what is returned. The application has no static catalog or fixed count.

[TomTom's setup guide](https://docs.tomtom.com/tomtom-maps-mcp/documentation/quick-setup)
documents the remote endpoint, header and API access. The URL is hardcoded in
`Program.cs`; editor MCP settings or a new environment variable do not override it.

## No prompt, streaming or second question

- Before **Ask your question**, the app is still validating configuration,
  connecting to TomTom, listing tools or constructing the agent.
- After you submit a question, `RunAsync` waits for the entire answer. There is no
  token-by-token display or app-defined progress indicator.
- The process exits after one answer. Run it again for another independent question.
- Run in a real interactive terminal. The source has no noninteractive input mode
  or question command-line option.
- An answer without an obvious TomTom lookup is possible: the model decides whether
  a tool is needed. Ask a specific location question, but do not assume any answer
  proves a tool invocation.

## Console markup errors

Spectre.Console interprets square brackets as markup. The app escapes discovered
tool names but not all dynamic output, including the question and model answer.
Prompts or answers containing bracketed text/JSON can therefore render incorrectly
or throw markup exceptions.

Workaround: avoid square brackets in your question, ask for a short plain-text answer,
and retry. This reduces the risk but cannot guarantee the model's output format.
A complete fix requires escaping dynamic output in source; it is not configurable
through environment variables. See
[Spectre.Console escaping guidance](https://spectreconsole.net/console/how-to/escaping-markup/).

## Safe information to collect

Record the failing stage, exception type/status, SDK version and whether each
required variable is present. Redact resource/project identifiers if sensitive.
The application reveals part of the TomTom key and prints the full endpoint and
deployment name; do not assume its output is safe to share.
