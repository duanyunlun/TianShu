param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore,
    [switch]$SkipStaticChecks
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
        throw "v0.7 capability gate static check failed: missing file $Path"
    }

    $content = Get-Content -LiteralPath $fullPath -Raw
    if ($content -notmatch $Pattern) {
        throw "v0.7 capability gate static check failed: $Code in $Path"
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
        throw "v0.7 capability gate failed: $Name"
    }
}

if (-not $SkipStaticChecks) {
    Assert-FileContains `
        -Path '.github/workflows/ci-release.yml' `
        -Pattern 'Test-TianShuV07CapabilityReleaseGate\.ps1' `
        -Code 'ci_missing_v07_capability_gate'

    Assert-FileContains `
        -Path 'docs/publishing/tianshu-release-smoke.md' `
        -Pattern 'Test-TianShuV07CapabilityReleaseGate\.ps1' `
        -Code 'release_smoke_docs_missing_v07_gate'

    Assert-FileContains `
        -Path 'docs/tools/tianshu-tool-capability-design.md' `
        -Pattern 'v0\.7 capability release gate' `
        -Code 'capability_design_missing_v07_gate'
}

Invoke-GateTest `
    -Name 'Default capability surface and governed workspace mutation gate' `
    -Project 'tests/TianShu.Execution.Runtime.Tests/TianShu.Execution.Runtime.Tests.csproj' `
    -Filter 'FullyQualifiedName~KernelRuntimeTurnLoopComposition_ShouldRegisterOnlyReadOnlyFilesystemToolsByDefault|FullyQualifiedName~KernelRuntimeTurnLoopComposition_ShouldRegisterWorkspaceWriteToolOnlyWhenExplicitlyApproved|FullyQualifiedName~WorkspaceWriteTool_ShouldRejectWhenApprovalMissingAtToolBoundary|FullyQualifiedName~WorkspaceApplyPatchTool_ShouldRejectWhenApprovalMissingAtToolBoundary|FullyQualifiedName~WorkspaceApplyPatchTool_ShouldApplyApprovedPatchAndProjectWorkspaceMutation|FullyQualifiedName~WorkspaceApplyPatchTool_ShouldProjectCompensationWhenPartialApplyFails'

Invoke-GateTest `
    -Name 'Shell, MCP, and Memory governed RuntimeStep bridges' `
    -Project 'tests/TianShu.Execution.Runtime.Tests/TianShu.Execution.Runtime.Tests.csproj' `
    -Filter 'FullyQualifiedName~KernelRuntimeTurnLoopComposition_ShouldRegisterMcpOnlyWhenExplicitlyIncluded|FullyQualifiedName~ExecuteAsync_ShouldRunMcpResourceThroughRuntimeStepWhenExplicitlyBound|FullyQualifiedName~ExecuteAsync_ShouldRunMcpToolThroughGovernedRuntimeStepWhenExplicitlyBoundAndApproved|FullyQualifiedName~ExecuteAsync_ShouldBlockMcpToolBeforeInvocationWithoutApproval|FullyQualifiedName~ExecuteAsync_ShouldRejectMcpToolWhenStepDowngradesRemoteSideEffect|FullyQualifiedName~ExecuteAsync_ShouldRunShellCommandThroughRuntimeStepWhenExplicitlyBoundAndApproved|FullyQualifiedName~ExecuteAsync_ShouldBlockShellCommandBeforeInvocationWithoutApproval|FullyQualifiedName~ExecuteAsync_ShouldRejectDangerousShellCommandBeforeExecution|FullyQualifiedName~ExecuteAsync_ShouldRejectSensitiveShellEnvironmentAndKeepAllowedOverrides|FullyQualifiedName~ExecuteStepAsync_ShouldDispatchMemoryRetrieveThroughBoundMemoryModule|FullyQualifiedName~ExecuteStepAsync_ShouldDispatchMemoryFormThroughBoundMemoryModule|FullyQualifiedName~ExecuteStepAsync_ShouldDispatchMemorySupersedeThroughBoundMemoryModule|FullyQualifiedName~ExecuteStepAsync_ShouldFailClosedForInvalidMemorySupersedePayload'

Invoke-GateTest `
    -Name 'Structured context policy execution matrix' `
    -Project 'tests/TianShu.Execution.Runtime.Tests/TianShu.Execution.Runtime.Tests.csproj' `
    -Filter 'FullyQualifiedName~ContextPolicyExecutionTests'

Invoke-GateTest `
    -Name 'Reactive loop live smoke for tool request materialization and fail-closed denial' `
    -Project 'tests/TianShu.Execution.Runtime.Tests/TianShu.Execution.Runtime.Tests.csproj' `
    -Filter 'FullyQualifiedName~RunReactiveAsync_ShouldMaterializeToolStepsFromModelToolRequests|FullyQualifiedName~RunReactiveAsync_ShouldFailClosedWhenModelToolRequestIsNotAllowed|FullyQualifiedName~RunReactiveAsync_ShouldInjectToolResultsIntoNextModelReasonInput'

Invoke-GateTest `
    -Name 'Kernel StageGraph and safety gate coverage' `
    -Project 'tests/TianShu.Kernel.Tests/TianShu.Kernel.Tests.csproj' `
    -Filter 'FullyQualifiedName~DefaultTurnGraphNarrowsCapabilityToolsToGovernanceReadOnlyBoundary|FullyQualifiedName~DefaultTurnGraphKeepsSpawnAgentFailClosedWhenModuleIsNotGoverned|FullyQualifiedName~InterpreterMapsMemoryStagesToOfficialMemoryModuleCapabilities|FullyQualifiedName~AdaptiveKernelSafetyGateMatrixTests'

Invoke-GateTest `
    -Name 'Memory access contract governance coverage' `
    -Project 'tests/TianShu.Contracts.Memory.Tests/TianShu.Contracts.Memory.Tests.csproj' `
    -Filter 'FullyQualifiedName~MemoryContractTests'

Write-Host "TianShu v0.7 capability release gate passed."
