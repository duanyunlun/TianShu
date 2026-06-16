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

function Assert-ArchiveRecord {
    param(
        [Parameter(Mandatory = $true)]$Record,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$ManifestVersion
    )

    foreach ($propertyName in @("runtimeIdentifier", "assetName", "relativePath", "sha256", "sizeBytes", "entryName")) {
        if (-not $Record.PSObject.Properties[$propertyName]) {
            throw "release-manifest archive record is missing '$propertyName'."
        }
    }

    $rid = [string]$Record.runtimeIdentifier
    $assetName = [string]$Record.assetName
    $expectedPrefix = "tianshu-cli-$ManifestVersion-$rid"
    if (-not ($assetName -eq "$expectedPrefix.zip" -or $assetName -eq "$expectedPrefix.tar.gz")) {
        throw "Archive asset name does not match version/runtime: $assetName"
    }

    $archivePath = Join-Path $Root $assetName
    if (-not (Test-Path -LiteralPath $archivePath)) {
        throw "Archive listed in manifest is missing: $archivePath"
    }

    $item = Get-Item -LiteralPath $archivePath
    if ([int64]$Record.sizeBytes -ne $item.Length) {
        throw "Archive size mismatch for $assetName. Manifest=$($Record.sizeBytes), actual=$($item.Length)."
    }

    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
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

foreach ($flag in @("publishSingleFile", "publishTrimmed", "selfContained")) {
    if ($manifest.$flag -ne $false) {
        throw "release-manifest must keep $flag=false for v0.5.x directory-style packages."
    }
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
