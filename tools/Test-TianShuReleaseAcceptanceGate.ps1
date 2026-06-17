param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore,
    [switch]$SkipRegressionTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    Join-Path $repoRoot $Path
}

function Read-Utf8Text {
    param([Parameter(Mandatory = $true)][string]$Path)
    [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function Assert-FileContains {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Code
    )

    $fullPath = Resolve-RepoPath $Path
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "release acceptance gate failed: missing file $Path"
    }

    $content = Read-Utf8Text -Path $fullPath
    if ($content -notmatch $Pattern) {
        throw "release acceptance gate failed: $Code in $Path"
    }
}

function Invoke-GateTest {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$Filter
    )

    Write-Host "==> $Name"
    $projectPath = Resolve-RepoPath $Project
    $arguments = @(
        'test',
        $projectPath,
        '--configuration',
        $Configuration,
        '--nologo',
        '--logger',
        'console;verbosity=minimal',
        '--filter',
        $Filter,
        '-m:1'
    )

    if ($NoRestore) {
        $arguments += '--no-restore'
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "release acceptance gate failed: $Name"
    }
}

Assert-FileContains -Path '.github/workflows/ci-release.yml' -Pattern 'tags:[\s\S]*"v\*"[\s\S]*Package CLI[\s\S]*Validate release manifest[\s\S]*Release package smoke[\s\S]*Publish GitHub Release[\s\S]*softprops/action-gh-release@v2' -Code 'ci_missing_tag_package_smoke_release_chain'
Assert-FileContains -Path '.github/workflows/ci-release.yml' -Pattern 'github-release:[\s\S]*needs:[\s\S]*package-cli[\s\S]*release-smoke[\s\S]*if: startsWith\(github\.ref, ''refs/tags/v''\)' -Code 'ci_release_job_missing_tag_gate_or_needs'
Assert-FileContains -Path 'tools/Publish-TianShuCliRelease.ps1' -Pattern 'RuntimeIdentifiers = @\("win-x64", "linux-x64", "osx-arm64"\)[\s\S]*tianshu-\$Version-\$rid[\s\S]*README\.md[\s\S]*LICENSE[\s\S]*VERSION\.txt[\s\S]*release-manifest\.json' -Code 'publish_script_missing_asset_manifest_contract'
Assert-FileContains -Path 'tools/Publish-TianShuCliRelease.ps1' -Pattern 'portable-tianshu-home' -Code 'publish_script_missing_portable_layout'
Assert-FileContains -Path 'tools/Publish-TianShuCliRelease.ps1' -Pattern 'sha256|SHA256' -Code 'publish_script_missing_sha256'
Assert-FileContains -Path 'tools/Test-TianShuReleaseManifest.ps1' -Pattern 'schemaVersion -ne 1' -Code 'manifest_script_missing_schema_version_check'
Assert-FileContains -Path 'tools/Test-TianShuReleaseManifest.ps1' -Pattern 'assetName[\s\S]*sha256[\s\S]*sizeBytes|assetName[\s\S]*sizeBytes[\s\S]*sha256' -Code 'manifest_script_missing_checksum_fields'
Assert-FileContains -Path 'tools/Test-TianShuReleaseManifest.ps1' -Pattern 'GitHubRepository[\s\S]*ReleaseTag[\s\S]*release-manifest\.json' -Code 'manifest_script_missing_github_asset_validation'
Assert-FileContains -Path 'tools/Test-TianShuCliReleasePackage.ps1' -Pattern 'README\.md[\s\S]*LICENSE[\s\S]*VERSION\.txt[\s\S]*tianshu\.toml[\s\S]*AGENTS\.md' -Code 'package_smoke_missing_required_files'
Assert-FileContains -Path 'tools/Test-TianShuCliReleasePackage.ps1' -Pattern '--help[\s\S]*doctor[\s\S]*init[\s\S]*send' -Code 'package_smoke_missing_command_chain'
Assert-FileContains -Path 'tools/Test-TianShuCliReleasePackage.ps1' -Pattern 'portableMode[\s\S]*provider_api_key_missing[\s\S]*packaged_assembly_missing' -Code 'package_smoke_missing_portable_diagnostics'
Assert-FileContains -Path 'docs/publishing/tianshu-release-acceptance.md' -Pattern '## 1\. Tag' -Code 'release_acceptance_doc_missing_tag_section'
Assert-FileContains -Path 'docs/publishing/tianshu-release-acceptance.md' -Pattern 'tianshu-<version>-win-x64\.zip' -Code 'release_acceptance_doc_missing_windows_asset_name'
Assert-FileContains -Path 'docs/publishing/tianshu-release-acceptance.md' -Pattern 'release-manifest\.json' -Code 'release_acceptance_doc_missing_manifest'
Assert-FileContains -Path 'docs/publishing/tianshu-release-acceptance.md' -Pattern 'SHA-256' -Code 'release_acceptance_doc_missing_checksum'
Assert-FileContains -Path 'docs/publishing/tianshu-release-acceptance.md' -Pattern 'Windows smoke' -Code 'release_acceptance_doc_missing_windows_smoke'
Assert-FileContains -Path 'docs/publishing/tianshu-release-acceptance.md' -Pattern '## 5\.' -Code 'release_acceptance_doc_missing_upgrade_section'
Assert-FileContains -Path 'docs/publishing/tianshu-release-acceptance.md' -Pattern '## 6\.' -Code 'release_acceptance_doc_missing_uninstall_section'
Assert-FileContains -Path 'docs/publishing/release-notes.md' -Pattern 'tianshu-release-acceptance\.md|release acceptance' -Code 'release_notes_missing_release_acceptance_link'
Assert-FileContains -Path 'docs/publishing/tianshu-release-smoke.md' -Pattern 'Test-TianShuReleaseAcceptanceGate\.ps1' -Code 'release_smoke_missing_release_acceptance_gate'
Assert-FileContains -Path '.github/workflows/ci-release.yml' -Pattern 'Test-TianShuReleaseAcceptanceGate\.ps1' -Code 'ci_missing_release_acceptance_gate'

if (-not $SkipRegressionTests) {
    Invoke-GateTest `
        -Name 'P31.6 release acceptance regression' `
        -Project 'tests/TianShu.Execution.Integration.Tests/TianShu.Execution.Integration.Tests.csproj' `
        -Filter 'FullyQualifiedName~P31_6_ReleaseAcceptanceGate'
}

Write-Host 'TianShu release acceptance gate passed.'
