param(
    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "[TianShu Core Architecture Checks] $Message"
}

function Resolve-RepoRoot {
    $scriptRoot = Split-Path -Parent $PSCommandPath
    return (Resolve-Path -LiteralPath (Join-Path $scriptRoot '..')).Path
}

function Invoke-DotNetTest {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$Filter,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $projectPath = Join-Path $RepoRoot $Project
    if (-not (Test-Path -LiteralPath $projectPath)) {
        throw "测试项目不存在：$Project"
    }

    $arguments = @(
        'test',
        $projectPath,
        '--nologo',
        '--filter',
        $Filter,
        '--logger',
        'console;verbosity=minimal'
    )

    if ($NoRestore) {
        $arguments += '--no-restore'
    }

    Write-Step "$Name"
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "测试失败：$Name"
    }
}

$repoRoot = Resolve-RepoRoot
Push-Location $repoRoot
try {
    Write-Step "RepoRoot=$repoRoot"
    if ($NoRestore) {
        Write-Step "NoRestore=true"
    }

    # 25.8 只验证当前源码的核心架构守护，不打包、不安装、不调用用户级 tianshu.exe。
    Invoke-DotNetTest `
        -RepoRoot $repoRoot `
        -Project 'tests\TianShu.Execution.Runtime.Tests\TianShu.Execution.Runtime.Tests.csproj' `
        -Filter 'FullyQualifiedName~KernelRuntimeExecutionLoopTests' `
        -Name 'Runtime reactive loop / provider-tool bridge / replay diagnostics'

    Invoke-DotNetTest `
        -RepoRoot $repoRoot `
        -Project 'tests\TianShu.Cli.Tests\TianShu.Cli.Tests.csproj' `
        -Filter 'FullyQualifiedName~SendCommandRunner_WithKernelRuntimeLoop_FailsClosedWithoutProviderCredentialsAndPreservesReplayEvidence|FullyQualifiedName~SendCommandRunner_WithoutKernelRuntimeLoop_UsesDefaultKernelRuntimeLoop|FullyQualifiedName~SendCommandRunner_DefaultKernelRuntimeLoop_MatchesOptInSemanticProjection|FullyQualifiedName~SendCommandRunner_KernelRuntimeProductParityGate_RemainsOpenWhileTerminalDeltasExist|FullyQualifiedName~WriteControlPlaneLine_WritesVisibleOutputWithoutPersistingTranscript|FullyQualifiedName~InteractiveChatRunner_ScriptedFlow_HostOperationCommands_DoNotPersistControlOutputToTranscript|FullyQualifiedName~SendCommandRunner_DoesNotReferenceKernelRuntimeInternalsDirectly|FullyQualifiedName~InteractiveChatSessionHost_ShouldTreatThreadSlashCommandsAsControlPlaneOutput' `
        -Name 'CLI default kernel runtime loop / parity gate / experience boundary'

    Invoke-DotNetTest `
        -RepoRoot $repoRoot `
        -Project 'tests\TianShu.HostGateway.Tests\TianShu.HostGateway.Tests.csproj' `
        -Filter 'FullyQualifiedName~SnapshotAsync_ReadsHostProjectionSnapshotWithoutKernelInternals|FullyQualifiedName~IHostGatewaySubscribeAsync_ProjectsOnlyHostViewUpdates|FullyQualifiedName~HostGatewayProject_ShouldNotReferenceKernelExecutionRuntimeOrModuleImplementations' `
        -Name 'HostGateway projection-only boundary'

    Invoke-DotNetTest `
        -RepoRoot $repoRoot `
        -Project 'tests\TianShu.Contracts.Modules.Tests\TianShu.Contracts.Modules.Tests.csproj' `
        -Filter 'FullyQualifiedName~ModuleDescriptorContractTests' `
        -Name 'Module descriptor contract'

    Invoke-DotNetTest `
        -RepoRoot $repoRoot `
        -Project 'tests\TianShu.Provider.OpenAI.Tests\TianShu.Provider.OpenAI.Tests.csproj' `
        -Filter 'FullyQualifiedName~ProviderModuleDescriptorTests' `
        -Name 'Provider descriptor contract'

    Invoke-DotNetTest `
        -RepoRoot $repoRoot `
        -Project 'tests\TianShu.AppHost.Tests\TianShu.AppHost.Tests.csproj' `
        -Filter 'FullyQualifiedName~ToolCapabilityDescriptorTests' `
        -Name 'Tool descriptor contract'

    Invoke-DotNetTest `
        -RepoRoot $repoRoot `
        -Project 'tests\TianShu.Execution.Integration.Tests\TianShu.Execution.Integration.Tests.csproj' `
        -Filter 'FullyQualifiedName~DocumentationTerminologyRegressionTests|FullyQualifiedName~CoreArchitectureDependencyBoundaryTests' `
        -Name 'Formal documentation terminology and core dependency boundaries'

    Write-Step 'All checks passed.'
}
finally {
    Pop-Location
}
