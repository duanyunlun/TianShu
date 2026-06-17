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

function Get-RepoRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $root = $repoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $full = [System.IO.Path]::GetFullPath($Path)
    if ($full.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($root.Length + 1).Replace('\', '/')
    }

    return $full
}

function Assert-FileContains {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Code
    )

    $fullPath = Resolve-RepoPath $Path
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "v1.0 documentation gate failed: missing file $Path"
    }

    $content = [System.IO.File]::ReadAllText($fullPath, [System.Text.Encoding]::UTF8)
    if ($content -notmatch $Pattern) {
        throw "v1.0 documentation gate failed: $Code in $Path"
    }
}

function Get-ProductionDocumentationFiles {
    $files = @(
        'README.md',
        'docs/usage/quickstart.md',
        'docs/usage/modules.md',
        'docs/tianshu-architecture-spec.md',
        'docs/usage/troubleshooting.md',
        'docs/security/tianshu-security-model.md',
        'docs/publishing/release-notes.md'
    )

    foreach ($file in $files) {
        $path = Resolve-RepoPath $file
        if (-not (Test-Path -LiteralPath $path)) {
            throw "v1.0 documentation gate failed: missing required production doc $file"
        }

        Get-Item -LiteralPath $path
    }
}

function Test-DocumentationHygiene {
    $patterns = @(
        [pscustomobject]@{ Code = 'private_path.repo'; Pattern = 'D:\\GitRepos\\' },
        [pscustomobject]@{ Code = 'private_path.user_profile'; Pattern = 'C:\\Users\\[A-Za-z0-9_.-]+\\' },
        [pscustomobject]@{ Code = 'private_path.codex_clipboard'; Pattern = 'codex-clipboard-[0-9a-f-]+' },
        [pscustomobject]@{ Code = 'secret.openai_literal'; Pattern = 'OPENAI_API_KEY\s*=\s*["''][^<\s][^"'']*["'']' },
        [pscustomobject]@{ Code = 'secret.anthropic_literal'; Pattern = 'ANTHROPIC_API_KEY\s*=\s*["''][^<\s][^"'']*["'']' },
        [pscustomobject]@{ Code = 'secret.openai_compatible_literal'; Pattern = 'OPENAI_COMPATIBLE_API_KEY\s*=\s*["''][^<\s][^"'']*["'']' },
        [pscustomobject]@{ Code = 'secret.openai_key'; Pattern = 'sk-[A-Za-z0-9_-]{16,}' },
        [pscustomobject]@{ Code = 'secret.github_token'; Pattern = '(ghp_|github_pat_)[A-Za-z0-9_]+' }
    )

    $violations = New-Object System.Collections.Generic.List[string]
    foreach ($file in Get-ProductionDocumentationFiles) {
        $relative = Get-RepoRelativePath $file.FullName
        $lines = [System.IO.File]::ReadAllLines($file.FullName, [System.Text.Encoding]::UTF8)
        for ($index = 0; $index -lt $lines.Count; $index++) {
            foreach ($pattern in $patterns) {
                if ($lines[$index] -match $pattern.Pattern) {
                    $violations.Add("${relative}:$($index + 1):$($pattern.Code)")
                }
            }
        }
    }

    if ($violations.Count -gt 0) {
        throw "v1.0 documentation hygiene failed:`n$($violations -join [Environment]::NewLine)"
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
        throw "v1.0 documentation gate failed: $Name"
    }
}

Assert-FileContains -Path 'README.md' -Pattern 'docs/usage/quickstart\.md' -Code 'readme_missing_quickstart_link'
Assert-FileContains -Path 'README.md' -Pattern 'docs/usage/modules\.md' -Code 'readme_missing_module_guide_link'
Assert-FileContains -Path 'README.md' -Pattern 'docs/tianshu-architecture-spec\.md' -Code 'readme_missing_architecture_spec_link'
Assert-FileContains -Path 'README.md' -Pattern 'docs/usage/troubleshooting\.md' -Code 'readme_missing_troubleshooting_link'
Assert-FileContains -Path 'README.md' -Pattern 'docs/security/tianshu-security-model\.md' -Code 'readme_missing_security_model_link'
Assert-FileContains -Path 'README.md' -Pattern 'docs/publishing/release-notes\.md' -Code 'readme_missing_release_notes_link'
Assert-FileContains -Path 'docs/publishing/tianshu-release-smoke.md' -Pattern 'Test-TianShuV10DocumentationReleaseGate\.ps1' -Code 'release_smoke_docs_missing_v10_documentation_gate'
Assert-FileContains -Path '.github/workflows/ci-release.yml' -Pattern 'Test-TianShuV10DocumentationReleaseGate\.ps1' -Code 'ci_missing_v10_documentation_gate'

Assert-FileContains -Path 'docs/usage/quickstart.md' -Pattern 'tianshu init[\s\S]*tianshu doctor[\s\S]*tianshu send' -Code 'quickstart_missing_cli_flow'
Assert-FileContains -Path 'docs/usage/modules.md' -Pattern 'Provider[\s\S]*Tool[\s\S]*Memory' -Code 'module_guide_missing_three_module_families'
Assert-FileContains -Path 'docs/tianshu-architecture-spec.md' -Pattern 'Experience Plane[\s\S]*Host Gateway[\s\S]*Control Plane[\s\S]*Kernel / Core Loop[\s\S]*Execution Runtime[\s\S]*Module Plane' -Code 'architecture_spec_missing_six_plane_chain'
Assert-FileContains -Path 'docs/usage/troubleshooting.md' -Pattern 'provider_api_key_missing[\s\S]*doctor --probe[\s\S]*Sub-Agent' -Code 'troubleshooting_missing_required_failures'
Assert-FileContains -Path 'docs/security/tianshu-security-model.md' -Pattern 'secret[\s\S]*governance[\s\S]*RuntimeStep[\s\S]*Remote Module' -Code 'security_model_missing_core_boundaries'
Assert-FileContains -Path 'docs/publishing/release-notes.md' -Pattern 'v0\.9\.1[\s\S]*Unreleased[\s\S]*release notes' -Code 'release_notes_missing_required_sections'

Test-DocumentationHygiene

if (-not $SkipRegressionTests) {
    Invoke-GateTest `
        -Name 'v1.0 production documentation regression' `
        -Project 'tests/TianShu.Execution.Integration.Tests/TianShu.Execution.Integration.Tests.csproj' `
        -Filter 'FullyQualifiedName~P31_3_ProductionDocumentationGate'
}

Write-Host 'TianShu v1.0 documentation release gate passed.'
