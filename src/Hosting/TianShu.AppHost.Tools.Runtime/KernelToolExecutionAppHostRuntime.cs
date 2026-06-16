using System.Text;
using System.Text.Json;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelToolExecutionAppHostRuntime
{
    private readonly KernelNativeToolOptionsAppHostRuntime nativeToolOptionsAppHostRuntime;
    private readonly KernelToolRuntimeServicesAppHostRuntime toolRuntimeServicesAppHostRuntime;
    private readonly KernelToolRuntimeAppHostRuntime toolRuntimeAppHostRuntime;
    private readonly KernelToolCallAppHostRuntime toolCallAppHostRuntime;
    private readonly Func<string?, Dictionary<string, object?>> buildRuntimeConfigSnapshot;
    private readonly Func<string, string, McpServerElicitationRequest, CancellationToken, Task<McpServerElicitationResponse>> requestMcpServerElicitationAsync;
    private readonly Func<string, string, KernelRequestUserInputRequest, CancellationToken, Task<KernelRequestUserInputResponse>> requestUserInputAsync;
    private readonly Func<string, string, KernelRequestPermissionsRequest, CancellationToken, Task<KernelRequestPermissionsResponse>> requestPermissionsAsync;
    private readonly Func<CancellationToken, string?, Task<Dictionary<string, string>>> loadWritablePersistedConfigValuesAsync;
    private readonly Func<Dictionary<string, string>, CancellationToken, string?, Task<string>> saveConfigValuesAsync;

    public KernelToolExecutionAppHostRuntime(
        KernelNativeToolOptionsAppHostRuntime nativeToolOptionsAppHostRuntime,
        KernelToolRuntimeServicesAppHostRuntime toolRuntimeServicesAppHostRuntime,
        KernelToolRuntimeAppHostRuntime toolRuntimeAppHostRuntime,
        KernelToolCallAppHostRuntime toolCallAppHostRuntime,
        Func<string?, Dictionary<string, object?>> buildRuntimeConfigSnapshot,
        Func<string, string, McpServerElicitationRequest, CancellationToken, Task<McpServerElicitationResponse>> requestMcpServerElicitationAsync,
        Func<string, string, KernelRequestUserInputRequest, CancellationToken, Task<KernelRequestUserInputResponse>> requestUserInputAsync,
        Func<string, string, KernelRequestPermissionsRequest, CancellationToken, Task<KernelRequestPermissionsResponse>> requestPermissionsAsync,
        Func<CancellationToken, string?, Task<Dictionary<string, string>>> loadWritablePersistedConfigValuesAsync,
        Func<Dictionary<string, string>, CancellationToken, string?, Task<string>> saveConfigValuesAsync)
    {
        this.nativeToolOptionsAppHostRuntime = nativeToolOptionsAppHostRuntime;
        this.toolRuntimeServicesAppHostRuntime = toolRuntimeServicesAppHostRuntime;
        this.toolRuntimeAppHostRuntime = toolRuntimeAppHostRuntime;
        this.toolCallAppHostRuntime = toolCallAppHostRuntime;
        this.buildRuntimeConfigSnapshot = buildRuntimeConfigSnapshot;
        this.requestMcpServerElicitationAsync = requestMcpServerElicitationAsync;
        this.requestUserInputAsync = requestUserInputAsync;
        this.requestPermissionsAsync = requestPermissionsAsync;
        this.loadWritablePersistedConfigValuesAsync = loadWritablePersistedConfigValuesAsync;
        this.saveConfigValuesAsync = saveConfigValuesAsync;
    }

    public static IReadOnlyList<IKernelToolExecutionHook> CreateDefaultExecutionHooks(
        Func<string, object, CancellationToken, Task> writeNotificationAsync)
    {
        return
        [
            new NotificationToolExecutionHook(writeNotificationAsync),
        ];
    }

    public async Task<bool> TryPersistDynamicToolApprovalAsync(
        KernelDynamicToolDescriptor descriptor,
        string? cwd,
        CancellationToken cancellationToken)
    {
        if (!KernelToolRuntimeApprovalHelpers.TryGetDynamicToolApprovalOverrideKey(descriptor, out _, out _, out var overrideKey))
        {
            return false;
        }

        try
        {
            var values = await loadWritablePersistedConfigValuesAsync(cancellationToken, cwd).ConfigureAwait(false);
            values[overrideKey] = JsonSerializer.Serialize("approve");
            _ = await saveConfigValuesAsync(values, cancellationToken, cwd).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<KernelToolResult> ExecuteToolCallAsync(
        string threadId,
        string turnId,
        string itemId,
        string toolName,
        JsonElement arguments,
        TurnRequestContext context,
        KernelReadinessFlag? toolCallGate,
        CancellationToken cancellationToken,
        string? customInput = null,
        bool isCustomToolCall = false,
        string? externalCallId = null)
    {
        var nativeToolOptions = await ResolveResponsesNativeToolOptionsAsync(context, cancellationToken).ConfigureAwait(false);
        var effectiveArguments = arguments.ValueKind == JsonValueKind.Undefined
            ? JsonSerializer.SerializeToElement(new { })
            : arguments;
        if (isCustomToolCall
            && string.Equals(toolName, "apply_patch", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(customInput))
        {
            effectiveArguments = JsonSerializer.SerializeToElement(new
            {
                input = customInput,
            });
        }

        var resolvedCwd = KernelToolJsonHelpers.Normalize(context.Cwd) ?? Environment.CurrentDirectory;
        var runtimeContext = new KernelToolCallAppHostRuntimeContext(
            ThreadId: threadId,
            TurnId: turnId,
            ItemId: itemId,
            ToolName: toolName,
            Arguments: effectiveArguments,
            ResolvedCwd: resolvedCwd,
            RuntimeConfig: buildRuntimeConfigSnapshot(resolvedCwd),
            NativeToolOptions: nativeToolOptions,
            SandboxPolicy: context.SandboxPolicy,
            SandboxMode: context.SandboxMode,
            DynamicTools: context.DynamicTools,
            ApprovalPolicy: context.ApprovalPolicy,
            AllowLoginShell: context.AllowLoginShell,
            ShellEnvironmentPolicy: context.ShellEnvironmentPolicy,
            CollaborationMode: context.CollaborationMode,
            DefaultModeRequestUserInputEnabled: context.DefaultModeRequestUserInputEnabled,
            WindowsSandboxLevel: context.WindowsSandboxLevel,
            RuntimeServices: toolRuntimeServicesAppHostRuntime.CreateToolRuntimeServices(threadId, turnId, context),
            McpServerElicitationRequester: (request, innerCancellationToken) => requestMcpServerElicitationAsync(
                threadId,
                turnId,
                request,
                innerCancellationToken),
            UserInputRequester: (request, innerCancellationToken) => requestUserInputAsync(
                threadId,
                turnId,
                request,
                innerCancellationToken),
            PermissionRequester: (request, innerCancellationToken) => requestPermissionsAsync(
                threadId,
                turnId,
                request,
                innerCancellationToken),
            ExternalCallId: externalCallId);

        return await toolCallAppHostRuntime.ExecuteToolCallAsync(
            runtimeContext,
            toolCallGate,
            cancellationToken,
            customInput,
            isCustomToolCall).ConfigureAwait(false);
    }

    public async Task<string> ExecuteInlineToolCallAsync(
        string threadId,
        string turnId,
        KernelReadinessFlag toolCallGate,
        string toolName,
        JsonElement arguments,
        TurnRequestContext context,
        CancellationToken cancellationToken)
    {
        var itemId = $"tool_{toolName}_{turnId}";
        var result = await ExecuteToolCallAsync(
            threadId,
            turnId,
            itemId,
            toolName,
            arguments,
            context,
            toolCallGate,
            cancellationToken).ConfigureAwait(false);

        var builder = new StringBuilder();
        builder.Append("工具执行结果");
        builder.AppendLine();
        builder.Append("tool: ");
        builder.AppendLine(toolName);
        builder.Append("output:");
        builder.AppendLine();
        builder.Append(result.OutputText);
        return builder.ToString();
    }

    public async Task<KernelResponsesNativeToolOptions> ResolveResponsesNativeToolOptionsAsync(
        TurnRequestContext context,
        CancellationToken cancellationToken)
    {
        return await nativeToolOptionsAppHostRuntime.ResolveResponsesNativeToolOptionsAsync(
            new KernelNativeToolOptionsAppHostRuntimeContext(
                Cwd: context.Cwd,
                Model: context.Model,
                WebSearchMode: context.WebSearchMode,
                SandboxPolicy: context.SandboxPolicy,
                SandboxMode: context.SandboxMode,
                DynamicTools: context.DynamicTools,
                SessionSource: context.SessionSource,
                EnableAgentJobWorkerTools: context.EnableAgentJobWorkerTools,
                WindowsSandboxLevel: context.WindowsSandboxLevel),
            cancellationToken).ConfigureAwait(false);
    }

    public Task<KernelRequestUserInputResponse> RequestUserInputAsync(
        string threadId,
        string turnId,
        KernelRequestUserInputRequest request,
        CancellationToken cancellationToken)
        => toolRuntimeAppHostRuntime.RequestUserInputAsync(threadId, turnId, request, cancellationToken);

    public Task<KernelRequestPermissionsResponse> RequestPermissionsAsync(
        string threadId,
        string turnId,
        KernelRequestPermissionsRequest request,
        CancellationToken cancellationToken)
        => toolRuntimeAppHostRuntime.RequestPermissionsAsync(threadId, turnId, request, cancellationToken);
}
