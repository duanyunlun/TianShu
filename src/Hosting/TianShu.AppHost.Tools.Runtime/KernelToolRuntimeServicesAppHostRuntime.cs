using System.Text.Json;
using TianShu.AppHost.Tools;
using TianShu.Contracts.Memory;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelToolRuntimeServicesAppHostRuntime
{
    private readonly KernelToolRuntimeAppHostRuntime toolRuntimeAppHostRuntime;
    private readonly KernelSpawnAgentsOnCsvAppHostRuntime spawnAgentsOnCsvAppHostRuntime;
    private readonly KernelMcpManager mcpManager;
    private readonly KernelArtifactsAppHostRuntime artifactsRuntime;
    private readonly KernelCodeModeAppHostRuntime codeModeRuntime;
    private readonly KernelJsReplAppHostRuntime jsReplRuntime;
    private readonly KernelPluginsAppHostRuntime pluginsAppHostRuntime;
    private readonly KernelThreadManager threadManager;
    private readonly Func<TurnRequestContext, CancellationToken, Task<KernelResponsesNativeToolOptions>> resolveResponsesNativeToolOptionsAsync;
    private readonly Func<string?, CancellationToken, Task<string?>> loadMergedPersistedConfigTextAsync;
    private readonly Func<KernelManagedNetworkExecutionRequest, CancellationToken, Task<KernelManagedNetworkExecutionLease>> beginManagedNetworkExecutionAsync;
    private readonly Func<string, string, string, string, JsonElement, TurnRequestContext, CancellationToken, string?, bool, Task<KernelToolResult>> executeNestedToolCallAsync;
    private readonly Func<string, TurnRequestContext, FilterMemory, CancellationToken, Task<MemoryQueryResult>> filterMemoryAsync;
    private readonly Func<string, TurnRequestContext, ResolveMemoryOverlay, CancellationToken, Task<MemoryOverlay>> resolveMemoryOverlayAsync;
    private readonly Func<string, TurnRequestContext, RecordMemoryFeedback, CancellationToken, Task<MemoryMutationResult>> recordMemoryFeedbackAsync;

    public KernelToolRuntimeServicesAppHostRuntime(
        KernelToolRuntimeAppHostRuntime toolRuntimeAppHostRuntime,
        KernelSpawnAgentsOnCsvAppHostRuntime spawnAgentsOnCsvAppHostRuntime,
        KernelMcpManager mcpManager,
        KernelArtifactsAppHostRuntime artifactsRuntime,
        KernelCodeModeAppHostRuntime codeModeRuntime,
        KernelJsReplAppHostRuntime jsReplRuntime,
        KernelPluginsAppHostRuntime pluginsAppHostRuntime,
        KernelThreadManager threadManager,
        Func<TurnRequestContext, CancellationToken, Task<KernelResponsesNativeToolOptions>> resolveResponsesNativeToolOptionsAsync,
        Func<string?, CancellationToken, Task<string?>> loadMergedPersistedConfigTextAsync,
        Func<KernelManagedNetworkExecutionRequest, CancellationToken, Task<KernelManagedNetworkExecutionLease>> beginManagedNetworkExecutionAsync,
        Func<string, string, string, string, JsonElement, TurnRequestContext, CancellationToken, string?, bool, Task<KernelToolResult>> executeNestedToolCallAsync,
        Func<string, TurnRequestContext, FilterMemory, CancellationToken, Task<MemoryQueryResult>> filterMemoryAsync,
        Func<string, TurnRequestContext, ResolveMemoryOverlay, CancellationToken, Task<MemoryOverlay>> resolveMemoryOverlayAsync,
        Func<string, TurnRequestContext, RecordMemoryFeedback, CancellationToken, Task<MemoryMutationResult>> recordMemoryFeedbackAsync)
    {
        this.toolRuntimeAppHostRuntime = toolRuntimeAppHostRuntime;
        this.spawnAgentsOnCsvAppHostRuntime = spawnAgentsOnCsvAppHostRuntime;
        this.mcpManager = mcpManager;
        this.artifactsRuntime = artifactsRuntime;
        this.codeModeRuntime = codeModeRuntime;
        this.jsReplRuntime = jsReplRuntime;
        this.pluginsAppHostRuntime = pluginsAppHostRuntime;
        this.threadManager = threadManager;
        this.resolveResponsesNativeToolOptionsAsync = resolveResponsesNativeToolOptionsAsync;
        this.loadMergedPersistedConfigTextAsync = loadMergedPersistedConfigTextAsync;
        this.beginManagedNetworkExecutionAsync = beginManagedNetworkExecutionAsync;
        this.executeNestedToolCallAsync = executeNestedToolCallAsync;
        this.filterMemoryAsync = filterMemoryAsync;
        this.resolveMemoryOverlayAsync = resolveMemoryOverlayAsync;
        this.recordMemoryFeedbackAsync = recordMemoryFeedbackAsync;
    }

    public KernelToolRuntimeServices CreateToolRuntimeServices(
        string threadId,
        string turnId,
        TurnRequestContext turnContext)
    {
        return new KernelToolRuntimeServices(
            UpdatePlan: (request, cancellationToken) => toolRuntimeAppHostRuntime.UpdatePlanAsync(threadId, turnId, request, cancellationToken),
            SpawnAgent: (request, cancellationToken) => toolRuntimeAppHostRuntime.SpawnAgentAsync(
                threadId,
                KernelToolRuntimeRequestContext.FromTurnRequestContext(turnContext),
                request,
                cancellationToken),
            SendInputToAgent: (request, cancellationToken) => toolRuntimeAppHostRuntime.SendInputToAgentAsync(
                KernelToolRuntimeRequestContext.FromTurnRequestContext(turnContext),
                request,
                cancellationToken),
            ResumeAgent: (agentId, cancellationToken) => toolRuntimeAppHostRuntime.ResumeAgentAsync(
                threadId,
                KernelToolRuntimeRequestContext.FromTurnRequestContext(turnContext),
                agentId,
                cancellationToken),
            WaitOnAgents: (agentIds, timeoutMs, cancellationToken) => toolRuntimeAppHostRuntime.WaitOnAgentsAsync(
                agentIds,
                timeoutMs,
                cancellationToken),
            CloseAgent: (agentId, cancellationToken) => toolRuntimeAppHostRuntime.CloseAgentAsync(agentId, cancellationToken),
            SpawnAgentsOnCsv: (request, cancellationToken) => spawnAgentsOnCsvAppHostRuntime.ExecuteAsync(
                threadId,
                turnId,
                KernelToolRuntimeRequestContext.FromTurnRequestContext(turnContext),
                request,
                cancellationToken),
            ReportAgentJobResult: (jobId, itemId, result, stop, cancellationToken) => toolRuntimeAppHostRuntime.ReportAgentJobResultAsync(
                threadId,
                jobId,
                itemId,
                result,
                stop,
                cancellationToken),
            ListMcpResources: (server, cursor, cancellationToken) => mcpManager.ListResourcesAsync(server, cursor, cancellationToken),
            ListMcpResourceTemplates: (server, cursor, cancellationToken) => mcpManager.ListResourceTemplatesAsync(server, cursor, cancellationToken),
            ReadMcpResource: (server, uri, cancellationToken) => mcpManager.ReadResourceAsync(server, uri, cancellationToken),
            ExecuteArtifacts: (request, cancellationToken) => ExecuteArtifactsAsync(
                threadId,
                turnId,
                turnContext,
                request,
                cancellationToken),
            BeginManagedNetworkExecution: async (request, cancellationToken) => await beginManagedNetworkExecutionAsync(
                request with { InteractionEnvelope = turnContext.InteractionEnvelope },
                cancellationToken).ConfigureAwait(false),
            ExecuteCodeMode: (request, cancellationToken) => ExecuteCodeModeAsync(
                threadId,
                turnId,
                turnContext,
                request,
                cancellationToken),
            WaitOnCodeMode: (request, cancellationToken) => WaitOnCodeModeAsync(
                threadId,
                turnId,
                turnContext,
                request,
                cancellationToken),
            ExecuteJsRepl: (request, cancellationToken) => ExecuteJsReplAsync(
                threadId,
                turnId,
                turnContext,
                request,
                cancellationToken),
            ResetJsRepl: cancellationToken => ResetJsReplAsync(
                threadId,
                turnId,
                turnContext,
                cancellationToken),
            ListToolSuggestDiscoverableConnectors: cancellationToken => pluginsAppHostRuntime.LoadToolSuggestDiscoverableConnectorsAsync(cancellationToken),
            RefreshOpenAiAppsToolSnapshot: cancellationToken => RefreshOpenAiAppsToolSnapshotAsync(
                threadId,
                cancellationToken),
            FilterMemory: (query, cancellationToken) => filterMemoryAsync(threadId, turnContext, query, cancellationToken),
            ResolveMemoryOverlay: (query, cancellationToken) => resolveMemoryOverlayAsync(threadId, turnContext, query, cancellationToken),
            RecordMemoryFeedback: (command, cancellationToken) => recordMemoryFeedbackAsync(threadId, turnContext, command, cancellationToken));
    }

    public async Task<KernelArtifactsExecutionResult> ExecuteArtifactsAsync(
        string threadId,
        string turnId,
        TurnRequestContext turnContext,
        KernelArtifactsExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var context = new KernelArtifactsAppHostContext(
            turnContext.Cwd,
            await loadMergedPersistedConfigTextAsync(turnContext.Cwd, cancellationToken).ConfigureAwait(false),
            turnContext.InteractionEnvelope);
        return await artifactsRuntime.ExecuteAsync(threadId, turnId, context, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<KernelCodeModeOperationResult> ExecuteCodeModeAsync(
        string threadId,
        string turnId,
        TurnRequestContext turnContext,
        KernelCodeModeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var context = await CreateCodeModeRuntimeContextAsync(turnContext, cancellationToken).ConfigureAwait(false);
        return await codeModeRuntime.ExecuteAsync(
            threadId,
            turnId,
            context,
            request,
            (toolName, nestedItemId, arguments, customInput, isCustomToolCall, innerCancellationToken) => executeNestedToolCallAsync(
                threadId,
                turnId,
                nestedItemId,
                toolName,
                arguments,
                turnContext,
                innerCancellationToken,
                customInput,
                isCustomToolCall),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<KernelCodeModeOperationResult> WaitOnCodeModeAsync(
        string threadId,
        string turnId,
        TurnRequestContext turnContext,
        KernelCodeModeWaitRequest request,
        CancellationToken cancellationToken)
    {
        var context = await CreateCodeModeRuntimeContextAsync(turnContext, cancellationToken).ConfigureAwait(false);
        return await codeModeRuntime.WaitAsync(
            threadId,
            turnId,
            context,
            request,
            (toolName, nestedItemId, arguments, customInput, isCustomToolCall, innerCancellationToken) => executeNestedToolCallAsync(
                threadId,
                turnId,
                nestedItemId,
                toolName,
                arguments,
                turnContext,
                innerCancellationToken,
                customInput,
                isCustomToolCall),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<KernelJsReplExecutionResult> ExecuteJsReplAsync(
        string threadId,
        string turnId,
        TurnRequestContext turnContext,
        KernelJsReplExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var context = await CreateJsReplRuntimeContextAsync(turnContext, cancellationToken).ConfigureAwait(false);
        return await jsReplRuntime.ExecuteAsync(
            threadId,
            turnId,
            context,
            request,
            (toolName, nestedItemId, arguments, innerCancellationToken) => executeNestedToolCallAsync(
                threadId,
                turnId,
                nestedItemId,
                toolName,
                arguments,
                turnContext,
                innerCancellationToken,
                null,
                false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ResetJsReplAsync(
        string threadId,
        string turnId,
        TurnRequestContext turnContext,
        CancellationToken cancellationToken)
    {
        var context = await CreateJsReplRuntimeContextAsync(turnContext, cancellationToken).ConfigureAwait(false);
        await jsReplRuntime.ResetAsync(threadId, turnId, context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<KernelOpenAiAppsToolSnapshot?> RefreshOpenAiAppsToolSnapshotAsync(
        string threadId,
        CancellationToken cancellationToken)
    {
        var snapshot = await pluginsAppHostRuntime.RefreshOpenAiAppsToolSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.DynamicTools is { Count: > 0 } dynamicTools
            && threadManager.TryGetThread(threadId, out var runtimeThread)
            && runtimeThread is not null)
        {
            runtimeThread.UpdateSession(runtimeThread.Session with
            {
                DynamicTools = KernelDynamicToolResolver.Clone(dynamicTools),
            });
        }

        return snapshot;
    }

    private async Task<KernelCodeModeAppHostContext> CreateCodeModeRuntimeContextAsync(
        TurnRequestContext turnContext,
        CancellationToken cancellationToken)
    {
        var nativeToolOptions = await resolveResponsesNativeToolOptionsAsync(turnContext, cancellationToken).ConfigureAwait(false);
        var configText = await loadMergedPersistedConfigTextAsync(turnContext.Cwd, cancellationToken).ConfigureAwait(false);
        return new KernelCodeModeAppHostContext(
            turnContext.Cwd,
            configText,
            turnContext.DynamicTools,
            nativeToolOptions,
            turnContext.InteractionEnvelope);
    }

    private async Task<KernelJsReplAppHostContext> CreateJsReplRuntimeContextAsync(
        TurnRequestContext turnContext,
        CancellationToken cancellationToken)
    {
        var configText = await loadMergedPersistedConfigTextAsync(turnContext.Cwd, cancellationToken).ConfigureAwait(false);
        return new KernelJsReplAppHostContext(turnContext.Cwd, configText, turnContext.InteractionEnvelope);
    }
}
