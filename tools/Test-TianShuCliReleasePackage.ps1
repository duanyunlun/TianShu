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
        [Parameter(Mandatory = $true)][int]$ExpectedExitCode,
        [string]$WorkingDirectory
    )

    if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $output = & $EntryPath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    else {
        Push-Location $WorkingDirectory
        try {
            $output = & $EntryPath @Arguments 2>&1
            $exitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
    }

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
        "GEMINI_API_KEY",
        "TIANSHU_HOME"
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
$workspaceRoot = Join-Path $runtimeWorkRoot "workspace"
New-Item -ItemType Directory -Force -Path $extractRoot, $workspaceRoot | Out-Null
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

foreach ($requiredFile in @("README.md", "LICENSE", "VERSION.txt", "tianshu.toml")) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageRoot $requiredFile))) {
        throw "Release package is missing $requiredFile."
    }
}

$versionLines = @(Get-Content -LiteralPath (Join-Path $packageRoot "VERSION.txt") | ForEach-Object { [string]$_ })
if ($versionLines -notcontains "runtimeIdentifier=$runtime") {
    throw "VERSION.txt does not record the package runtime identifier $runtime."
}

foreach ($requiredPath in @(
        (Join-Path $packageRoot "modules/model/provider-instances/default.toml"),
        (Join-Path $packageRoot "modules/model/route-sets/default.toml"),
        (Join-Path $packageRoot "modules/model/protocol-rules/default.toml")
    )) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Release package is missing default module template: $requiredPath"
    }
}

$providerTemplate = Get-Content -LiteralPath (Join-Path $packageRoot "modules/model/provider-instances/default.toml") -Raw
if ($providerTemplate -match "192\.168\.|OPENAI_API_KEY_ST") {
    throw "Release provider template must not contain local private gateway or stale secret environment names."
}

$routeTemplate = Get-Content -LiteralPath (Join-Path $packageRoot "modules/model/route-sets/default.toml") -Raw
if ($routeTemplate -notmatch 'protocol\s*=\s*"openai_responses"') {
    throw "Release route template must use canonical openai_responses protocol."
}

if ($routeTemplate -match 'protocol\s*=\s*"responses"') {
    throw "Release route template must not use the responses alias in public defaults."
}

if (Test-Path -LiteralPath (Join-Path $packageRoot "AGENTS.md")) {
    throw "Release package must not contain AGENTS.md."
}

$entryPath = Join-Path $packageRoot ([string]$record.entryPath)
if (-not (Test-Path -LiteralPath $entryPath)) {
    throw "Release package entry is missing: $entryPath"
}

$appHostPath = Join-Path $packageRoot ([string]$record.appHostPath)
if (-not (Test-Path -LiteralPath $appHostPath)) {
    throw "Release package AppHost entry is missing: $appHostPath"
}

if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
    chmod +x $entryPath
}

$previousProviderEnvironment = Clear-ProviderEnvironment
try {
    Invoke-Cli -EntryPath $entryPath -Arguments @("--help") -ExpectedExitCode 0 -WorkingDirectory $workspaceRoot | Out-Null

    $configPath = Join-Path $packageRoot "tianshu.toml"
    $doctorJson = Invoke-Cli -EntryPath $entryPath -Arguments @("doctor", "--cwd", $workspaceRoot, "--json") -ExpectedExitCode 1 -WorkingDirectory $workspaceRoot
    $doctor = $doctorJson | ConvertFrom-Json
    if ($doctor.ready -ne $false) {
        throw "doctor should fail closed when provider API key is missing."
    }

    if ($doctor.portableMode -ne $true) {
        throw "doctor did not report portableMode=true."
    }

    if ([string]$doctor.configPath -ne (Resolve-FullPath $configPath)) {
        throw "doctor did not resolve package-root tianshu.toml. Actual=$($doctor.configPath)"
    }

    if ($doctor.runtimeWritable -ne $true -or $doctor.appHostExists -ne $true) {
        throw "doctor did not report writable runtime root and packaged AppHost."
    }

    if ([string]$doctor.packageRuntimeIdentifier -ne $runtime) {
        throw "doctor did not report the package runtime identifier. Actual=$($doctor.packageRuntimeIdentifier)"
    }

    if ([string]$doctor.currentRuntimeIdentifier -ne $runtime) {
        throw "doctor current runtime identifier did not match smoke runtime. Actual=$($doctor.currentRuntimeIdentifier)"
    }

    if ($doctor.runtimeIdentifierMatches -ne $true) {
        throw "doctor did not report runtimeIdentifierMatches=true for the smoke package."
    }

    $issueCodes = @($doctor.issues | ForEach-Object { [string]$_.code })
    if ($issueCodes -notcontains "provider_api_key_missing") {
        throw "doctor did not report provider_api_key_missing."
    }

    if ($issueCodes -contains "packaged_assembly_missing" -or $issueCodes -contains "apphost_missing") {
        throw "doctor reported missing packaged runtime assets for release package."
    }

    if ($null -eq $doctor.modules) {
        throw "doctor did not include module diagnostics projection."
    }

    if ([int]$doctor.modules.discoveredCount -lt 1 -or [int]$doctor.modules.registeredCount -lt 1) {
        throw "doctor module diagnostics should report discovered and registered modules."
    }

    $initJson = Invoke-Cli -EntryPath $entryPath -Arguments @("init", "--provider", "openai", "--force", "--cwd", $workspaceRoot, "--json") -ExpectedExitCode 0 -WorkingDirectory $workspaceRoot
    $init = $initJson | ConvertFrom-Json
    if ($init.success -ne $true) {
        throw "init --json did not report success."
    }

    if ([string]$init.configPath -ne (Resolve-FullPath $configPath)) {
        throw "init did not write package-root tianshu.toml. Actual=$($init.configPath)"
    }

    foreach ($requiredPath in @(
            $configPath,
            (Join-Path $packageRoot "modules/model/provider-instances/default.toml"),
            (Join-Path $packageRoot "modules/model/route-sets/default.toml"),
            (Join-Path $packageRoot "modules/model/protocol-rules/default.toml")
        )) {
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            throw "init did not generate required configuration path: $requiredPath"
        }
    }

    $sendJson = Invoke-Cli -EntryPath $entryPath -Arguments @("send", "--message", "ping", "--kernel-runtime-loop", "--json", "--cwd", $workspaceRoot) -ExpectedExitCode 7 -WorkingDirectory $workspaceRoot
    $send = $sendJson | ConvertFrom-Json
    if ($send.artifactsRootExplicit -ne $false) {
        throw "send default artifacts root should not be marked explicit."
    }

    if ([string]$send.artifactsDirectory -notlike "$(Join-Path $packageRoot 'runtime')*") {
        throw "send artifacts should be written under package runtime root. Actual=$($send.artifactsDirectory)"
    }

    if (Test-Path -LiteralPath (Join-Path $workspaceRoot ".tianshu")) {
        throw "send must not create .tianshu under smoke workspace."
    }

    if (Test-Path -LiteralPath (Join-Path $workspaceRoot ".tianshu-cli")) {
        throw "send must not create .tianshu-cli under smoke workspace."
    }
}
finally {
    Restore-ProviderEnvironment -PreviousValues $previousProviderEnvironment
}

Write-Host "release package smoke passed: $($record.assetName)"
$global:LASTEXITCODE = 0
