using System.Text.Json;
using TianShu.AppHost.Tools;
using TianShu.Contracts.Tools;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

internal interface IKernelToolHandler
{
    string Name { get; }

    string Description { get; }

    bool IsMutating { get; }

    bool SupportsParallelToolCalls { get; }

    JsonElement InputSchema { get; }

    JsonElement? OutputSchema { get; }

    ToolImplementationBinding ImplementationBinding { get; }

    ProviderResponsesToolDefinition BuildProviderToolDefinition();

    Task<KernelToolResult> ExecuteAsync(JsonElement arguments, KernelToolCallContext context, CancellationToken cancellationToken);

    Task<KernelToolResult> ExecuteCustomAsync(string input, KernelToolCallContext context, CancellationToken cancellationToken);
}

internal abstract class KernelToolHandlerBase : IKernelToolHandler
{
    protected KernelToolHandlerBase(
        string name,
        string description,
        bool isMutating,
        bool supportsParallelToolCalls,
        JsonElement inputSchema,
        JsonElement? outputSchema = null,
        ToolImplementationBinding? implementationBinding = null)
    {
        Name = name;
        Description = description;
        IsMutating = isMutating;
        SupportsParallelToolCalls = supportsParallelToolCalls;
        InputSchema = inputSchema.Clone();
        OutputSchema = outputSchema?.Clone();
        ImplementationBinding = implementationBinding ?? new ToolImplementationBinding(name, ToolImplementationKind.Managed);
    }

    public string Name { get; }

    public string Description { get; }

    public bool IsMutating { get; }

    public bool SupportsParallelToolCalls { get; }

    public JsonElement InputSchema { get; }

    public JsonElement? OutputSchema { get; }

    public ToolImplementationBinding ImplementationBinding { get; }

    public virtual ProviderResponsesToolDefinition BuildProviderToolDefinition()
        => new ProviderResponsesFunctionToolDefinition(
            Name,
            Description,
            InputSchema,
            OutputSchema,
            strict: false);

    public abstract Task<KernelToolResult> ExecuteAsync(JsonElement arguments, KernelToolCallContext context, CancellationToken cancellationToken);

    public virtual Task<KernelToolResult> ExecuteCustomAsync(string input, KernelToolCallContext context, CancellationToken cancellationToken)
    {
        _ = input;
        _ = context;
        _ = cancellationToken;
        return Task.FromResult(new KernelToolResult(false, $"工具 {Name} 不支持 freeform 输入。"));
    }

    protected static KernelToolResult Success(string output)
    {
        return new KernelToolResult(true, output);
    }

    protected static KernelToolResult Failure(string output)
    {
        return new KernelToolResult(false, output);
    }
}

internal abstract class KernelCustomToolHandlerBase : IKernelToolHandler
{
    protected KernelCustomToolHandlerBase(
        string name,
        string description,
        bool isMutating,
        bool supportsParallelToolCalls,
        JsonElement format,
        JsonElement? outputSchema = null,
        ToolImplementationBinding? implementationBinding = null)
    {
        Name = name;
        Description = description;
        IsMutating = isMutating;
        SupportsParallelToolCalls = supportsParallelToolCalls;
        Format = format.Clone();
        InputSchema = JsonSerializer.Deserialize<JsonElement>("{}");
        OutputSchema = outputSchema?.Clone();
        ImplementationBinding = implementationBinding ?? new ToolImplementationBinding(name, ToolImplementationKind.Managed);
    }

    protected JsonElement Format { get; }

    public string Name { get; }

    public string Description { get; }

    public bool IsMutating { get; }

    public bool SupportsParallelToolCalls { get; }

    public JsonElement InputSchema { get; }

    public JsonElement? OutputSchema { get; }

    public ToolImplementationBinding ImplementationBinding { get; }

    public virtual ProviderResponsesToolDefinition BuildProviderToolDefinition()
        => new ProviderResponsesCustomToolDefinition(
            Name,
            Description,
            Format,
            OutputSchema);

    public virtual Task<KernelToolResult> ExecuteAsync(JsonElement arguments, KernelToolCallContext context, CancellationToken cancellationToken)
    {
        _ = arguments;
        _ = context;
        _ = cancellationToken;
        return Task.FromResult(new KernelToolResult(false, $"工具 {Name} 需要 raw 文本输入。"));
    }

    public abstract Task<KernelToolResult> ExecuteCustomAsync(string input, KernelToolCallContext context, CancellationToken cancellationToken);

    protected static KernelToolResult Success(string output)
    {
        return new KernelToolResult(true, output);
    }

    protected static KernelToolResult Failure(string output)
    {
        return new KernelToolResult(false, output);
    }
}

internal sealed record KernelToolCallContext(
    string ThreadId,
    string TurnId,
    string Cwd,
    JsonElement? SandboxPolicy = null,
    string? SandboxMode = null,
    KernelPermissionGrantProfile? GrantedPermissions = null,
    IReadOnlyList<string>? ApprovedFileChangePaths = null,
    Func<McpServerElicitationRequest, CancellationToken, Task<McpServerElicitationResponse>>? McpServerElicitationRequester = null,
    KernelToolRuntimeServices? RuntimeServices = null,
    IReadOnlyList<KernelDynamicToolDescriptor>? DynamicTools = null,
    KernelApprovalPolicy? ApprovalPolicy = null,
    bool AllowLoginShell = true,
    KernelShellEnvironmentPolicy? ShellEnvironmentPolicy = null,
    string? ItemId = null,
    KernelCollaborationModeState? CollaborationMode = null,
    bool DefaultModeRequestUserInputEnabled = false,
    Func<KernelRequestUserInputRequest, CancellationToken, Task<KernelRequestUserInputResponse>>? UserInputRequester = null,
    bool ExecPermissionApprovalsEnabled = false,
    bool RequestPermissionsToolEnabled = false,
    bool RequestPermissionsEnabled = true,
    Func<KernelRequestPermissionsRequest, CancellationToken, Task<KernelRequestPermissionsResponse>>? PermissionRequester = null,
    bool SupportsImageInput = true,
    bool CanRequestOriginalImageDetail = false,
    KernelWindowsSandboxLevel WindowsSandboxLevel = KernelWindowsSandboxLevel.Disabled,
    string? ExternalCallId = null);

internal static class KernelFileChangeApprovalHelpers
{
    public static string NormalizeApprovalKey(string fullPath)
    {
        var normalized = Path.GetFullPath(fullPath);
        return Path.TrimEndingDirectorySeparator(normalized);
    }

    public static bool IsApproved(string fullPath, IReadOnlyList<string>? approvedPaths)
    {
        if (approvedPaths is null || approvedPaths.Count == 0)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedFullPath = NormalizeApprovalKey(fullPath);
        foreach (var approvedPath in approvedPaths)
        {
            if (string.Equals(normalizedFullPath, NormalizeApprovalKey(approvedPath), comparison))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class KernelToolResult
{
    public KernelToolResult(
        bool success,
        string outputText,
        IReadOnlyList<KernelToolOutputContentItem>? outputContentItems = null,
        IReadOnlyList<JsonElement>? rawOutputContentItems = null,
        JsonElement? structuredOutput = null,
        JsonElement? metadata = null)
    {
        Success = success;
        OutputContentItems = outputContentItems is null ? null : outputContentItems.ToArray();
        RawOutputContentItems = rawOutputContentItems is null ? null : rawOutputContentItems.Select(static item => item.Clone()).ToArray();
        StructuredOutput = structuredOutput?.Clone();
        Metadata = metadata?.Clone();
        OutputText = string.IsNullOrWhiteSpace(outputText)
            ? ToolUseFollowUpItemProjector.BuildTextPreview(ToContractOutputContentItems(OutputContentItems))
            : outputText;
    }

    public bool Success { get; }

    public string OutputText { get; }

    public IReadOnlyList<KernelToolOutputContentItem>? OutputContentItems { get; }

    public IReadOnlyList<JsonElement>? RawOutputContentItems { get; }

    public JsonElement? StructuredOutput { get; }

    public JsonElement? Metadata { get; }

    public object BuildFunctionCallOutputPayload()
        => ToolUseFollowUpItemProjector.BuildFunctionCallOutputPayload(
            OutputText,
            ToContractOutputContentItems(OutputContentItems));

    private static IReadOnlyList<ToolOutputContentItem>? ToContractOutputContentItems(
        IReadOnlyList<KernelToolOutputContentItem>? items)
        => items is null
            ? null
            : items.Select(static item => new ToolOutputContentItem(item.Type, item.Text, item.ImageUrl, item.Detail)).ToArray();
}

internal sealed record KernelToolExecutionHookContext(
    string ThreadId,
    string TurnId,
    string ItemId,
    string? ExternalCallId,
    string ToolName,
    JsonElement Arguments);

internal sealed record KernelToolExecutionHookAfterDecision(
    bool ShouldAbort,
    string? ErrorMessage)
{
    public static KernelToolExecutionHookAfterDecision Continue { get; } = new(false, null);

    public static KernelToolExecutionHookAfterDecision Abort(string errorMessage)
    {
        var normalized = KernelToolJsonHelpers.Normalize(errorMessage) ?? "after_tool_use hook aborted operation";
        return new KernelToolExecutionHookAfterDecision(true, normalized);
    }
}

internal sealed class KernelToolExecutionHookAbortException(string message) : InvalidOperationException(message);

internal interface IKernelToolExecutionHook
{
    string Name { get; }

    Task OnBeforeExecuteAsync(KernelToolExecutionHookContext context, CancellationToken cancellationToken);

    Task<KernelToolExecutionHookAfterDecision> OnAfterExecuteAsync(
        KernelToolExecutionHookContext context,
        KernelToolResult result,
        TimeSpan duration,
        CancellationToken cancellationToken);

    Task OnExecuteErrorAsync(
        KernelToolExecutionHookContext context,
        string error,
        TimeSpan duration,
        CancellationToken cancellationToken);
}
