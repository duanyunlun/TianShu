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

    return $full.Replace('\', '/')
}

function Read-Utf8Text {
    param([Parameter(Mandatory = $true)][string]$Path)
    [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function Read-Utf8Lines {
    param([Parameter(Mandatory = $true)][string]$Path)
    [System.IO.File]::ReadAllLines($Path, [System.Text.Encoding]::UTF8)
}

function Get-GitTrackedFiles {
    $files = & git -C $repoRoot ls-files
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to list git tracked files.'
    }

    $files | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

function Get-PublicReleaseTextFiles {
    $files = New-Object System.Collections.Generic.List[string]

    foreach ($path in @('README.md', 'CONTRIBUTING.md')) {
        $fullPath = Resolve-RepoPath $path
        if (Test-Path -LiteralPath $fullPath) {
            $files.Add($fullPath)
        }
    }

    foreach ($root in @('docs', '.github/workflows', 'tools')) {
        $fullRoot = Resolve-RepoPath $root
        if (-not (Test-Path -LiteralPath $fullRoot)) {
            continue
        }

        foreach ($extension in @('*.md', '*.yml', '*.yaml', '*.ps1')) {
            foreach ($file in Get-ChildItem -LiteralPath $fullRoot -Recurse -File -Filter $extension) {
                $relative = Get-RepoRelativePath $file.FullName
                if ($relative.StartsWith('docs/reference/', [System.StringComparison]::OrdinalIgnoreCase)) {
                    continue
                }

                $files.Add($file.FullName)
            }
        }
    }

    $files | Sort-Object -Unique
}

function Test-SecretAndPrivatePathScan {
    $patterns = @(
        [pscustomobject]@{ Code = 'private_path.repo'; Pattern = 'D:\\GitRepos\\Personal\\' },
        [pscustomobject]@{ Code = 'private_path.user_profile'; Pattern = 'C:\\Users\\SEMI\\' },
        [pscustomobject]@{ Code = 'private_path.codex_clipboard'; Pattern = 'codex-clipboard-[0-9a-f-]+' },
        [pscustomobject]@{ Code = 'secret.openai_literal'; Pattern = 'OPENAI_API_KEY\s*=\s*["''][^<\s][^"'']*["'']' },
        [pscustomobject]@{ Code = 'secret.anthropic_literal'; Pattern = 'ANTHROPIC_API_KEY\s*=\s*["''][^<\s][^"'']*["'']' },
        [pscustomobject]@{ Code = 'secret.openai_compatible_literal'; Pattern = 'OPENAI_COMPATIBLE_API_KEY\s*=\s*["''][^<\s][^"'']*["'']' },
        [pscustomobject]@{ Code = 'secret.google_literal'; Pattern = '(GOOGLE_API_KEY|GEMINI_API_KEY)\s*=\s*["''][^<\s][^"'']*["'']' },
        [pscustomobject]@{ Code = 'secret.openai_key'; Pattern = '(?<![A-Za-z0-9])sk-[A-Za-z0-9_-]{16,}' },
        [pscustomobject]@{ Code = 'secret.github_token'; Pattern = '(ghp_|github_pat_)[A-Za-z0-9_]+' },
        [pscustomobject]@{ Code = 'secret.jwt_like'; Pattern = 'eyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}' }
    )

    $violations = New-Object System.Collections.Generic.List[string]
    foreach ($file in Get-PublicReleaseTextFiles) {
        $relative = Get-RepoRelativePath $file
        $lines = Read-Utf8Lines -Path $file
        for ($index = 0; $index -lt $lines.Count; $index++) {
            $line = $lines[$index]
            if ($line -match "Code = '(private_path|secret)\." -or $line -match 'Pattern = ') {
                continue
            }

            foreach ($pattern in $patterns) {
                if ($line -match $pattern.Pattern) {
                    $violations.Add("${relative}:$($index + 1):$($pattern.Code)")
                }
            }
        }
    }

    if ($violations.Count -gt 0) {
        throw "Public release secret/private path scan failed:`n$($violations -join [Environment]::NewLine)"
    }
}

function Test-TrackedRuntimeAndTestArtifacts {
    $forbiddenPathPatterns = @(
        [pscustomobject]@{ Code = 'runtime_root'; Pattern = '^(runtime|\.tianshu|data/sessions)(/|$)' },
        [pscustomobject]@{ Code = 'test_artifact_root'; Pattern = '^(Test|test-results|TestResults|artifacts)(/|$)' },
        [pscustomobject]@{ Code = 'audit_evidence'; Pattern = '^docs/audit/evidence(/|$)' },
        [pscustomobject]@{ Code = 'agent_local_state'; Pattern = '^(\.codex|\.claude|\.serena)(/|$)' },
        [pscustomobject]@{ Code = 'runtime_state_file'; Pattern = '(^|/)(runtime-state|runtime_state|thread-state|thread_state|session-state|session_state)\.(json|jsonl|ndjson)$' },
        [pscustomobject]@{ Code = 'test_result_file'; Pattern = '(^|/)(test-results?|TestResults?)(/|$)|\.(trx|coverage|coveragexml|binlog)$' },
        [pscustomobject]@{ Code = 'debug_request_body'; Pattern = '(^|/)request-body.*\.json$' }
    )

    $violations = New-Object System.Collections.Generic.List[string]
    foreach ($path in Get-GitTrackedFiles) {
        $normalized = $path.Replace('\', '/')
        foreach ($pattern in $forbiddenPathPatterns) {
            if ($normalized -match $pattern.Pattern) {
                $violations.Add("${normalized}:$($pattern.Code)")
            }
        }
    }

    if ($violations.Count -gt 0) {
        throw "Tracked runtime state or test artifact scan failed:`n$($violations -join [Environment]::NewLine)"
    }
}

function Test-IgnoreRules {
    $gitIgnorePath = Resolve-RepoPath '.gitignore'
    $source = Read-Utf8Text -Path $gitIgnorePath
    $requiredTerms = @(
        '[Tt]est/',
        '/artifacts/',
        '.codex/',
        '.claude/',
        '.serena/',
        'request-body*.json'
    )

    foreach ($term in $requiredTerms) {
        if ($source.IndexOf($term, [System.StringComparison]::Ordinal) -lt 0) {
            throw ".gitignore is missing release safety ignore rule: $term"
        }
    }
}

function Get-MarkdownLinkTargets {
    param([Parameter(Mandatory = $true)][string]$Path)

    $source = Read-Utf8Text -Path $Path
    $matches = [System.Text.RegularExpressions.Regex]::Matches($source, '(?<!\!)\[[^\]]+\]\((?<target>[^)]+)\)')
    foreach ($match in $matches) {
        [pscustomobject]@{
            Target = [string]$match.Groups['target'].Value
            Index = $match.Index
        }
    }
}

function Resolve-MarkdownTargetPath {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$Target
    )

    $targetValue = $Target.Trim()
    if ([string]::IsNullOrWhiteSpace($targetValue)) {
        return $null
    }

    if ($targetValue.StartsWith('<') -and $targetValue.Contains('>')) {
        $targetValue = $targetValue.Substring(1, $targetValue.IndexOf('>') - 1)
    }
    else {
        $targetValue = ($targetValue -split '\s+')[0]
    }

    if ($targetValue.StartsWith('#')) {
        return $null
    }

    if ($targetValue -match '^(https?|mailto|tel):' -or $targetValue.StartsWith('data:')) {
        return $null
    }

    $withoutFragment = ($targetValue -split '#', 2)[0]
    $withoutQuery = ($withoutFragment -split '\?', 2)[0]
    if ([string]::IsNullOrWhiteSpace($withoutQuery)) {
        return $null
    }

    $decoded = [System.Uri]::UnescapeDataString($withoutQuery)
    $baseDirectory = Split-Path -Parent $SourcePath
    if ($decoded.StartsWith('/')) {
        return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $decoded.TrimStart('/')))
    }

    [System.IO.Path]::GetFullPath((Join-Path $baseDirectory $decoded))
}

function Test-MarkdownRelativeLinks {
    $violations = New-Object System.Collections.Generic.List[string]
    $markdownFiles = @(
        Resolve-RepoPath 'README.md'
        Get-ChildItem -LiteralPath (Resolve-RepoPath 'docs') -Recurse -File -Filter '*.md' | ForEach-Object { $_.FullName }
    )

    foreach ($file in $markdownFiles) {
        $relative = Get-RepoRelativePath $file
        foreach ($link in Get-MarkdownLinkTargets -Path $file) {
            $resolved = Resolve-MarkdownTargetPath -SourcePath $file -Target $link.Target
            if ($null -eq $resolved) {
                continue
            }

            $root = $repoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
            if (-not $resolved.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
                $violations.Add("${relative}: external relative link escapes repo: $($link.Target)")
                continue
            }

            if (-not (Test-Path -LiteralPath $resolved)) {
                $violations.Add("${relative}: missing markdown link target: $($link.Target)")
            }
        }
    }

    if ($violations.Count -gt 0) {
        throw "README/docs markdown dead-link scan failed:`n$($violations -join [Environment]::NewLine)"
    }
}

function Assert-FileContains {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Code
    )

    $fullPath = Resolve-RepoPath $Path
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "Public release safety scan failed: missing file $Path"
    }

    $content = Read-Utf8Text -Path $fullPath
    if ($content -notmatch $Pattern) {
        throw "Public release safety scan failed: $Code in $Path"
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
        throw "Public release safety scan failed: $Name"
    }
}

Test-SecretAndPrivatePathScan
Test-TrackedRuntimeAndTestArtifacts
Test-IgnoreRules
Test-MarkdownRelativeLinks

Assert-FileContains -Path 'tools/Test-TianShuCliReleasePackage.ps1' -Pattern 'Release package must not contain AGENTS\.md' -Code 'release_package_missing_agents_exclusion'
Assert-FileContains -Path 'docs/publishing/tianshu-release-smoke.md' -Pattern 'Test-TianShuPublicReleaseSafetyScan\.ps1' -Code 'release_smoke_missing_public_safety_scan'
Assert-FileContains -Path '.github/workflows/ci-release.yml' -Pattern 'Test-TianShuPublicReleaseSafetyScan\.ps1' -Code 'ci_missing_public_safety_scan'

if (-not $SkipRegressionTests) {
    Invoke-GateTest `
        -Name 'P31.4 public release safety scan regression' `
        -Project 'tests/TianShu.Execution.Integration.Tests/TianShu.Execution.Integration.Tests.csproj' `
        -Filter 'FullyQualifiedName~P31_4_PublicReleaseSafetyScan'
}

Write-Host 'TianShu public release safety scan passed.'
