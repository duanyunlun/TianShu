param(
    [string]$PackagesRoot = "artifacts/release/packages",
    [string]$RuntimeIdentifier,
    [string]$WorkRoot = "artifacts/release/smoke"
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
        throw "Refusing to operate outside smoke root: $resolvedPath"
    }
}

function Resolve-CurrentRuntimeIdentifier {
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        return "win-x64"
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
        return "osx-arm64"
    }

    return "linux-x64"
}

function Invoke-Cli {
    param(
        [Parameter(Mandatory = $true)][string]$EntryPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][int]$ExpectedExitCode
    )

    $output = & $EntryPath @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne $ExpectedExitCode) {
        throw "Command failed with exit code $exitCode, expected $ExpectedExitCode. Command: $EntryPath $($Arguments -join ' ')`n$output"
    }

    return ($output | Out-String).Trim()
}

function Clear-ProviderEnvironment {
    $names = @(
        "OPENAI_API_KEY",
        "ANTHROPIC_API_KEY",
        "OPENAI_COMPATIBLE_API_KEY",
        "GOOGLE_API_KEY",
        "GEMINI_API_KEY"
    )

    $previousValues = @{}
    foreach ($name in $names) {
        $previousValues[$name] = [System.Environment]::GetEnvironmentVariable($name)
        [System.Environment]::SetEnvironmentVariable($name, $null)
    }

    return $previousValues
}

function Restore-ProviderEnvironment {
    param([Parameter(Mandatory = $true)][hashtable]$PreviousValues)

    foreach ($entry in $PreviousValues.GetEnumerator()) {
        if ($null -eq $entry.Value) {
            [System.Environment]::SetEnvironmentVariable([string]$entry.Key, $null)
        }
        else {
            [System.Environment]::SetEnvironmentVariable([string]$entry.Key, [string]$entry.Value)
        }
    }
}

$repoRoot = Resolve-FullPath (Join-Path $PSScriptRoot "..")
$packagesRootPath = Resolve-FullPath (Join-Path $repoRoot $PackagesRoot)
$runtime = if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) { Resolve-CurrentRuntimeIdentifier } else { $RuntimeIdentifier }

$manifestPath = Join-Path $packagesRootPath "release-manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "release-manifest.json is missing: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$record = @($manifest.archives) | Where-Object { [string]$_.runtimeIdentifier -eq $runtime } | Select-Object -First 1
if ($null -eq $record) {
    throw "No release archive found for runtime identifier: $runtime"
}

$archivePath = Join-Path $packagesRootPath ([string]$record.assetName)
if (-not (Test-Path -LiteralPath $archivePath)) {
    throw "Release archive is missing: $archivePath"
}

$workRootPath = Resolve-FullPath (Join-Path $repoRoot $WorkRoot)
$runtimeWorkRoot = Join-Path $workRootPath $runtime
Assert-PathUnderRoot -Path $runtimeWorkRoot -Root $workRootPath
if (Test-Path -LiteralPath $runtimeWorkRoot) {
    Remove-Item -LiteralPath $runtimeWorkRoot -Recurse -Force
}

$extractRoot = Join-Path $runtimeWorkRoot "extract"
$homeRoot = Join-Path $runtimeWorkRoot "home"
$workspaceRoot = Join-Path $runtimeWorkRoot "workspace"
New-Item -ItemType Directory -Force -Path $extractRoot, $homeRoot, $workspaceRoot | Out-Null
Set-Content -LiteralPath (Join-Path $workspaceRoot "TianShu.sln") -Encoding UTF8 -Value ""

if ([string]$record.assetName -like "*.zip") {
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force
}
else {
    tar -xzf $archivePath -C $extractRoot
}

$packageName = [string]$record.assetName
$packageName = $packageName -replace '\.tar\.gz$', ''
$packageName = $packageName -replace '\.zip$', ''
$packageRoot = Join-Path $extractRoot $packageName
if (-not (Test-Path -LiteralPath $packageRoot)) {
    throw "Extracted package root is missing: $packageRoot"
}

foreach ($requiredFile in @("README.md", "LICENSE", "VERSION.txt")) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageRoot $requiredFile))) {
        throw "Release package is missing $requiredFile."
    }
}

if (Test-Path -LiteralPath (Join-Path $packageRoot "AGENTS.md")) {
    throw "Release package must not contain AGENTS.md."
}

$entryName = [string]$record.entryName
$entryPath = Join-Path $packageRoot $entryName
if (-not (Test-Path -LiteralPath $entryPath)) {
    throw "Release package entry is missing: $entryPath"
}

if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
    chmod +x $entryPath
}

$previousProviderEnvironment = Clear-ProviderEnvironment
try {
    Invoke-Cli -EntryPath $entryPath -Arguments @("--help") -ExpectedExitCode 0 | Out-Null

    $configPath = Join-Path $homeRoot "tianshu.toml"
    $initJson = Invoke-Cli -EntryPath $entryPath -Arguments @("init", "--provider", "openai", "--config-file", $configPath, "--cwd", $workspaceRoot, "--json") -ExpectedExitCode 0
    $init = $initJson | ConvertFrom-Json
    if ($init.success -ne $true) {
        throw "init --json did not report success."
    }

    foreach ($requiredPath in @(
            $configPath,
            (Join-Path $homeRoot "modules/model/provider-instances/default.toml"),
            (Join-Path $homeRoot "modules/model/route-sets/default.toml"),
            (Join-Path $homeRoot "modules/model/protocol-rules/default.toml")
        )) {
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            throw "init did not generate required configuration path: $requiredPath"
        }
    }

    $doctorJson = Invoke-Cli -EntryPath $entryPath -Arguments @("doctor", "--config-file", $configPath, "--cwd", $workspaceRoot, "--json") -ExpectedExitCode 1
    $doctor = $doctorJson | ConvertFrom-Json
    if ($doctor.ready -ne $false) {
        throw "doctor should fail closed when provider API key is missing."
    }

    $issueCodes = @($doctor.issues | ForEach-Object { [string]$_.code })
    if ($issueCodes -notcontains "provider_api_key_missing") {
        throw "doctor did not report provider_api_key_missing."
    }

    if ($issueCodes -contains "packaged_assembly_missing") {
        throw "doctor reported packaged_assembly_missing for release package."
    }
}
finally {
    Restore-ProviderEnvironment -PreviousValues $previousProviderEnvironment
}

Write-Host "release package smoke passed: $($record.assetName)"
$global:LASTEXITCODE = 0
