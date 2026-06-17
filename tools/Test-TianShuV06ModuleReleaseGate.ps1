param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTemplateTests,
    [switch]$SkipSampleTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    Join-Path $repoRoot $Path
}

function Get-PublicDocumentationFiles {
    $explicitFiles = @(
        'README.md',
        'docs/tianshu-architecture-spec.md',
        'docs/publishing/tianshu-release-smoke.md',
        'docs/usage/modules.md',
        'docs/architecture/tianshu-module-plane-design.md'
    )

    foreach ($file in $explicitFiles) {
        $path = Resolve-RepoPath $file
        if (Test-Path -LiteralPath $path) {
            Get-Item -LiteralPath $path
        }
    }
}

function Get-RepoRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $root = $repoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $full = [System.IO.Path]::GetFullPath($Path)
    if ($full.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($root.Length + 1)
    }

    return $full
}

function Test-PublicDocumentation {
    $patterns = @(
        [pscustomobject]@{ Code = 'private_path.repo'; Pattern = 'D:\\GitRepos\\' },
        [pscustomobject]@{ Code = 'private_path.user_profile'; Pattern = 'C:\\Users\\SEMI\\' },
        [pscustomobject]@{ Code = 'private_path.codex_clipboard'; Pattern = 'codex-clipboard-[0-9a-f-]+' },
        [pscustomobject]@{ Code = 'secret.openai_literal'; Pattern = 'OPENAI_API_KEY\s*=\s*["''][^<\s][^"'']*["'']' },
        [pscustomobject]@{ Code = 'secret.anthropic_literal'; Pattern = 'ANTHROPIC_API_KEY\s*=\s*["''][^<\s][^"'']*["'']' },
        [pscustomobject]@{ Code = 'secret.openai_key'; Pattern = 'sk-[A-Za-z0-9_-]{16,}' },
        [pscustomobject]@{ Code = 'secret.github_token'; Pattern = '(ghp_|github_pat_)[A-Za-z0-9_]+' }
    )

    $violations = New-Object System.Collections.Generic.List[string]
    foreach ($file in Get-PublicDocumentationFiles) {
        $relative = Get-RepoRelativePath $file.FullName
        $lines = Get-Content -LiteralPath $file.FullName
        for ($index = 0; $index -lt $lines.Count; $index++) {
            foreach ($pattern in $patterns) {
                if ($lines[$index] -match $pattern.Pattern) {
                    $violations.Add("${relative}:$($index + 1):$($pattern.Code)")
                }
            }
        }
    }

    if ($violations.Count -gt 0) {
        throw "Public documentation gate failed:`n$($violations -join [Environment]::NewLine)"
    }
}

Test-PublicDocumentation

if (-not $SkipTemplateTests) {
    & (Resolve-RepoPath 'tools/Build-TianShuModuleTemplates.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw 'Module template validation failed.'
    }
}

if (-not $SkipSampleTests) {
    & (Resolve-RepoPath 'tools/Build-TianShuModuleSamples.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw 'Module sample validation failed.'
    }
}

Write-Host "TianShu v0.6 module release gate passed."
