param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore,
    [switch]$SkipStaticChecks,
    [string]$EvidenceRoot = 'Test/TianShuV09MultiAgentReleaseGate',
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
        throw "v0.9 multi-agent gate static check failed: missing file $Path"
    }

    $content = Get-Content -LiteralPath $fullPath -Raw
    if ($content -notmatch $Pattern) {
        throw "v0.9 multi-agent gate static check failed: $Code in $Path"
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
        throw "v0.9 multi-agent gate failed: $Name"
    }
}

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw "v0.9 multi-agent evidence check failed: $Message"
    }
}

function ConvertFrom-JsonCompat {
    param([Parameter(Mandatory = $true)][string]$Text)

    if ($PSVersionTable.PSVersion.Major -ge 6) {
        return $Text | ConvertFrom-Json -Depth 100
    }

    return $Text | ConvertFrom-Json
}

function Test-MultiAgentEvidence {
    param(
        [Parameter(Mandatory = $true)]$Evidence,
        [Parameter(Mandatory = $true)][string]$EvidenceText
    )

    Assert-Condition ($Evidence.success -eq $true) 'evidence success must be true.'
    Assert-Condition ($Evidence.acceptanceKind -eq 'deterministic-subagent-mechanism') 'acceptance kind must be deterministic-subagent-mechanism.'
    Assert-Condition ($Evidence.moduleCapabilityStepObserved -eq $true) 'ModuleCapabilityStep must be observed.'
    Assert-Condition ($Evidence.subAgentBridgeObserved -eq $true) 'SubAgent module bridge must be observed.'
    Assert-Condition ($Evidence.parentSecondModelReceivedToolResult -eq $true) 'parent model must receive child tool result.'
    Assert-Condition ($Evidence.multiAgentFinalCaseObserved -eq $true) 'multi-agent final case must be observed.'
    Assert-Condition ($Evidence.parallelFanoutObserved -eq $true) 'parallel fanout must be observed.'
    Assert-Condition ($Evidence.subtreeGovernanceObserved -eq $true) 'subtree governance must be observed.'
    Assert-Condition ($Evidence.budgetSplitObserved -eq $true) 'budget split must be observed.'
    Assert-Condition ($Evidence.fanInObserved -eq $true) 'fan-in must be observed.'
    Assert-Condition ($Evidence.failureIsolationObserved -eq $true) 'failure isolation must be observed.'
    Assert-Condition ($Evidence.wholeTreeDiagnosticsObserved -eq $true) 'whole-tree diagnostics must be observed.'
    Assert-Condition ([int]$Evidence.plannedSubTaskCount -ge 3) 'planned sub-task count must be at least 3.'
    Assert-Condition ([int]$Evidence.maxConcurrentAgents -ge 2) 'maxConcurrentAgents must prove bounded parallelism.'
    Assert-Condition ([int]$Evidence.completedChildCount -ge 2) 'at least two child runs must complete.'
    Assert-Condition ([int]$Evidence.failedChildCount -ge 1) 'at least one child run must fail in the isolation case.'
    Assert-Condition ([int]$Evidence.treeNodeCount -ge 4) 'tree diagnostics must contain root plus child nodes.'
    Assert-Condition ([int]$Evidence.treeEdgeCount -ge 3) 'tree diagnostics must contain child edges.'
    Assert-Condition ($EvidenceText -match 'sub_agent\.tree_diagnostics\.v1') 'raw evidence must include whole-tree diagnostics schema.'
}

function Invoke-MultiAgentAcceptance {
    $evidenceRootFullPath = Resolve-RepoPath $EvidenceRoot
    if (Test-Path -LiteralPath $evidenceRootFullPath) {
        Remove-Item -LiteralPath $evidenceRootFullPath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $evidenceRootFullPath -Force | Out-Null
    $outputPath = Join-Path $evidenceRootFullPath 'evidence.json'
    $projectPath = Resolve-RepoPath 'tools/acceptance/TianShu.SubAgentAcceptance/TianShu.SubAgentAcceptance.csproj'

    Write-Host '==> Deterministic multi-agent acceptance harness'
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
        throw 'v0.9 multi-agent gate failed: deterministic acceptance harness.'
    }

    if (-not (Test-Path -LiteralPath $outputPath)) {
        throw "v0.9 multi-agent gate failed: missing evidence $outputPath"
    }

    $evidenceText = [System.IO.File]::ReadAllText($outputPath)
    $evidence = ConvertFrom-JsonCompat -Text $evidenceText
    Test-MultiAgentEvidence -Evidence $evidence -EvidenceText $evidenceText

    if (-not $PreserveEvidence) {
        Remove-Item -LiteralPath $evidenceRootFullPath -Recurse -Force
    }
}

if (-not $SkipStaticChecks) {
    Assert-FileContains `
        -Path '.github/workflows/ci-release.yml' `
        -Pattern 'Test-TianShuV09MultiAgentReleaseGate\.ps1' `
        -Code 'ci_missing_v09_multi_agent_gate'

    Assert-FileContains `
        -Path 'docs/publishing/tianshu-release-smoke.md' `
        -Pattern 'Test-TianShuV09MultiAgentReleaseGate\.ps1' `
        -Code 'release_smoke_docs_missing_v09_gate'

    Assert-FileContains `
        -Path 'docs/architecture/tianshu-subagent-design.md' `
        -Pattern 'v0\.9 multi-agent release gate' `
        -Code 'subagent_design_missing_v09_gate'

    Assert-FileContains `
        -Path 'README.md' `
        -Pattern 'v0\.9 multi-agent release gate' `
        -Code 'readme_missing_v09_multi_agent_gate'

    Assert-FileContains `
        -Path 'src/Presentations/TianShu.Cli/Commands/Options/SendCommandOptions.cs' `
        -Pattern '--enable-subagents[\s\S]*--approve-all[\s\S]*HostMutation' `
        -Code 'cli_missing_subagent_approval_boundary'

    Assert-FileContains `
        -Path 'tools/Run-TianShuFinalAcceptance.ps1' `
        -Pattern 'SubAgentLiveObservationProtocol' `
        -Code 'final_acceptance_missing_subagent_live_observation_protocol'

    Assert-FileContains `
        -Path 'tools/Run-TianShuFinalAcceptance.ps1' `
        -Pattern 'SubAgentLiveTriggerRates' `
        -Code 'final_acceptance_missing_subagent_live_trigger_rates'

    Assert-FileContains `
        -Path 'tools/Run-TianShuFinalAcceptance.ps1' `
        -Pattern 'SubAgentLiveOverallTriggerRate[\s\S]*SubAgentLiveOverallEffectiveRate[\s\S]*SubAgentLiveOverallFalsePositiveRate' `
        -Code 'final_acceptance_missing_subagent_live_overall_rates'

    Assert-FileContains `
        -Path 'tools/Run-TianShuFinalAcceptance.ps1' `
        -Pattern 'SubAgentLiveConclusion' `
        -Code 'final_acceptance_missing_subagent_live_conclusion'

    Assert-FileContains `
        -Path 'tools/Run-TianShuFinalAcceptance.ps1' `
        -Pattern 'SubAgentLiveMatrix' `
        -Code 'final_acceptance_missing_subagent_live_matrix'

    Assert-FileContains `
        -Path 'tools/Run-TianShuFinalAcceptance.ps1' `
        -Pattern 'SubAgentLiveArtifactsRoot' `
        -Code 'final_acceptance_missing_subagent_live_artifacts_root'

    Assert-FileContains `
        -Path 'tools/Run-TianShuFinalAcceptance.ps1' `
        -Pattern 'SubAgentMultiAgentFinalCaseAccepted[\s\S]*SubAgentMechanismParallelFanoutObserved[\s\S]*SubAgentMechanismWholeTreeDiagnosticsObserved' `
        -Code 'final_acceptance_missing_deterministic_multi_agent_report'

    Assert-FileContains `
        -Path 'docs/publishing/tianshu-release-smoke.md' `
        -Pattern 'deterministic fanout/fan-in mechanism evidence' `
        -Code 'release_smoke_docs_missing_v09_multi_agent_evidence_boundary'
}

Invoke-GateTest `
    -Name 'CLI SubAgent opt-in requires explicit approval boundary' `
    -Project 'tests/TianShu.Cli.Tests/TianShu.Cli.Tests.csproj' `
    -Filter 'FullyQualifiedName~Parse_SendKernelRuntimeLoop_EnableSubAgents_WithApproveAll_Succeeds|FullyQualifiedName~Parse_SendKernelRuntimeLoop_EnableSubAgents_WithoutApproveAll_FailsClosed'

Invoke-GateTest `
    -Name 'Kernel StageGraph keeps spawn_agent fail-closed unless SubAgent module is governed' `
    -Project 'tests/TianShu.Kernel.Tests/TianShu.Kernel.Tests.csproj' `
    -Filter 'FullyQualifiedName~DefaultTurnGraphExposesSpawnAgentOnlyWhenSubAgentModuleIsGoverned|FullyQualifiedName~DefaultTurnGraphKeepsSpawnAgentFailClosedWhenModuleIsNotGoverned'

Invoke-GateTest `
    -Name 'Provider tool surface hides SubAgent tools by default and exposes them only when enabled' `
    -Project 'tests/TianShu.Provider.OpenAI.Tests/TianShu.Provider.OpenAI.Tests.csproj' `
    -Filter 'FullyQualifiedName~RunAsync_ShouldHideMultiAgentAndFanoutTools_WhenFeaturesAreDisabled|FullyQualifiedName~RunAsync_ShouldIncludeConfiguredMultiAgentAndFanoutTools_ButKeepWorkerToolHiddenForRegularTurns'

Invoke-GateTest `
    -Name 'Runtime SubAgent fanout, governance, fan-in, diagnostics, and fork-bomb guard coverage' `
    -Project 'tests/TianShu.Execution.Runtime.Tests/TianShu.Execution.Runtime.Tests.csproj' `
    -Filter 'FullyQualifiedName~SubAgent'

Invoke-GateTest `
    -Name 'v0.9 multi-agent release gate documentation regression' `
    -Project 'tests/TianShu.Execution.Integration.Tests/TianShu.Execution.Integration.Tests.csproj' `
    -Filter 'FullyQualifiedName~V09MultiAgentReleaseGate'

Invoke-MultiAgentAcceptance

Write-Host 'TianShu v0.9 multi-agent release gate passed.'
