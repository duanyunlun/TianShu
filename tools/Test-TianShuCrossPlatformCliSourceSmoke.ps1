param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$WorkRoot = 'artifacts/cross-platform-source-smoke'
)

$ErrorActionPreference = 'Stop'

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
        return 'win-x64'
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
        return 'osx-arm64'
    }

    return 'linux-x64'
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][int]$ExpectedExitCode
    )

    $output = & dotnet @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne $ExpectedExitCode) {
        throw "dotnet command failed with exit code $exitCode, expected $ExpectedExitCode. Command: dotnet $($Arguments -join ' ')`n$output"
    }

    return ($output | Out-String).Trim()
}

function Clear-ProviderEnvironment {
    $names = @(
        'OPENAI_API_KEY',
        'ANTHROPIC_API_KEY',
        'OPENAI_COMPATIBLE_API_KEY',
        'GOOGLE_API_KEY',
        'GEMINI_API_KEY'
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

$repoRoot = Resolve-FullPath (Join-Path $PSScriptRoot '..')
$cliProjectPath = Join-Path $repoRoot 'src/Presentations/TianShu.Cli/TianShu.Cli.csproj'
$workRootPath = Resolve-FullPath (Join-Path $repoRoot $WorkRoot)
$runtime = Resolve-CurrentRuntimeIdentifier
$runtimeWorkRoot = Join-Path $workRootPath $runtime

Assert-PathUnderRoot -Path $runtimeWorkRoot -Root $workRootPath
if (Test-Path -LiteralPath $runtimeWorkRoot) {
    Remove-Item -LiteralPath $runtimeWorkRoot -Recurse -Force
}

$homeRoot = Join-Path $runtimeWorkRoot 'home'
$workspaceRoot = Join-Path $runtimeWorkRoot 'workspace'
New-Item -ItemType Directory -Force -Path $homeRoot, $workspaceRoot | Out-Null
Set-Content -LiteralPath (Join-Path $workspaceRoot 'TianShu.sln') -Encoding UTF8 -Value ''

Invoke-DotNet -Arguments @('restore', $cliProjectPath) -ExpectedExitCode 0 | Out-Null
Invoke-DotNet -Arguments @('build', $cliProjectPath, '--configuration', $Configuration, '--no-restore', '--nologo', '--verbosity:minimal') -ExpectedExitCode 0 | Out-Null

$previousProviderEnvironment = Clear-ProviderEnvironment
try {
    $configPath = Join-Path $homeRoot 'tianshu.toml'
    $commonRunArguments = @(
        'run',
        '--project',
        $cliProjectPath,
        '--configuration',
        $Configuration,
        '--no-build',
        '--'
    )

    $initJson = Invoke-DotNet -Arguments ($commonRunArguments + @('init', '--provider', 'openai', '--config-file', $configPath, '--cwd', $workspaceRoot, '--json')) -ExpectedExitCode 0
    $init = $initJson | ConvertFrom-Json
    if ($init.success -ne $true) {
        throw 'cross-platform source smoke failed: init --json did not report success.'
    }

    foreach ($requiredPath in @(
            $configPath,
            (Join-Path $homeRoot 'modules/model/provider-instances/default.toml'),
            (Join-Path $homeRoot 'modules/model/route-sets/default.toml'),
            (Join-Path $homeRoot 'modules/model/protocol-rules/default.toml')
        )) {
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            throw "cross-platform source smoke failed: init did not generate required path $requiredPath"
        }
    }

    $doctorJson = Invoke-DotNet -Arguments ($commonRunArguments + @('doctor', '--config-file', $configPath, '--cwd', $workspaceRoot, '--json')) -ExpectedExitCode 1
    $doctor = $doctorJson | ConvertFrom-Json
    if ($doctor.ready -ne $false) {
        throw 'cross-platform source smoke failed: doctor should fail closed without provider credentials.'
    }

    $issueCodes = @($doctor.issues | ForEach-Object { [string]$_.code })
    if ($issueCodes -notcontains 'provider_api_key_missing') {
        throw 'cross-platform source smoke failed: doctor did not report provider_api_key_missing.'
    }

    if ($null -eq $doctor.modules -or [int]$doctor.modules.discoveredCount -lt 1 -or [int]$doctor.modules.registeredCount -lt 1) {
        throw 'cross-platform source smoke failed: doctor did not report module discovery/loading projection.'
    }
}
finally {
    Restore-ProviderEnvironment -PreviousValues $previousProviderEnvironment
}

Write-Host "TianShu cross-platform CLI source smoke passed: $runtime"
