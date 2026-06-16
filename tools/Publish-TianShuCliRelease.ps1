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

$repoRoot = Resolve-FullPath (Join-Path $PSScriptRoot "..")
$outputRootPath = Resolve-FullPath (Join-Path $repoRoot $OutputRoot)
$stagingRoot = Join-Path $outputRootPath "staging"
$packageRoot = Join-Path $outputRootPath "packages"
$cliProjectPath = Join-Path $repoRoot "src/Presentations/TianShu.Cli/TianShu.Cli.csproj"

Assert-PathUnderRoot -Path $outputRootPath -Root $repoRoot
if (Test-Path -LiteralPath $outputRootPath) {
    Remove-Item -LiteralPath $outputRootPath -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $stagingRoot, $packageRoot | Out-Null

$archives = @()
foreach ($rid in $RuntimeIdentifiers) {
    $packageName = "tianshu-cli-$Version-$rid"
    $publishDirectory = Join-Path $stagingRoot $packageName
    New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null

    dotnet publish $cliProjectPath `
        -c $Configuration `
        -r $rid `
        --self-contained false `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -o $publishDirectory `
        --nologo `
        --verbosity:minimal

    Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $publishDirectory "README.md") -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $publishDirectory "LICENSE") -Force
    Set-Content -LiteralPath (Join-Path $publishDirectory "VERSION.txt") -Encoding UTF8 -Value $Version

    $entryPath = if ($rid.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) {
        Join-Path $publishDirectory "TianShu.Cli.exe"
    }
    else {
        Join-Path $publishDirectory "TianShu.Cli"
    }
    if (-not (Test-Path -LiteralPath $entryPath)) {
        throw "Published CLI entry is missing for ${rid}: $entryPath"
    }

    $friendlyEntryPath = if ($rid.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) {
        Join-Path $publishDirectory "tianshu.exe"
    }
    else {
        Join-Path $publishDirectory "tianshu"
    }
    Copy-Item -LiteralPath $entryPath -Destination $friendlyEntryPath -Force

    if ($rid.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) {
        $archivePath = Join-Path $packageRoot "$packageName.zip"
        Compress-Archive -LiteralPath $publishDirectory -DestinationPath $archivePath -Force
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

    $archives += $archivePath
}

$manifest = [ordered]@{
    version = $Version
    configuration = $Configuration
    runtimeIdentifiers = $RuntimeIdentifiers
    publishSingleFile = $false
    publishTrimmed = $false
    selfContained = $false
    archives = @($archives | ForEach-Object { Resolve-FullPath $_ })
}

$manifestPath = Join-Path $packageRoot "release-manifest.json"
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host "TianShu CLI release packages:"
foreach ($archive in $archives) {
    Write-Host "  $archive"
}
Write-Host "Manifest:"
Write-Host "  $manifestPath"
