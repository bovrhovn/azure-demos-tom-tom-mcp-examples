#Requires -Version 7.2
<#
.SYNOPSIS
Builds in ACR Tasks and deploys Location Intelligence using the current Azure CLI login.
.DESCRIPTION
Resource names are mandatory; environment-specific defaults and credentials are not saved.
Creates a dedicated VNet/environment, private Cosmos/Foundry connections, a managed identity,
an Entra Easy Auth registration, and a manually triggered end-to-end verification job.
Supply TomTomApiKey as a SecureString, TOMTOM_API_KEY, or Agent:TomTomApiKey in .NET user secrets.
Requires Azure resource/RBAC permissions and permission to create Entra applications.
Rerun with the same AppName to update the same deployment. Existing shared services retain
their public access configuration. Easy Auth credentials expire after AuthSecretDays.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ResourceGroup,
    [Parameter(Mandatory)][string] $RegistryName,
    [Parameter(Mandatory)][string] $CosmosAccountName,
    [Parameter(Mandatory)][string] $FoundryAccountName,
    [Parameter(Mandatory)][string] $FoundryProjectName,
    [Parameter(Mandatory)][string] $ModelDeployment,
    [Parameter(Mandatory)][string] $ApplicationInsightsName,
    [Parameter(Mandatory)][ValidateLength(2, 24)][ValidatePattern('^[a-z](?!.*--)[a-z0-9-]*[a-z0-9]$')][string] $AppName,
    [string] $SubscriptionId,
    [string] $Location,
    [string] $NpmRegistry,
    [securestring] $TomTomApiKey,
    [string] $DatabaseName = 'LocationIntelligence',
    [string] $ContainerName = 'Conversations',
    [string] $VnetAddressPrefix = '10.73.0.0/16',
    [string] $AppSubnetPrefix = '10.73.0.0/23',
    [string] $PrivateEndpointSubnetPrefix = '10.73.2.0/24',
    [ValidateRange(1, 730)][int] $AuthSecretDays = 180,
    [ValidateRange(1, 30)][int] $MaxReplicas = 1,
    [ValidateRange(1, 60)][int] $VerificationTimeoutMinutes = 15,
    [string] $ImageTag,
    [switch] $SkipImageBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src\TTSLN\TT.Web.LocationIntelligence'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ("tt-deploy-" + [guid]::NewGuid().ToString('N'))
$step = 0
$azureExecutable = (Get-Command az -ErrorAction Stop).Source
$azurePrefix = @()
$azurePython = Join-Path (Split-Path $azureExecutable -Parent) '..\python.exe'
if ($IsWindows -and (Test-Path -LiteralPath $azurePython)) {
    # The Windows CLI launcher isolates Python and ignores PYTHONIOENCODING.
    $azureExecutable = $azurePython
    $azurePrefix = @('-I', '-X', 'utf8', '-m', 'azure.cli')
}

function Write-Step([string] $Message) {
    $script:step++
    Write-Host "`n[$script:step] $Message" -ForegroundColor Cyan
}

function Invoke-Az([string[]] $Arguments, [switch] $Stream) {
    if ($Stream) {
        & $script:azureExecutable @script:azurePrefix @Arguments --only-show-errors
        if ($LASTEXITCODE -ne 0) { throw "Azure CLI failed: $($Arguments[0..1] -join ' ')." }
        return
    }
    $result = & $script:azureExecutable @script:azurePrefix @Arguments --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw "Azure CLI failed: $($Arguments[0..1] -join ' '). See diagnostics above." }
    if ($result) { ($result -join "`n") | ConvertFrom-Json -Depth 100 }
}

function Write-JsonFile([string] $Name, $Value) {
    $path = Join-Path $temporary $Name
    $Value | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $path -Encoding utf8NoBOM
    return $path
}

function Find-Resource([string] $Name, [string] $Type) {
    $items = @(Invoke-Az @('resource', 'list', '--name', $Name, '--resource-type', $Type))
    if ($items.Count -ne 1) { throw "Expected exactly one $Type named $Name in the selected subscription; found $($items.Count)." }
    return $items[0]
}

function Ensure-Role([string] $Scope, [string] $PrincipalId, [string] $Role) {
    $existing = @(Invoke-Az @('role', 'assignment', 'list', '--scope', $Scope,
        '--assignee-object-id', $PrincipalId, '--query', "[?roleDefinitionName=='$Role']"))
    if (!$existing.Count) {
        $null = Invoke-Az @('role', 'assignment', 'create', '--scope', $Scope,
            '--assignee-object-id', $PrincipalId, '--assignee-principal-type', 'ServicePrincipal', '--role', $Role)
    }
    Write-Host "Role verified: $Role ($Scope)"
}

function Ensure-PrivateEndpoint([string] $Suffix, [string] $TargetId, [string] $GroupId, [string[]] $Zones) {
    $name = "$AppName-$Suffix-pe"
    $endpoints = @(Invoke-Az @('network', 'private-endpoint', 'list', '-g', $ResourceGroup,
        '--query', "[?name=='$name']"))
    if (!$endpoints.Count) {
        $null = Invoke-Az @('network', 'private-endpoint', 'create', '-g', $ResourceGroup, '-n', $name,
            '-l', $Location, '--vnet-name', "$AppName-vnet", '--subnet', 'private-endpoints',
            '--private-connection-resource-id', $TargetId, '--group-id', $GroupId,
            '--connection-name', "$AppName-$Suffix")
    }
    foreach ($zone in $Zones) {
        $existingZones = @(Invoke-Az @('network', 'private-dns', 'zone', 'list', '-g', $ResourceGroup,
            '--query', "[?name=='$zone']"))
        if (!$existingZones.Count) {
            $null = Invoke-Az @('network', 'private-dns', 'zone', 'create', '-g', $ResourceGroup, '-n', $zone)
        }
        $links = @(Invoke-Az @('network', 'private-dns', 'link', 'vnet', 'list', '-g', $ResourceGroup,
            '-z', $zone, '--query', "[?name=='$AppName-link']"))
        if (!$links.Count) {
            $null = Invoke-Az @('network', 'private-dns', 'link', 'vnet', 'create', '-g', $ResourceGroup,
                '-z', $zone, '-n', "$AppName-link", '--virtual-network', $script:vnet.id,
                '--registration-enabled', 'false')
        }
        $zoneId = "/subscriptions/$script:subscription/resourceGroups/$ResourceGroup/providers/Microsoft.Network/privateDnsZones/$zone"
        $zoneGroups = @(Invoke-Az @('network', 'private-endpoint', 'dns-zone-group', 'list',
            '-g', $ResourceGroup, '--endpoint-name', $name))
        if (!$zoneGroups.Count) {
            $null = Invoke-Az @('network', 'private-endpoint', 'dns-zone-group', 'create', '-g', $ResourceGroup,
                '--endpoint-name', $name, '-n', 'default', '--private-dns-zone', $zoneId, '--zone-name', ($zone -replace '\.', '-'))
        } elseif (!@($zoneGroups[0].privateDnsZoneConfigs | Where-Object { $_.privateDnsZoneId -eq $zoneId }).Count) {
            $null = Invoke-Az @('network', 'private-endpoint', 'dns-zone-group', 'add', '-g', $ResourceGroup,
                '--endpoint-name', $name, '-n', 'default', '--private-dns-zone', $zoneId, '--zone-name', ($zone -replace '\.', '-'))
        }
    }
    $endpoint = Invoke-Az @('network', 'private-endpoint', 'show', '-g', $ResourceGroup, '-n', $name)
    foreach ($connection in $endpoint.privateLinkServiceConnections) {
        if ($connection.privateLinkServiceConnectionState.status -ne 'Approved') {
            throw "Private endpoint $name is not approved."
        }
    }
    Write-Host "Private endpoint approved: $name"
}

try {
    $null = New-Item -ItemType Directory -Path $temporary
    Write-Step 'Validate Azure CLI login, target services, model deployment and TomTom secret'
    if ($SubscriptionId) { $null = Invoke-Az @('account', 'set', '--subscription', $SubscriptionId) }
    $account = Invoke-Az @('account', 'show')
    $subscription = $account.id
    Write-Host "Subscription: $($account.name) ($subscription)"
    $group = Invoke-Az @('group', 'show', '-n', $ResourceGroup)
    if (!$Location) { $Location = $group.location }
    Write-Host "Resource group: $ResourceGroup; region: $Location"
    $registryResource = Find-Resource $RegistryName 'Microsoft.ContainerRegistry/registries'
    $cosmosResource = Find-Resource $CosmosAccountName 'Microsoft.DocumentDB/databaseAccounts'
    $foundryResource = Find-Resource $FoundryAccountName 'Microsoft.CognitiveServices/accounts'
    $insights = Find-Resource $ApplicationInsightsName 'microsoft.insights/components'
    $registry = Invoke-Az @('acr', 'show', '-n', $RegistryName, '-g', $registryResource.resourceGroup)
    $cosmos = Invoke-Az @('cosmosdb', 'show', '-n', $CosmosAccountName, '-g', $cosmosResource.resourceGroup)
    $deployment = Invoke-Az @('cognitiveservices', 'account', 'deployment', 'show', '-g', $foundryResource.resourceGroup,
        '-n', $FoundryAccountName, '--deployment-name', $ModelDeployment)
    if ($deployment.properties.provisioningState -ne 'Succeeded') { throw 'The requested model deployment is not ready.' }
    $foundryProject = Invoke-Az @('resource', 'show', '--ids', "$($foundryResource.id)/projects/$FoundryProjectName",
        '--api-version', '2025-06-01')
    $foundryEndpoint = $foundryProject.properties.endpoints.'AI Foundry API'
    if (!$foundryEndpoint) { throw 'The Foundry project has no AI Foundry API endpoint.' }
    if (!$TomTomApiKey) {
        $plainKey = $env:TOMTOM_API_KEY
        if ([string]::IsNullOrWhiteSpace($plainKey)) {
            $userSecrets = & dotnet user-secrets list --project $project
            if ($LASTEXITCODE -ne 0) { throw 'Cannot load .NET user secrets.' }
            $keyEntry = @($userSecrets | Where-Object { $_ -match '^Agent:TomTomApiKey\s*=' })
            if ($keyEntry.Count -eq 1) { $plainKey = ($keyEntry[0] -split '=', 2)[1].Trim() }
        }
    } else {
        $plainKey = [System.Net.NetworkCredential]::new('', $TomTomApiKey).Password
    }
    if ([string]::IsNullOrWhiteSpace($plainKey)) { throw 'Supply TomTomApiKey, TOMTOM_API_KEY, or the Agent:TomTomApiKey .NET user secret.' }
    Write-Host "Model $ModelDeployment is available; TomTom key loaded (not displayed)."

    Write-Step 'Create or reuse the dedicated managed identity and least-privilege roles'
    $identity = Invoke-Az @('identity', 'create', '-g', $ResourceGroup, '-n', "$AppName-identity", '-l', $Location)
    $pullRole = if ($registry.roleAssignmentMode -eq 'AbacRepositoryPermissions') {
        'Container Registry Repository Reader'
    } else { 'AcrPull' }
    Ensure-Role $registry.id $identity.principalId $pullRole
    Ensure-Role $foundryProject.id $identity.principalId 'Foundry User'
    Ensure-Role $foundryResource.id $identity.principalId 'Cognitive Services OpenAI User'
    $cosmosScope = "$($cosmosResource.id)/dbs/$DatabaseName"
    $cosmosRoles = @(Invoke-Az @('cosmosdb', 'sql', 'role', 'assignment', 'list',
        '-g', $cosmosResource.resourceGroup, '-a', $CosmosAccountName))
    if (!@($cosmosRoles | Where-Object { $_.principalId -eq $identity.principalId -and $_.scope -eq $cosmosScope }).Count) {
        $null = Invoke-Az @('cosmosdb', 'sql', 'role', 'assignment', 'create', '-g', $cosmosResource.resourceGroup,
            '-a', $CosmosAccountName, '--principal-id', $identity.principalId, '--scope', "/dbs/$DatabaseName",
            '--role-definition-id', '00000000-0000-0000-0000-000000000002')
    }
    Write-Host 'Cosmos DB Built-in Data Contributor assigned at database scope (read/write, no account keys).'
    $null = Invoke-Az @('cosmosdb', 'sql', 'database', 'create', '-g', $cosmosResource.resourceGroup,
        '-a', $CosmosAccountName, '-n', $DatabaseName)
    $null = Invoke-Az @('cosmosdb', 'sql', 'container', 'create', '-g', $cosmosResource.resourceGroup,
        '-a', $CosmosAccountName, '-d', $DatabaseName, '-n', $ContainerName, '--partition-key-path', '/ownerId')
    $container = Invoke-Az @('cosmosdb', 'sql', 'container', 'show', '-g', $cosmosResource.resourceGroup,
        '-a', $CosmosAccountName, '-d', $DatabaseName, '-n', $ContainerName)
    if ($container.resource.partitionKey.paths[0] -ne '/ownerId') { throw 'Existing Cosmos partition key must be /ownerId.' }
    $null = Invoke-Az @('role', 'assignment', 'list', '--scope', $registry.id,
        '--assignee-object-id', $identity.principalId)
    Write-Host 'Registry pull role visible. Remaining propagation occurs while networking and image build complete.'

    Write-Step 'Create isolated VNet, delegated app subnet and private Cosmos/Foundry endpoints'
    $vnets = @(Invoke-Az @('network', 'vnet', 'list', '-g', $ResourceGroup, '--query', "[?name=='$AppName-vnet']"))
    if (!$vnets.Count) {
        $null = Invoke-Az @('network', 'vnet', 'create', '-g', $ResourceGroup, '-n', "$AppName-vnet", '-l', $Location,
            '--address-prefixes', $VnetAddressPrefix, '--subnet-name', 'container-apps', '--subnet-prefixes', $AppSubnetPrefix)
        $null = Invoke-Az @('network', 'vnet', 'subnet', 'update', '-g', $ResourceGroup, '--vnet-name', "$AppName-vnet",
            '-n', 'container-apps', '--delegations', 'Microsoft.App/environments')
        $null = Invoke-Az @('network', 'vnet', 'subnet', 'create', '-g', $ResourceGroup, '--vnet-name', "$AppName-vnet",
            '-n', 'private-endpoints', '--address-prefixes', $PrivateEndpointSubnetPrefix,
            '--private-endpoint-network-policies', 'Disabled')
    }
    $vnet = Invoke-Az @('network', 'vnet', 'show', '-g', $ResourceGroup, '-n', "$AppName-vnet")
    Ensure-PrivateEndpoint 'cosmos' $cosmosResource.id 'Sql' @('privatelink.documents.azure.com')
    Ensure-PrivateEndpoint 'foundry' $foundryResource.id 'account' @(
        'privatelink.cognitiveservices.azure.com', 'privatelink.openai.azure.com', 'privatelink.services.ai.azure.com')

    Write-Step 'Create the VNet-integrated Consumption Container Apps environment'
    $component = Invoke-Az @('resource', 'show', '--ids', $insights.id, '--api-version', '2020-02-02')
    $workspaceId = $component.properties.WorkspaceResourceId
    if (!$workspaceId) { throw 'Application Insights must be workspace-based.' }
    $workspace = Invoke-Az @('resource', 'show', '--ids', $workspaceId, '--api-version', '2023-09-01')
    $workspaceKeys = Invoke-Az @('rest', '--method', 'post', '--url',
        "https://management.azure.com$workspaceId/sharedKeys?api-version=2023-09-01")
    $environments = @(Invoke-Az @('containerapp', 'env', 'list', '-g', $ResourceGroup,
        '--query', "[?name=='$AppName-env']"))
    if (!$environments.Count) {
        $null = Invoke-Az @('containerapp', 'env', 'create', '-g', $ResourceGroup, '-n', "$AppName-env", '-l', $Location,
            '--enable-workload-profiles', 'true', '--infrastructure-subnet-resource-id', "$($vnet.id)/subnets/container-apps",
            '--logs-workspace-id', $workspace.properties.customerId, '--logs-workspace-key', $workspaceKeys.primarySharedKey)
    }
    $environment = Invoke-Az @('containerapp', 'env', 'show', '-g', $ResourceGroup, '-n', "$AppName-env")
    if ($environment.properties.provisioningState -ne 'Succeeded') { throw 'Container Apps environment is not ready.' }
    if ($environment.properties.vnetConfiguration.infrastructureSubnetId -ne "$($vnet.id)/subnets/container-apps") {
        throw 'Existing environment is not attached to the expected subnet.'
    }

    Write-Step 'Build and push the Linux image using ACR Tasks (no local Docker required)'
    $imageRepository = "$AppName/web"
    if ($SkipImageBuild) {
        if ($ImageTag) {
            $null = Invoke-Az @('acr', 'repository', 'show', '-n', $RegistryName, '--image', "${imageRepository}:$ImageTag")
            $image = "$($registry.loginServer)/${imageRepository}:$ImageTag"
        } else {
            $existingApp = Invoke-Az @('containerapp', 'show', '-g', $ResourceGroup, '-n', $AppName)
            $image = $existingApp.properties.template.containers[0].image
        }
    } else {
        $context = Join-Path $temporary 'build'
        $null = New-Item -ItemType Directory -Path $context
        foreach ($item in @('Client', 'Configuration', 'Features', 'Options', 'Pages', 'wwwroot',
            'Program.cs', 'TT.Web.LocationIntelligence.csproj', 'appsettings.json', 'package.json',
            'package-lock.json', 'vite.config.js')) {
            Copy-Item -LiteralPath (Join-Path $project $item) -Destination $context -Recurse
        }
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LocationIntelligence.Dockerfile') -Destination (Join-Path $context 'Dockerfile')
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'verify-location-intelligence.sh') -Destination $context
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'verify-travel.sh') -Destination $context
        $excluded = "**/node_modules`n**/bin`n**/obj`nwwwroot/dist`n**/.env*`n**/*secrets*`n**/*.pfx`n**/*.pem`n"
        Set-Content -LiteralPath (Join-Path $context '.dockerignore') -Value $excluded -Encoding utf8NoBOM
        if (!$ImageTag) { $ImageTag = [DateTime]::UtcNow.ToString('yyyyMMddHHmmss') }
        $buildArguments = @('acr', 'build', '-r', $RegistryName, '-g', $registryResource.resourceGroup,
            '-t', "${imageRepository}:$ImageTag", '--platform', 'linux/amd64')
        if ($registry.roleAssignmentMode -eq 'AbacRepositoryPermissions') {
            $buildArguments += @('--source-acr-auth-id', '[caller]')
        }
        if ($NpmRegistry) { $buildArguments += @('--build-arg', "NPM_CONFIG_REGISTRY=$NpmRegistry") }
        Invoke-Az ($buildArguments + @($context)) -Stream
        $image = "$($registry.loginServer)/${imageRepository}:$ImageTag"
    }

    Write-Step 'Create/reuse the single-tenant Entra registration and issue an Easy Auth credential'
    $displayName = "$AppName-easyauth"
    $registrations = @(Invoke-Az @('ad', 'app', 'list', '--display-name', $displayName))
    if ($registrations.Count -gt 1) { throw "Multiple Entra applications named $displayName; resolve ambiguity before retrying." }
    if (!$registrations.Count) {
        $registration = Invoke-Az @('ad', 'app', 'create', '--display-name', $displayName,
            '--sign-in-audience', 'AzureADMyOrg', '--enable-id-token-issuance', 'true')
        $null = Invoke-Az @('ad', 'sp', 'create', '--id', $registration.appId)
    } else { $registration = $registrations[0] }
    $url = "https://$AppName.$($environment.properties.defaultDomain)"
    $null = Invoke-Az @('ad', 'app', 'update', '--id', $registration.appId,
        '--web-redirect-uris', "$url/.auth/login/aad/callback", '--enable-id-token-issuance', 'true')
    $authSecretExpiry = [DateTime]::UtcNow.AddDays($AuthSecretDays).ToString('yyyy-MM-ddTHH:mm:ssZ')
    $credential = Invoke-Az @('ad', 'app', 'credential', 'reset', '--id', $registration.appId, '--append',
        '--display-name', "$AppName-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))",
        '--end-date', $authSecretExpiry)

    Write-Step 'Deploy application configuration, identity image pulls, secrets and health probes'
    $secrets = @(
        @{ name = 'tomtom-api-key'; value = $plainKey },
        @{ name = 'application-insights'; value = $component.properties.ConnectionString },
        @{ name = 'entra-client-secret'; value = $credential.password }
    )
    $variables = @(
        @{ name = 'ASPNETCORE_ENVIRONMENT'; value = 'Production' },
        @{ name = 'ASPNETCORE_HTTP_PORTS'; value = '8080' },
        @{ name = 'AZURE_CLIENT_ID'; value = $identity.clientId },
        @{ name = 'Cosmos__Endpoint'; value = $cosmos.documentEndpoint },
        @{ name = 'Cosmos__Database'; value = $DatabaseName },
        @{ name = 'Cosmos__Container'; value = $ContainerName },
        @{ name = 'Cosmos__UseEmulator'; value = 'false' },
        @{ name = 'Agent__Endpoint'; value = $foundryEndpoint },
        @{ name = 'Agent__Model'; value = $ModelDeployment },
        @{ name = 'Agent__TomTomApiKey'; secretRef = 'tomtom-api-key' },
        @{ name = 'ApplicationInsights__ConnectionString'; secretRef = 'application-insights' },
        @{ name = 'APPLICATIONINSIGHTS_CONNECTION_STRING'; secretRef = 'application-insights' },
        @{ name = 'ReverseProxy__TrustAllProxies'; value = 'true' },
        @{ name = 'Deployment__ResourceGroup'; value = $ResourceGroup },
        @{ name = 'Deployment__Location'; value = $Location },
        @{ name = 'Deployment__RegistryName'; value = $RegistryName },
        @{ name = 'Deployment__Registry'; value = $registry.loginServer },
        @{ name = 'Deployment__CosmosAccount'; value = $CosmosAccountName },
        @{ name = 'Deployment__ApplicationInsights'; value = $ApplicationInsightsName },
        @{ name = 'Deployment__FoundryAccount'; value = $FoundryAccountName },
        @{ name = 'Deployment__FoundryProject'; value = $FoundryProjectName },
        @{ name = 'Deployment__AuthClientId'; value = $registration.appId },
        @{ name = 'Deployment__TenantId'; value = $account.tenantId }
    )
    $containerDefinition = @{
        name = 'web'; image = $image; env = $variables
        resources = @{ cpu = 0.5; memory = '1Gi' }
        probes = @(
            @{ type = 'Startup'; httpGet = @{ path = '/health'; port = 8080 }; periodSeconds = 5; failureThreshold = 60 },
            @{ type = 'Liveness'; httpGet = @{ path = '/health'; port = 8080 }; periodSeconds = 30; failureThreshold = 3 },
            @{ type = 'Readiness'; httpGet = @{ path = '/health/ready'; port = 8080 }; periodSeconds = 10; timeoutSeconds = 10; failureThreshold = 6 }
        )
    }
    $definition = @{
        location = $Location
        identity = @{ type = 'UserAssigned'; userAssignedIdentities = @{ $identity.id = @{} } }
        properties = @{
            managedEnvironmentId = $environment.id
            configuration = @{
                activeRevisionsMode = 'Single'
                secrets = $secrets
                registries = @(@{ server = $registry.loginServer; identity = $identity.id })
                ingress = @{
                    external = $false; targetPort = 8080; transport = 'auto'; allowInsecure = $false
                    stickySessions = @{ affinity = 'sticky' }
                }
            }
            template = @{
                containers = @($containerDefinition)
                scale = @{ minReplicas = 1; maxReplicas = $MaxReplicas }
            }
        }
    }
    $definitionPath = Write-JsonFile 'app.json' $definition
    $null = Invoke-Az @('containerapp', 'create', '-g', $ResourceGroup, '-n', $AppName, '--yaml', $definitionPath)

    Write-Step 'Require HTTPS and Entra Easy Auth before enabling public browser ingress'
    $null = Invoke-Az @('containerapp', 'auth', 'microsoft', 'update', '-g', $ResourceGroup, '-n', $AppName,
        '--client-id', $registration.appId, '--client-secret-name', 'entra-client-secret',
        '--issuer', "https://login.microsoftonline.com/$($account.tenantId)/v2.0",
        '--allowed-token-audiences', $registration.appId, '--yes')
    $null = Invoke-Az @('containerapp', 'auth', 'update', '-g', $ResourceGroup, '-n', $AppName,
        '--enabled', 'true', '--require-https', 'true', '--unauthenticated-client-action', 'RedirectToLoginPage',
        '--redirect-provider', 'azureActiveDirectory', '--excluded-paths', '/health,/health/ready', '--yes')
    $null = Invoke-Az @('containerapp', 'ingress', 'enable', '-g', $ResourceGroup, '-n', $AppName,
        '--type', 'external', '--target-port', '8080', '--transport', 'auto')

    Write-Step 'Run an in-network end-to-end verification job using the same image and identity'
    $jobContainer = @{
        name = 'verify'; image = $image; env = $variables
        resources = @{ cpu = 0.5; memory = '1Gi' }
        command = @('/bin/bash', '/app/verify-location-intelligence.sh')
    }
    $jobDefinition = @{
        location = $Location; identity = $definition.identity
        properties = @{
            environmentId = $environment.id
            configuration = @{
                triggerType = 'Manual'; replicaTimeout = $VerificationTimeoutMinutes * 60; replicaRetryLimit = 0
                manualTriggerConfig = @{ parallelism = 1; replicaCompletionCount = 1 }
                secrets = $secrets
                registries = $definition.properties.configuration.registries
            }
            template = @{ containers = @($jobContainer) }
        }
    }
    $jobPath = Write-JsonFile 'verify-job.json' $jobDefinition
    $existingJobs = @(Invoke-Az @('containerapp', 'job', 'list', '-g', $ResourceGroup,
        '--query', "[?name=='$AppName-verify']"))
    $jobOperation = if ($existingJobs.Count) { 'update' } else { 'create' }
    $null = Invoke-Az @('containerapp', 'job', $jobOperation, '-g', $ResourceGroup, '-n', "$AppName-verify", '--yaml', $jobPath)
    $execution = Invoke-Az @('containerapp', 'job', 'start', '-g', $ResourceGroup, '-n', "$AppName-verify")
    $deadline = [DateTime]::UtcNow.AddMinutes($VerificationTimeoutMinutes)
    do {
        Start-Sleep -Seconds 15
        $executionState = Invoke-Az @('containerapp', 'job', 'execution', 'show', '-g', $ResourceGroup,
            '-n', "$AppName-verify", '--job-execution-name', $execution.name)
        Write-Host "Verification $($execution.name): $($executionState.properties.status)"
    } while ($executionState.properties.status -in @('Running', 'Processing', 'Pending') -and [DateTime]::UtcNow -lt $deadline)
    Invoke-Az @('containerapp', 'job', 'logs', 'show', '-g', $ResourceGroup, '-n', "$AppName-verify",
        '--execution', $execution.name, '--container', 'verify', '--tail', '200') -Stream
    if ($executionState.properties.status -ne 'Succeeded') { throw 'End-to-end verification did not succeed. Inspect the job logs above.' }

    Write-Step 'Verify the live app, readiness endpoint and unauthenticated Easy Auth redirect'
    $liveApp = Invoke-Az @('containerapp', 'show', '-g', $ResourceGroup, '-n', $AppName)
    if ($liveApp.properties.runningStatus -ne 'Running') { throw 'Container app is not Running.' }
    $ready = Invoke-WebRequest "$url/health/ready" -TimeoutSec 60
    if ($ready.StatusCode -ne 200) { throw 'The deployed application is not ready.' }
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $http = [Net.Http.HttpClient]::new($handler)
    try {
        $http.DefaultRequestHeaders.Accept.ParseAdd('text/html')
        $http.DefaultRequestHeaders.UserAgent.ParseAdd('Mozilla/5.0')
        foreach ($path in @('/', '/api/sessions/', '/hubs/chat/negotiate')) {
            $response = $http.GetAsync("$url$path").GetAwaiter().GetResult()
            if ([int]$response.StatusCode -ne 302 -or $response.Headers.Location.Host -ne 'login.microsoftonline.com' -or
                $response.Headers.Location.Query -notlike "*client_id=$($registration.appId)*") {
                throw "Easy Auth did not protect $path with an Entra sign-in redirect."
            }
        }
        $login = $http.GetAsync("$url/.auth/login/aad").GetAwaiter().GetResult()
        if ([int]$login.StatusCode -ne 302 -or $login.Headers.Location.Host -ne 'login.microsoftonline.com' -or
            $login.Headers.Location.Query -notlike "*client_id=$($registration.appId)*") {
            throw 'Easy Auth could not initiate sign-in with the configured Entra application.'
        }
        $http.DefaultRequestHeaders.Accept.Clear()
        $http.DefaultRequestHeaders.UserAgent.Clear()
        $apiResponse = $http.GetAsync("$url/api/sessions/").GetAwaiter().GetResult()
        if ([int]$apiResponse.StatusCode -ne 401) { throw 'Easy Auth did not reject an unauthenticated API request.' }
    } finally { $http.Dispose(); $handler.Dispose() }
    $null = Invoke-Az @('cosmosdb', 'sql', 'role', 'assignment', 'list', '-g', $cosmosResource.resourceGroup, '-a', $CosmosAccountName)
    Ensure-Role $registry.id $identity.principalId $pullRole
    Ensure-Role $foundryProject.id $identity.principalId 'Foundry User'
    Write-Host "`nDeployment and backend verification succeeded." -ForegroundColor Green
    Write-Host "Open: $url"
    Write-Host "Direct Entra sign-in: $url/.auth/login/aad"
    Write-Host "Easy Auth credential expires $authSecretExpiry. Rerun the script before expiry to issue a new credential."
    Write-Host 'Verification job is retained for repeat checks; it runs only when manually triggered.'
    Write-Host 'Interactive sign-in must be completed in your browser; no login bypass was configured.'
} finally {
    $plainKey = $null
    $credential = $null
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
