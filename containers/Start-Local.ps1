[CmdletBinding()]
param(
    [string]$Image = "tt-location-intelligence:development",
    [string]$AppName = "tt-location-intelligence",
    [string]$EmulatorName = "tt-location-cosmos",
    [string]$Network = "tt-location-network",
    [int]$Port = 8088,
    [int]$CosmosPort = 8081,
    [switch]$SkipBuild,
    [string]$NpmRegistry,
    [string]$NugetSource
)
$ErrorActionPreference = "Stop"

function Invoke-Wslc {
    & wslc @args
    if ($LASTEXITCODE -ne 0) { throw "wslc command failed: $($args[0])" }
}

$root = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) {
    $buildArgs = @()
    if ($NpmRegistry) { $buildArgs += @("--build-arg", "NPM_REGISTRY=$NpmRegistry") }
    if ($NugetSource) { $buildArgs += @("--build-arg", "NUGET_SOURCE=$NugetSource") }
    Invoke-Wslc build --target development -f "$PSScriptRoot\LocationIntelligence.Dockerfile" -t $Image @buildArgs $root
}
$networks = & wslc network list --format json
if ($LASTEXITCODE -ne 0) { throw "Cannot list WSLC networks." }
$networkNames = @($networks | ForEach-Object { ($_ | ConvertFrom-Json).Name })
if ($Network -notin $networkNames) { Invoke-Wslc network create $Network }
$volumes = @(& wslc volume list --quiet)
if ($LASTEXITCODE -ne 0) { throw "Cannot list WSLC volumes." }
foreach ($volume in @("tt-location-cosmos-data", "tt-location-azure-cli", "tt-location-dataprotection")) {
    if ($volume -notin $volumes) { Invoke-Wslc volume create $volume }
}

# Fail on existing container names rather than deleting another running instance.
Invoke-Wslc run -d --name $EmulatorName --network $Network --network-alias cosmos `
    -p "127.0.0.1:${CosmosPort}:8081" `
    --mount "type=volume,source=tt-location-cosmos-data,target=/data" `
    mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-latest --protocol https --enable-explorer false

$envArgs = @()
foreach ($name in @("Agent__Endpoint", "Agent__Model", "Agent__TomTomApiKey", "ApplicationInsights__ConnectionString")) {
    if ([Environment]::GetEnvironmentVariable($name)) { $envArgs += @("-e", $name) }
}
Invoke-Wslc run -d --name $AppName --network $Network -p "127.0.0.1:${Port}:8080" `
    -e ASPNETCORE_ENVIRONMENT=Development -e Cosmos__Endpoint=https://cosmos:8081 `
    -e AZURE_CONFIG_DIR=/home/app/.azure `
    --mount "type=volume,source=tt-location-azure-cli,target=/home/app/.azure" `
    --mount "type=volume,source=tt-location-dataprotection,target=/home/app/.aspnet/DataProtection-Keys" `
    @envArgs $Image
Invoke-Wslc exec -u root $AppName chown -R app:app /home/app/.azure /home/app/.aspnet

$deadline = [DateTime]::UtcNow.AddMinutes(3)
do {
    try {
        $response = Invoke-WebRequest "http://localhost:$Port/health/ready" -TimeoutSec 10
        if ($response.StatusCode -eq 200) { break }
    } catch [System.Net.Http.HttpRequestException] { Start-Sleep 3 }
} while ([DateTime]::UtcNow -lt $deadline)
if ($response.StatusCode -ne 200) { throw "Application database readiness did not succeed. Inspect wslc logs $AppName." }
Write-Host "Ready: http://localhost:$Port"
Write-Host "Sign in inside the container (the Windows CLI cache is not Linux-compatible):"
Write-Host "wslc exec -it $AppName az login --use-device-code"
