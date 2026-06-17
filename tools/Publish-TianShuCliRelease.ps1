param(
    [string]$Configuration = "Release",
    [string]$Version = $(if ($env:GITHUB_REF_NAME) { $env:GITHUB_REF_NAME } else { "dev" }),
    [string]$OutputRoot = "artifacts/release",
    [string[]]$RuntimeIdentifiers = @("win-x64", "linux-x64", "osx-arm64")
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $executionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Assert-PathUnderRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $resolvedPath = Resolve-FullPath $Path
    $resolvedRoot = (Resolve-FullPath $Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    if (-not $resolvedPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to write outside repository root: $resolvedPath"
    }
}

function Write-TextFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Value
    )

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    Set-Content -LiteralPath $Path -Encoding UTF8 -Value $Value
}

function Write-DefaultPortableConfig {
    param([Parameter(Mandatory = $true)][string]$PackageRoot)

    Write-TextFile -Path (Join-Path $PackageRoot "tianshu.toml") -Value @'
# TianShu portable package default configuration.
# This file stores public defaults and environment variable names only. Do not put secrets here.
profile = "default"
model_route_set = "default"
model_protocol_rule_set = "default"
provider_instances = "default"
approval_policy = "never"
sandbox_mode = "workspace-write"

[profiles.default]
model_route_set = "default"
'@

    Write-TextFile -Path (Join-Path $PackageRoot "modules/model/provider-instances/default.toml") -Value @'
# TianShu portable package provider templates.
# Replace base_url/model as needed; keep API keys in environment variables.

[providers.openai]
base_url = "https://api.openai.com"
api_key_env = "OPENAI_API_KEY"
default_protocol = "openai_responses"
protocol_fallbacks = ["openai_responses"]
request_max_retries = 1
stream_max_retries = 1
stream_idle_timeout_ms = 30000
websocket_connect_timeout_ms = 15000
supports_websockets = true

[providers.anthropic]
base_url = "https://api.anthropic.com"
api_key_env = "ANTHROPIC_API_KEY"
default_protocol = "anthropic_messages"
protocol_fallbacks = ["anthropic_messages"]
request_max_retries = 1
stream_max_retries = 1
stream_idle_timeout_ms = 30000
websocket_connect_timeout_ms = 15000
supports_websockets = false

[providers.openai-compatible]
base_url = "https://api.openai.com"
api_key_env = "OPENAI_COMPATIBLE_API_KEY"
default_protocol = "openai_chat_completions"
protocol_fallbacks = ["openai_chat_completions"]
request_max_retries = 1
stream_max_retries = 1
stream_idle_timeout_ms = 30000
websocket_connect_timeout_ms = 15000
supports_websockets = false
'@

    Write-TextFile -Path (Join-Path $PackageRoot "modules/model/route-sets/default.toml") -Value @'
[model_route_sets.default]
display_name = "TianShu portable default route set"
description = "Public portable package route set. Change provider/model after extraction if needed."
routes = [
  { kind = "default", candidates = [{ provider = "openai", model = "gpt-5.5", protocol = "openai_responses" }] }
]
'@

    Write-TextFile -Path (Join-Path $PackageRoot "modules/model/protocol-rules/default.toml") -Value @'
[model_protocol_rule_sets.default]
display_name = "TianShu portable protocol rules"
rules = [
  { match = "gpt-*", protocols = ["openai_responses"] },
  { match = "claude-*", protocols = ["anthropic_messages"] },
  { match = "*", protocols = ["openai_chat_completions"] }
]
'@
}

$repoRoot = Resolve-FullPath (Join-Path $PSScriptRoot "..")
$outputRootPath = Resolve-FullPath (Join-Path $repoRoot $OutputRoot)
$stagingRoot = Join-Path $outputRootPath "staging"
$packageRoot = Join-Path $outputRootPath "packages"
$cliProjectPath = Join-Path $repoRoot "src/Presentations/TianShu.Cli/TianShu.Cli.csproj"
$appHostProjectPath = Join-Path $repoRoot "src/Hosting/TianShu.AppHost/TianShu.AppHost.csproj"

Assert-PathUnderRoot -Path $outputRootPath -Root $repoRoot
if (Test-Path -LiteralPath $outputRootPath) {
    Remove-Item -LiteralPath $outputRootPath -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $stagingRoot, $packageRoot | Out-Null

$archiveRecords = @()
foreach ($rid in $RuntimeIdentifiers) {
    $packageName = "tianshu-$Version-$rid"
    $portableRoot = Join-Path $stagingRoot $packageName
    $binDirectory = Join-Path $portableRoot "bin"
    $appHostDirectory = Join-Path $portableRoot "runtime/apphost"
    New-Item -ItemType Directory -Force -Path $binDirectory, $appHostDirectory | Out-Null

    dotnet publish $cliProjectPath `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -o $binDirectory `
        --nologo `
        --verbosity:minimal

    dotnet publish $appHostProjectPath `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -o $appHostDirectory `
        --nologo `
        --verbosity:minimal

    Write-DefaultPortableConfig -PackageRoot $portableRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $portableRoot "README.md") -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $portableRoot "LICENSE") -Force
    Set-Content -LiteralPath (Join-Path $portableRoot "VERSION.txt") -Encoding UTF8 -Value @(
        "version=$Version"
        "runtimeIdentifier=$rid"
        "layout=portable-tianshu-home"
    )

    $publishedEntryPath = if ($rid.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) {
        Join-Path $binDirectory "TianShu.Cli.exe"
    }
    else {
        Join-Path $binDirectory "TianShu.Cli"
    }
    if (-not (Test-Path -LiteralPath $publishedEntryPath)) {
        throw "Published CLI entry is missing for ${rid}: $publishedEntryPath"
    }

    $friendlyEntryPath = if ($rid.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) {
        Join-Path $binDirectory "tianshu.exe"
    }
    else {
        Join-Path $binDirectory "tianshu"
    }
    Copy-Item -LiteralPath $publishedEntryPath -Destination $friendlyEntryPath -Force

    $appHostEntryPath = if ($rid.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) {
        Join-Path $appHostDirectory "TianShu.AppHost.exe"
    }
    else {
        Join-Path $appHostDirectory "TianShu.AppHost"
    }
    if (-not (Test-Path -LiteralPath $appHostEntryPath)) {
        throw "Published AppHost entry is missing for ${rid}: $appHostEntryPath"
    }

    if (Test-Path -LiteralPath (Join-Path $portableRoot "AGENTS.md")) {
        throw "Release package staging must not contain AGENTS.md."
    }

    if ($rid.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) {
        $archivePath = Join-Path $packageRoot "$packageName.zip"
        Compress-Archive -LiteralPath $portableRoot -DestinationPath $archivePath -Force
    }
    else {
        $archivePath = Join-Path $packageRoot "$packageName.tar.gz"
        Push-Location $stagingRoot
        try {
            tar -czf $archivePath $packageName
        }
        finally {
            Pop-Location
        }
    }

    $archiveItem = Get-Item -LiteralPath $archivePath
    $archiveHash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
    $archiveRecords += [ordered]@{
        runtimeIdentifier = $rid
        assetName = $archiveItem.Name
        relativePath = $archiveItem.Name
        sha256 = $archiveHash.Hash.ToLowerInvariant()
        sizeBytes = $archiveItem.Length
        layout = "portable-tianshu-home"
        entryPath = if ($rid.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) { "bin/tianshu.exe" } else { "bin/tianshu" }
        entryName = if ($rid.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) { "tianshu.exe" } else { "tianshu" }
        configPath = "tianshu.toml"
        modulesPath = "modules"
        appHostPath = if ($rid.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) { "runtime/apphost/TianShu.AppHost.exe" } else { "runtime/apphost/TianShu.AppHost" }
        selfContained = $true
        publishSingleFile = $false
        publishTrimmed = $false
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    version = $Version
    generatedAtUtc = [System.DateTimeOffset]::UtcNow.ToString("O")
    configuration = $Configuration
    runtimeIdentifiers = $RuntimeIdentifiers
    layout = "portable-tianshu-home"
    publishSingleFile = $false
    publishTrimmed = $false
    selfContained = $true
    archives = @($archiveRecords)
}

$manifestPath = Join-Path $packageRoot "release-manifest.json"
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host "TianShu portable release packages:"
foreach ($archive in $archiveRecords) {
    Write-Host "  $($archive.assetName)  $($archive.sha256)  $($archive.sizeBytes) bytes"
}
Write-Host "Manifest:"
Write-Host "  $manifestPath"
