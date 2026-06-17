param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore,
    [switch]$SkipStaticChecks,
    [string]$EvidenceRoot = 'Test/TianShuV08RemoteContinuityReleaseGate',
    [switch]$PreserveEvidence
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    Join-Path $repoRoot $Path
}

function Assert-FileContains {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Code
    )

    $fullPath = Resolve-RepoPath $Path
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "v0.8 remote continuity gate static check failed: missing file $Path"
    }

    $content = Get-Content -LiteralPath $fullPath -Raw
    if ($content -notmatch $Pattern) {
        throw "v0.8 remote continuity gate static check failed: $Code in $Path"
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
        throw "v0.8 remote continuity gate failed: $Name"
    }
}

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw "v0.8 remote continuity evidence check failed: $Message"
    }
}

function Get-ArrayValues {
    param($Value)

    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [System.Array]) {
        return @($Value)
    }

    return @($Value)
}

function Test-RemoteContinuityEvidence {
    param(
        [Parameter(Mandatory = $true)]$Evidence,
        [Parameter(Mandatory = $true)][string]$EvidenceText
    )

    $redactedKinds = Get-ArrayValues $Evidence.snapshotRedactedKinds
    $commandKinds = Get-ArrayValues $Evidence.submittedCommandKinds
    $approvalDecisions = Get-ArrayValues $Evidence.submittedApprovalDecisions

    Assert-Condition ($Evidence.success -eq $true) 'evidence success must be true.'
    Assert-Condition ($Evidence.acceptanceKind -eq 'deterministic-remote-continuity') 'acceptance kind must be deterministic-remote-continuity.'
    Assert-Condition ($Evidence.activationAccepted -eq $true) 'remote module activation must be accepted only after explicit activation context.'
    Assert-Condition ([int]$Evidence.readOnlyFollowerEventCount -gt 0) 'read-only follower must observe events.'
    Assert-Condition ($redactedKinds -contains 'absolute_path') 'snapshot redaction must include absolute_path.'
    Assert-Condition ($Evidence.cursorResumeReplayMode -eq 'FromCursor') 'cursor resume must use FromCursor when retained.'
    Assert-Condition ($Evidence.snapshotRefreshRequired -eq $true) 'expired cursor must require snapshot refresh.'
    Assert-Condition ($Evidence.remoteApprovalStatus -eq 'Accepted') 'remote approval must be accepted through the bridge.'
    Assert-Condition ($Evidence.remoteInterruptStatus -eq 'Accepted') 'remote interrupt must be accepted through the bridge.'
    Assert-Condition ($Evidence.remoteResumeStatus -eq 'Accepted') 'remote resume must be accepted through the bridge.'
    Assert-Condition ($Evidence.remoteFollowUpStatus -eq 'Accepted') 'remote follow-up must be accepted through the bridge.'
    Assert-Condition ($Evidence.duplicateFollowUpStatus -eq 'DuplicateIgnored') 'duplicate follow-up must be idempotently ignored.'
    Assert-Condition ($commandKinds -contains 'SubmitMessage') 'submitted command kinds must include SubmitMessage.'
    Assert-Condition ($commandKinds -contains 'ApprovalDecision') 'submitted command kinds must include ApprovalDecision.'
    Assert-Condition ($commandKinds -contains 'Interrupt') 'submitted command kinds must include Interrupt.'
    Assert-Condition ($commandKinds -contains 'Resume') 'submitted command kinds must include Resume.'
    Assert-Condition ($approvalDecisions -contains 'Approve') 'submitted approval decisions must include Approve.'
    Assert-Condition ($EvidenceText -match 'absolute_path') 'raw evidence text must record absolute_path redaction.'
}

function Invoke-RemoteContinuityAcceptance {
    $evidenceRootFullPath = Resolve-RepoPath $EvidenceRoot
    if (Test-Path -LiteralPath $evidenceRootFullPath) {
        Remove-Item -LiteralPath $evidenceRootFullPath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $evidenceRootFullPath -Force | Out-Null
    $outputPath = Join-Path $evidenceRootFullPath 'evidence.json'
    $projectPath = Resolve-RepoPath 'tools/acceptance/TianShu.RemoteContinuityAcceptance/TianShu.RemoteContinuityAcceptance.csproj'

    Write-Host '==> Deterministic remote continuity acceptance harness'
    $arguments = @(
        'run',
        '--project',
        $projectPath,
        '--configuration',
        $Configuration,
        '--',
        '--workdir',
        $evidenceRootFullPath,
        '--output',
        $outputPath
    )

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw 'v0.8 remote continuity gate failed: deterministic acceptance harness.'
    }

    if (-not (Test-Path -LiteralPath $outputPath)) {
        throw "v0.8 remote continuity gate failed: missing evidence $outputPath"
    }

    $evidenceText = [System.IO.File]::ReadAllText($outputPath)
    $evidence = ConvertFrom-Json -InputObject $evidenceText
    Test-RemoteContinuityEvidence -Evidence $evidence -EvidenceText $evidenceText

    if (-not $PreserveEvidence) {
        Remove-Item -LiteralPath $evidenceRootFullPath -Recurse -Force
    }
}

if (-not $SkipStaticChecks) {
    Assert-FileContains `
        -Path '.github/workflows/ci-release.yml' `
        -Pattern 'Test-TianShuV08RemoteContinuityReleaseGate\.ps1' `
        -Code 'ci_missing_v08_remote_continuity_gate'

    Assert-FileContains `
        -Path 'docs/publishing/tianshu-release-smoke.md' `
        -Pattern 'Test-TianShuV08RemoteContinuityReleaseGate\.ps1' `
        -Code 'release_smoke_docs_missing_v08_gate'

    Assert-FileContains `
        -Path 'docs/architecture/tianshu-remote-continuity-design.md' `
        -Pattern 'v0\.8 remote continuity release gate' `
        -Code 'remote_design_missing_v08_gate'

    Assert-FileContains `
        -Path 'docs/tianshu-architecture-spec.md' `
        -Pattern 'Test-TianShuV08RemoteContinuityReleaseGate\.ps1' `
        -Code 'architecture_spec_missing_v08_gate'

    Assert-FileContains `
        -Path 'README.md' `
        -Pattern '云中继也只是 transport adapter' `
        -Code 'readme_missing_remote_continuity_public_boundary'
}

Invoke-GateTest `
    -Name 'Remote contracts default safety, pairing, token, scope, audit, and projection security' `
    -Project 'tests/TianShu.Contracts.Remote.Tests/TianShu.Contracts.Remote.Tests.csproj' `
    -Filter 'FullyQualifiedName~Remote'

Invoke-GateTest `
    -Name 'Local Remote Module activation, read-only subscription, command admission, reconnect, and idempotency' `
    -Project 'tests/TianShu.Remote.Local.Tests/TianShu.Remote.Local.Tests.csproj' `
    -Filter 'FullyQualifiedName~LocalRemoteContinuityModuleTests'

Invoke-GateTest `
    -Name 'Host Gateway remote continuity bridges and dependency boundaries' `
    -Project 'tests/TianShu.HostGateway.Tests/TianShu.HostGateway.Tests.csproj' `
    -Filter 'FullyQualifiedName~RemoteCommandBridge_SubmitsCommandThroughHostGatewayUnifiedEntry|FullyQualifiedName~RemoteContinuityHostGatewayBridge_MapsThreadSnapshotForMultipleHostConsumers|FullyQualifiedName~RemoteContinuityHostGatewayBridge_SubscribeMapsHostViewUpdatesToRemoteEvents|FullyQualifiedName~RemoteCommandHostGatewayBridge_ShouldNotReferenceRuntimeWorkspaceOrStores|FullyQualifiedName~RemoteContinuityHostGatewayBridge_ShouldNotReferenceRuntimeWorkspaceStoresOrRemoteModuleImplementation'

Invoke-RemoteContinuityAcceptance

Write-Host 'TianShu v0.8 remote continuity release gate passed.'
