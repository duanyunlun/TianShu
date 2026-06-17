param(
    [string]$PackagesRoot = "artifacts/release/packages",
    [string]$Version,
    [string[]]$RuntimeIdentifiers = @("win-x64", "linux-x64", "osx-arm64"),
    [string]$GitHubRepository,
    [string]$ReleaseTag
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $executionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Get-Sha256Hash {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (Get-Command Get-FileHash -ErrorAction SilentlyContinue) {
        return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    }

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            $bytes = $sha256.ComputeHash($stream)
            return ([System.BitConverter]::ToString($bytes) -replace "-", "").ToLowerInvariant()
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-ArchiveRecord {
    param(
        [Parameter(Mandatory = $true)]$Record,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$ManifestVersion
    )

    foreach ($propertyName in @("runtimeIdentifier", "assetName", "relativePath", "sha256", "sizeBytes", "layout", "entryPath", "configPath", "modulesPath", "appHostPath", "selfContained")) {
        if (-not $Record.PSObject.Properties[$propertyName]) {
            throw "release-manifest archive record is missing '$propertyName'."
        }
    }

    $rid = [string]$Record.runtimeIdentifier
    $assetName = [string]$Record.assetName
    $expectedPrefix = "tianshu-$ManifestVersion-$rid"
    if (-not ($assetName -eq "$expectedPrefix.zip" -or $assetName -eq "$expectedPrefix.tar.gz")) {
        throw "Archive asset name does not match version/runtime: $assetName"
    }

    if ([string]$Record.layout -ne "portable-tianshu-home") {
        throw "Archive layout must be portable-tianshu-home for $assetName."
    }

    if ([string]$Record.configPath -ne "tianshu.toml" -or [string]$Record.modulesPath -ne "modules") {
        throw "Archive config/modules paths are invalid for $assetName."
    }

    if (-not ([string]$Record.entryPath).StartsWith("bin/", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Archive entryPath must point under bin/: $($Record.entryPath)"
    }

    if (-not ([string]$Record.appHostPath).StartsWith("runtime/apphost/", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Archive appHostPath must point under runtime/apphost/: $($Record.appHostPath)"
    }

    if ($Record.selfContained -ne $true) {
        throw "Archive selfContained must be true for portable package: $assetName"
    }

    $archivePath = Join-Path $Root $assetName
    if (-not (Test-Path -LiteralPath $archivePath)) {
        throw "Archive listed in manifest is missing: $archivePath"
    }

    $item = Get-Item -LiteralPath $archivePath
    if ([int64]$Record.sizeBytes -ne $item.Length) {
        throw "Archive size mismatch for $assetName. Manifest=$($Record.sizeBytes), actual=$($item.Length)."
    }

    $hash = Get-Sha256Hash -Path $archivePath
    if ($hash -ne [string]$Record.sha256) {
        throw "Archive sha256 mismatch for $assetName. Manifest=$($Record.sha256), actual=$hash."
    }
}

$packagesRootPath = Resolve-FullPath $PackagesRoot
$manifestPath = Join-Path $packagesRootPath "release-manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "release-manifest.json is missing: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
    throw "Unsupported release-manifest schemaVersion: $($manifest.schemaVersion)"
}

if (-not [string]::IsNullOrWhiteSpace($Version) -and [string]$manifest.version -ne $Version) {
    throw "release-manifest version mismatch. Expected=$Version, actual=$($manifest.version)."
}

$manifestVersion = [string]$manifest.version
if ([string]::IsNullOrWhiteSpace($manifestVersion)) {
    throw "release-manifest version must not be empty."
}

foreach ($flag in @("publishSingleFile", "publishTrimmed")) {
    if ($manifest.$flag -ne $false) {
        throw "release-manifest must keep $flag=false for portable directory-style packages."
    }
}

if ([string]$manifest.layout -ne "portable-tianshu-home") {
    throw "release-manifest layout must be portable-tianshu-home."
}

if ($manifest.selfContained -ne $true) {
    throw "release-manifest selfContained must be true for portable packages."
}

$archiveRecords = @($manifest.archives)
if ($archiveRecords.Count -eq 0) {
    throw "release-manifest must contain at least one archive."
}

$actualRids = @($archiveRecords | ForEach-Object { [string]$_.runtimeIdentifier })
foreach ($rid in $RuntimeIdentifiers) {
    if ($actualRids -notcontains $rid) {
        throw "release-manifest is missing runtime identifier: $rid"
    }
}

foreach ($record in $archiveRecords) {
    Assert-ArchiveRecord -Record $record -Root $packagesRootPath -ManifestVersion $manifestVersion
}

if (-not [string]::IsNullOrWhiteSpace($GitHubRepository) -or -not [string]::IsNullOrWhiteSpace($ReleaseTag)) {
    if ([string]::IsNullOrWhiteSpace($GitHubRepository) -or [string]::IsNullOrWhiteSpace($ReleaseTag)) {
        throw "GitHubRepository and ReleaseTag must be provided together."
    }

    $releaseJson = & gh release view $ReleaseTag --repo $GitHubRepository --json assets 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to read GitHub Release ${GitHubRepository}@${ReleaseTag}: $releaseJson"
    }

    $release = $releaseJson | ConvertFrom-Json
    $assetsByName = @{}
    foreach ($asset in @($release.assets)) {
        $assetsByName[[string]$asset.name] = $asset
    }

    if (-not $assetsByName.ContainsKey("release-manifest.json")) {
        throw "GitHub Release is missing release-manifest.json."
    }

    foreach ($record in $archiveRecords) {
        $assetName = [string]$record.assetName
        if (-not $assetsByName.ContainsKey($assetName)) {
            throw "GitHub Release is missing archive asset: $assetName"
        }

        $asset = $assetsByName[$assetName]
        if ([int64]$asset.size -ne [int64]$record.sizeBytes) {
            throw "GitHub Release asset size mismatch for $assetName. Manifest=$($record.sizeBytes), release=$($asset.size)."
        }

        $expectedDigest = "sha256:$($record.sha256)"
        if ($asset.PSObject.Properties["digest"] -and -not [string]::IsNullOrWhiteSpace([string]$asset.digest) -and [string]$asset.digest -ne $expectedDigest) {
            throw "GitHub Release asset digest mismatch for $assetName. Manifest=$expectedDigest, release=$($asset.digest)."
        }
    }
}

Write-Host "release-manifest validation passed: $manifestPath"
