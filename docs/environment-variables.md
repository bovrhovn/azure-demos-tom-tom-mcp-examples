# 🔐 Environment variables

[Documentation index](README.md) · [Getting started](getting-started.md) · [Troubleshooting](troubleshooting.md)

## Application variables: complete list

[`Program.cs`](../src/TTSLN/TT.Basic/Program.cs) reads **exactly three** variables using
`Environment.GetEnvironmentVariable`. All are required, have no defaults, and are
checked for null or empty strings at startup. Whitespace is not rejected by those
checks; supply real, non-whitespace values.

| Exact name | Required | Value and example placeholder | Where to obtain it | Secret? |
|---|---|---|---|---|
| `ENDPOINT` | Yes | Full Foundry **project** endpoint: `https://<resource>.services.ai.azure.com/api/projects/<project>` | Select your project in Foundry and copy the project endpoint from its welcome screen; see [Microsoft's connection-details instructions](https://learn.microsoft.com/en-us/azure/foundry/tutorials/quickstart-create-foundry-resources#get-your-project-connection-details). | Not a credential, but identifies your infrastructure; printed unmasked. |
| `DEPLOYMENT_NAME` | Yes | The deployed model's **deployment name**, such as your own `<model-deployment-name>` | Copy the name of a ready deployment in your project; see [Microsoft's model-deployment instructions](https://learn.microsoft.com/en-us/azure/foundry/tutorials/quickstart-create-foundry-resources#deploy-a-model). Choose a deployment supporting Responses API and function/tool calling. | No; printed unmasked. |
| `TT_APIKEY` | Yes | Your TomTom API key; no default or embedded key | Follow [TomTom's API-key guide](https://docs.tomtom.com/platform/documentation/my-tomtom/how-to-get-a-tomtom-api-key), then check [MCP product/API access](https://docs.tomtom.com/tomtom-maps-mcp/documentation/quick-setup). Passed in the `tomtom-api-key` HTTP header. | **Yes**; only partially masked in console output. |

The endpoint is passed to `AIProjectClient`, and the deployment name is passed to
`AsAIAgent(model: ...)`. Do **not** use a bare `*.openai.azure.com` resource URL, a
`/models` endpoint, a portal browser URL, a connection string, or a full
`/openai/v1/responses` request URL for `ENDPOINT`. Copy the project endpoint instead.
See the [Foundry integration contract](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry).

Upstream examples may call these settings `FOUNDRY_PROJECT_ENDPOINT` or other names.
This application does not read those aliases. Likewise, `AZURE_OPENAI_API_KEY`,
`OPENAI_API_KEY`, an Azure subscription ID and an agent ID are **not required
application variables**.

## Azure authentication: SDK dependencies, not application settings

The source constructs `new DefaultAzureCredential()` from **Azure.Identity 1.21.0**.
Foundry uses Microsoft Entra tokens; the TomTom key does not authenticate to Azure.
For local development, sign in with `az login` as shown in
[getting started](getting-started.md#4-sign-in-and-configure-this-terminal).
**No `AZURE_*` variables are required when a suitable developer-tool login succeeds.**

The credential chain can also consume the following settings. Set only those for
your chosen method; they are not additional `Program.cs` requirements.
These are supported credential inputs and controls, not an inventory of every
internal runtime, proxy or host-service variable read transitively by dependencies.
See [Microsoft's credential-chain guide](https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/credential-chains)
and the [version-pinned Azure Identity reference](https://github.com/Azure/azure-sdk-for-net/blob/Azure.Identity_1.21.0/sdk/identity/Azure.Identity/README.md#environment-variables).

| Variable | When needed | Value source and handling |
|---|---|---|
| `AZURE_TENANT_ID` | Required for service-principal secret/certificate and workload identity authentication | Directory (tenant) ID of your Microsoft Entra tenant. For an app registration, find its Overview alongside the client ID; see [register an application](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app). Not a secret. |
| `AZURE_CLIENT_ID` | Required for service-principal and workload identity flows; optional selector for a user-assigned managed identity | Application (client) ID from the app registration's Overview, or the managed identity's **client** ID, not its object ID. See [app registration](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app) and [managed identity guidance](https://github.com/Azure/azure-sdk-for-net/blob/Azure.Identity_1.21.0/sdk/identity/Azure.Identity/README.md). Not a secret. |
| `AZURE_CLIENT_SECRET` | Required only for service-principal secret authentication, with tenant and client IDs | App registration → Certificates & secrets → Client secrets → New client secret. Copy the **Value**, not the secret ID; it is shown once. [Credential instructions](https://learn.microsoft.com/en-us/entra/identity-platform/how-to-add-credentials). **Secret**; prefer CLI login locally or secretless identity on an appropriate host. |
| `AZURE_CLIENT_CERTIFICATE_PATH` | Required for certificate authentication instead of a client secret | Path to an approved PEM/PFX certificate containing its private key, or the SDK-supported certificate-store reference. An administrator provisions the certificate; upload its public portion to the app registration per [credential instructions](https://learn.microsoft.com/en-us/entra/identity-platform/how-to-add-credentials). Protect the private key and file. |
| `AZURE_CLIENT_CERTIFICATE_PASSWORD` | Optional, when the PFX is password-protected | Password assigned when exporting/provisioning the PFX. Obtain it securely from the certificate owner, not the portal's public certificate. **Secret**. |
| `AZURE_CLIENT_SEND_CERTIFICATE_CHAIN` | Optional, for subject-name/issuer certificate authentication | Set `true` or `1` only if your identity administrator requires sending the certificate chain (`x5c`). See [EnvironmentCredential](https://learn.microsoft.com/en-us/dotnet/api/azure.identity.environmentcredential?view=azure-dotnet). Not a secret. |
| `AZURE_FEDERATED_TOKEN_FILE` | Required for workload identity, with tenant and client IDs | Path to the projected federated token file supplied by the workload host. Do not paste a token into this variable. See [Microsoft Entra Workload ID on AKS](https://learn.microsoft.com/en-us/azure/aks/workload-identity-overview). Token file contents are **secret**. |
| `AZURE_AUTHORITY_HOST` | Optional; public Azure normally needs no override | Microsoft Entra authority URL for your cloud, chosen by your administrator. Public Azure defaults to `https://login.microsoftonline.com/`. Developer tools such as Azure CLI use their own cloud configuration. [SDK cloud guidance](https://github.com/Azure/azure-sdk-for-net/blob/Azure.Identity_1.21.0/sdk/identity/Azure.Identity/README.md#sovereign-cloud-configuration). This does not change `ENDPOINT` or guarantee model availability in another cloud. |
| `AZURE_TOKEN_CREDENTIALS` | Optional; narrows `DefaultAzureCredential` | For this tutorial, `AzureCliCredential` selects the CLI login only. `dev` limits to developer credentials and `prod` to deployed-service credentials. It does not create credentials or grant permissions. [Supported values and version requirements](https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/credential-chains#use-a-specific-credential). |

A complete service-principal secret configuration takes precedence over certificate
authentication. A misconfigured deployed credential can stop the chain rather than
fall back to your CLI login. Every selected identity still needs permission to the
Foundry project/model. Configure managed or workload identity on its supporting
host; setting an ID on a normal laptop does not create a managed identity.
This repository provides no cloud deployment or identity provisioning scripts.

## Process variables versus persistent variables

Recommended session setup (PowerShell 7.1+):

```powershell
$env:ENDPOINT = 'https://<resource>.services.ai.azure.com/api/projects/<project>'
$env:DEPLOYMENT_NAME = '<your-model-deployment-name>'
$env:TT_APIKEY = Read-Host 'TomTom API key' -MaskInput
```

`$env:...` sets the current PowerShell process environment. A child `dotnet run`
inherits it; another existing terminal or IDE does not. Reopening the terminal
normally loses these session-only assignments.

On Windows, a non-secret value can be stored for **future** user processes:

```powershell
[Environment]::SetEnvironmentVariable(
    'ENDPOINT',
    'https://<resource>.services.ai.azure.com/api/projects/<project>',
    'User'
)
```

Persistent user/system settings do not update the current process. Open a new
terminal (and restart your IDE if necessary), or also set `$env:ENDPOINT` now.
`setx` has the same future-process limitation. Avoid persisting secrets this way:
environment variables are not an encrypted secret store.

Check presence without printing values:

```powershell
'ENDPOINT', 'DEPLOYMENT_NAME', 'TT_APIKEY' | ForEach-Object {
    [pscustomobject]@{
        Name = $_
        Present = -not [string]::IsNullOrWhiteSpace(
            [Environment]::GetEnvironmentVariable($_))
    }
}
```

Clear a session secret with `Remove-Item Env:\TT_APIKEY -ErrorAction SilentlyContinue`.
To remove a separately persisted user value, use
`[Environment]::SetEnvironmentVariable('TT_APIKEY', $null, 'User')` and restart
affected processes; this does not remove existing process copies.

## No automatic file-based configuration

There is no dotenv loader, configuration builder or user-secrets integration in
the application. Merely creating `.env`, `appsettings.json` or an MCP client
configuration file does not configure it. Export variables in the parent process
or use your IDE's environment-injection mechanism.

Never commit keys, tokens, private keys or real credential examples. The repository
ignores `*.env`, but that does **not** cover every name such as `.env.local`; never
rely on ignore rules as your only protection. Use placeholders in shared commands.
The key masking helper reveals the first and last four characters of keys longer
than eight characters; shorter keys are entirely masked. Treat terminal captures
as sensitive, and rotate a key through its provider if exposed.

## Hardcoded choices: not environment variables

| Choice in source | Current behavior |
|---|---|
| `TomTomMCPRemoteUrl` constant | `https://mcp.tomtom.com/maps?apps=false`; data tools, no interactive maps |
| HTTP transport and header | `HttpClientTransport`, with `tomtom-api-key` |
| Agent name and instructions | `TomTomBasic`; friendly TomTom assistant instructed to keep answers brief |
| Default question | `Where is Paris located? Give me coordinates of Eiffel tower.` |
| Execution and UI | One interactive question, `RunAsync`, one completed answer, then exit |

There are no application environment switches for the MCP URL, model API version,
tool approval, streaming or agent instructions. Changing these requires a source change.
