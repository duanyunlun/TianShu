using System.Text.Json;
using System.Text.Json.Nodes;
using TianShu.AppHost.Tools;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Tools;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Runtime 注入给工具 Provider adapter 的受治理宿主服务集合。
/// Governed host service collection injected into tool Provider adapters by Runtime.
/// </summary>
internal sealed record KernelToolRuntimeServices(
    Func<KernelPlanUpdateRequest, CancellationToken, Task>? UpdatePlan = null,
    Func<KernelSpawnAgentRequest, CancellationToken, Task<KernelSpawnAgentResponse>>? SpawnAgent = null,
    Func<KernelSendInputRequest, CancellationToken, Task<KernelSendInputResponse>>? SendInputToAgent = null,
    Func<string, CancellationToken, Task<JsonNode?>>? ResumeAgent = null,
    Func<IReadOnlyList<string>, int?, CancellationToken, Task<KernelWaitAgentsResponse>>? WaitOnAgents = null,
    Func<string, CancellationToken, Task<JsonNode?>>? CloseAgent = null,
    Func<KernelSpawnAgentsOnCsvRequest, CancellationToken, Task<KernelSpawnAgentsOnCsvResponse>>? SpawnAgentsOnCsv = null,
    Func<string, string, JsonElement, bool, CancellationToken, Task<bool>>? ReportAgentJobResult = null,
    Func<string?, string?, CancellationToken, Task<KernelMcpListResourcesResult>>? ListMcpResources = null,
    Func<string?, string?, CancellationToken, Task<KernelMcpListResourceTemplatesResult>>? ListMcpResourceTemplates = null,
    Func<string, string, CancellationToken, Task<KernelMcpReadResourceResult>>? ReadMcpResource = null,
    Func<KernelArtifactsExecutionRequest, CancellationToken, Task<KernelArtifactsExecutionResult>>? ExecuteArtifacts = null,
    Func<KernelManagedNetworkExecutionRequest, CancellationToken, Task<IKernelManagedNetworkExecutionLease>>? BeginManagedNetworkExecution = null,
    Func<KernelCodeModeExecutionRequest, CancellationToken, Task<KernelCodeModeOperationResult>>? ExecuteCodeMode = null,
    Func<KernelCodeModeWaitRequest, CancellationToken, Task<KernelCodeModeOperationResult>>? WaitOnCodeMode = null,
    Func<KernelJsReplExecutionRequest, CancellationToken, Task<KernelJsReplExecutionResult>>? ExecuteJsRepl = null,
    Func<CancellationToken, Task>? ResetJsRepl = null,
    Func<CancellationToken, Task<IReadOnlyList<KernelToolSuggestConnectorInfo>>>? ListToolSuggestDiscoverableConnectors = null,
    Func<CancellationToken, Task<KernelOpenAiAppsToolSnapshot?>>? RefreshOpenAiAppsToolSnapshot = null,
    Func<FilterMemory, CancellationToken, Task<MemoryQueryResult>>? FilterMemory = null,
    Func<ResolveMemoryOverlay, CancellationToken, Task<MemoryOverlay>>? ResolveMemoryOverlay = null,
    Func<RecordMemoryFeedback, CancellationToken, Task<MemoryMutationResult>>? RecordMemoryFeedback = null,
    Func<TianShuToolDiagnosticEvent, CancellationToken, Task>? ReportToolDiagnostic = null);
