using System.Text.Json;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Tools;

/// <summary>
/// 工具注册上下文，由宿主在组合工具目录时提供。
/// Tool registration context provided by the host while composing the tool catalog.
/// </summary>
public sealed record TianShuToolRegistrationContext(
    string? WorkspacePath = null,
    string? ProfileId = null,
    MetadataBag? Metadata = null)
{
    public MetadataBag Metadata { get; init; } = Metadata ?? MetadataBag.Empty;
}

/// <summary>
/// 工具激活上下文，由宿主在创建具体工具 handler 时提供。
/// Tool activation context provided by the host when creating a concrete tool handler.
/// </summary>
public sealed record TianShuToolActivationContext(
    string? WorkspacePath = null,
    string? ProfileId = null,
    MetadataBag? Metadata = null)
{
    public MetadataBag Metadata { get; init; } = Metadata ?? MetadataBag.Empty;
}

/// <summary>
/// 工具调用上下文，暴露治理后的最小运行信息。
/// Tool invocation context that exposes the governed minimum runtime information.
/// </summary>
public sealed record TianShuToolInvocationContext(
    string ThreadId,
    string TurnId,
    string WorkingDirectory,
    IReadOnlyList<TianShuToolDiscoveryDescriptor>? DynamicTools = null,
    ITianShuMemoryToolServices? MemoryServices = null,
    ITianShuMcpResourceToolServices? McpResourceServices = null,
    ITianShuMcpToolServices? McpToolServices = null,
    ITianShuFileMutationToolServices? FileMutationServices = null,
    ITianShuToolSuggestionServices? ToolSuggestionServices = null,
    ITianShuShellToolServices? ShellServices = null,
    ITianShuInteractionToolServices? InteractionServices = null,
    ITianShuCollaborationToolServices? CollaborationServices = null,
    ITianShuFanoutToolServices? FanoutServices = null,
    ITianShuCodeToolServices? CodeServices = null,
    ITianShuArtifactToolServices? ArtifactServices = null,
    ITianShuToolDiagnosticServices? DiagnosticServices = null,
    MetadataBag? Metadata = null)
{
    public MetadataBag Metadata { get; init; } = Metadata ?? MetadataBag.Empty;
}

/// <summary>
/// 工具诊断严重级别。
/// Tool diagnostic severity.
/// </summary>
public enum TianShuToolDiagnosticSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>
/// 第三方工具可上报的受治理诊断事件。
/// Governed diagnostic event that third-party tools can report.
/// </summary>
public sealed record TianShuToolDiagnosticEvent
{
    /// <summary>
    /// 初始化工具诊断事件。
    /// Initializes a tool diagnostic event.
    /// </summary>
    public TianShuToolDiagnosticEvent(
        string toolKey,
        string code,
        string message,
        TianShuToolDiagnosticSeverity severity = TianShuToolDiagnosticSeverity.Info,
        MetadataBag? metadata = null)
    {
        ToolKey = IdentifierGuard.AgainstNullOrWhiteSpace(toolKey, nameof(toolKey));
        Code = IdentifierGuard.AgainstNullOrWhiteSpace(code, nameof(code));
        Message = IdentifierGuard.AgainstNullOrWhiteSpace(message, nameof(message));
        Severity = severity;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string ToolKey { get; }

    public string Code { get; }

    public string Message { get; }

    public TianShuToolDiagnosticSeverity Severity { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 工具域可调用的受治理诊断上报能力。
/// Governed diagnostic reporting capability available to tool domains.
/// </summary>
public interface ITianShuToolDiagnosticServices
{
    /// <summary>
    /// 上报工具诊断事件；宿主负责过滤、审计和投影。
    /// Reports a tool diagnostic event; the host owns filtering, auditing, and projection.
    /// </summary>
    Task ReportDiagnosticAsync(TianShuToolDiagnosticEvent diagnostic, CancellationToken cancellationToken);
}

/// <summary>
/// Artifact / View 工具域可调用的受治理宿主能力。
/// Governed host capabilities available to the Artifact / View tool domain.
/// </summary>
public interface ITianShuArtifactToolServices
{
    /// <summary>
    /// 通过宿主治理链路执行 Artifact / View 工具调用。
    /// Invokes an Artifact / View tool through the host-governed execution path.
    /// </summary>
    Task<TianShuArtifactToolResult> InvokeArtifactToolAsync(TianShuArtifactToolRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Code / REPL 工具域可调用的受治理宿主能力。
/// Governed host capabilities available to the Code / REPL tool domain.
/// </summary>
public interface ITianShuCodeToolServices
{
    /// <summary>
    /// 通过宿主治理链路执行 Code / REPL 工具调用。
    /// Invokes a Code / REPL tool through the host-governed execution path.
    /// </summary>
    Task<TianShuCodeToolResult> InvokeCodeToolAsync(TianShuCodeToolRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Fanout Jobs 工具域可调用的受治理宿主能力。
/// Governed host capabilities available to the Fanout Jobs tool domain.
/// </summary>
public interface ITianShuFanoutToolServices
{
    /// <summary>
    /// 通过宿主治理链路执行 fan-out job 工具调用。
    /// Invokes a fan-out job tool through the host-governed execution path.
    /// </summary>
    Task<TianShuFanoutToolResult> InvokeFanoutToolAsync(TianShuFanoutToolRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Collaboration / Multi-agent 工具域可调用的受治理宿主能力。
/// Governed host capabilities available to the Collaboration / Multi-agent tool domain.
/// </summary>
public interface ITianShuCollaborationToolServices
{
    /// <summary>
    /// 通过宿主治理链路执行协作或多代理工具调用。
    /// Invokes a collaboration or multi-agent tool through the host-governed execution path.
    /// </summary>
    Task<TianShuCollaborationToolResult> InvokeCollaborationToolAsync(TianShuCollaborationToolRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Interaction / Governance 工具域可调用的受治理宿主能力。
/// Governed host capabilities available to the Interaction / Governance tool domain.
/// </summary>
public interface ITianShuInteractionToolServices
{
    /// <summary>
    /// 通过宿主治理链路执行补录或权限请求工具调用。
    /// Invokes an interaction or governance tool through the host-governed execution path.
    /// </summary>
    Task<TianShuInteractionToolResult> InvokeInteractionToolAsync(TianShuInteractionToolRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Shell / Exec 工具域可调用的受治理宿主能力。
/// Governed host capabilities available to the Shell / Exec tool domain.
/// </summary>
public interface ITianShuShellToolServices
{
    /// <summary>
    /// 通过宿主治理链路执行 Shell / Exec 工具调用。
    /// Invokes a Shell / Exec tool through the host-governed execution path.
    /// </summary>
    Task<TianShuShellToolResult> InvokeShellToolAsync(TianShuShellToolRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// 写入文件系统工具域可调用的受治理宿主能力。
/// Governed host capabilities available to the mutating filesystem tool domain.
/// </summary>
public interface ITianShuFileMutationToolServices
{
    /// <summary>
    /// 判断指定完整路径是否允许写入，宿主必须合并 sandbox、权限与当前运行时策略。
    /// Determines whether a full path can be written after host sandbox, permission, and runtime policy checks.
    /// </summary>
    bool IsWritePathAllowed(string fullPath);

    /// <summary>
    /// 判断指定完整路径是否已通过文件变更审批。
    /// Determines whether a full path has an active file-change approval grant.
    /// </summary>
    bool IsFileChangeApproved(string fullPath);
}

/// <summary>
/// 工具建议流程可调用的受治理宿主能力。
/// Governed host capabilities available to tool suggestion providers.
/// </summary>
public interface ITianShuToolSuggestionServices
{
    /// <summary>
    /// 列出当前可建议安装或启用的 connector。
    /// Lists connectors that can currently be suggested for install or enable flows.
    /// </summary>
    Task<IReadOnlyList<TianShuToolSuggestConnectorInfo>> ListDiscoverableConnectorsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 提交 connector 建议请求，由宿主负责用户确认、外部安装链接和刷新判断。
    /// Submits a connector suggestion request; the host owns user confirmation, external install links, and refresh checks.
    /// </summary>
    Task<TianShuToolSuggestionResult> SuggestConnectorAsync(TianShuToolSuggestionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Memory 工具域可调用的受治理宿主能力。
/// Governed host capabilities available to the Memory tool domain.
/// </summary>
public interface ITianShuMemoryToolServices
{
    /// <summary>
    /// 按当前运行时策略过滤可见记忆。
    /// Filters visible memory records according to the current runtime policy.
    /// </summary>
    Task<MemoryQueryResult> FilterMemoryAsync(FilterMemory command, CancellationToken cancellationToken);

    /// <summary>
    /// 解析当前回合可见的记忆 overlay。
    /// Resolves the memory overlay visible to the current turn.
    /// </summary>
    Task<MemoryOverlay> ResolveMemoryOverlayAsync(ResolveMemoryOverlay command, CancellationToken cancellationToken);

    /// <summary>
    /// 记录记忆纠偏反馈，不直接覆盖长期事实。
    /// Records memory feedback without directly overwriting long-term facts.
    /// </summary>
    Task<MemoryMutationResult> RecordMemoryFeedbackAsync(RecordMemoryFeedback command, CancellationToken cancellationToken);
}

/// <summary>
/// MCP Resource 工具域可调用的受治理宿主能力。
/// Governed host capabilities available to the MCP Resource tool domain.
/// </summary>
public interface ITianShuMcpResourceToolServices
{
    /// <summary>
    /// 列出 MCP server 暴露的 resource。
    /// Lists resources exposed by MCP servers.
    /// </summary>
    Task<TianShuMcpListResourcesResult> ListResourcesAsync(string? server, string? cursor, CancellationToken cancellationToken);

    /// <summary>
    /// 列出 MCP server 暴露的 resource template。
    /// Lists resource templates exposed by MCP servers.
    /// </summary>
    Task<TianShuMcpListResourceTemplatesResult> ListResourceTemplatesAsync(string? server, string? cursor, CancellationToken cancellationToken);

    /// <summary>
    /// 读取指定 MCP resource。
    /// Reads a specific MCP resource.
    /// </summary>
    Task<TianShuMcpReadResourceResult> ReadResourceAsync(string server, string uri, CancellationToken cancellationToken);
}

/// <summary>
/// MCP resource 条目。
/// MCP resource entry.
/// </summary>
public sealed record TianShuMcpResourceEntry(string Server, JsonElement Resource);

/// <summary>
/// MCP resource template 条目。
/// MCP resource template entry.
/// </summary>
public sealed record TianShuMcpResourceTemplateEntry(string Server, JsonElement Template);

/// <summary>
/// MCP resource 列表结果。
/// MCP resource list result.
/// </summary>
public sealed record TianShuMcpListResourcesResult(
    string? Server,
    IReadOnlyList<TianShuMcpResourceEntry> Resources,
    string? NextCursor);

/// <summary>
/// MCP resource template 列表结果。
/// MCP resource template list result.
/// </summary>
public sealed record TianShuMcpListResourceTemplatesResult(
    string? Server,
    IReadOnlyList<TianShuMcpResourceTemplateEntry> ResourceTemplates,
    string? NextCursor);

/// <summary>
/// MCP resource 读取结果。
/// MCP resource read result.
/// </summary>
public sealed record TianShuMcpReadResourceResult(string Server, string Uri, JsonElement Result);

/// <summary>
/// MCP tool 描述符，用于把远端 MCP tool 投影为统一 ToolDescriptor。
/// MCP tool descriptor used to project a remote MCP tool into a unified ToolDescriptor.
/// </summary>
public sealed record TianShuMcpToolDescriptor
{
    public TianShuMcpToolDescriptor(
        string serverId,
        string toolName,
        string toolId,
        string displayName,
        string description,
        JsonElement inputSchema,
        JsonElement? outputSchema = null,
        SideEffectLevel sideEffectLevel = SideEffectLevel.ExternalMutation,
        bool requiresHumanGate = true,
        IReadOnlyList<string>? requiredScopes = null,
        ToolImplementationKind implementationKind = ToolImplementationKind.McpStdio)
    {
        ServerId = IdentifierGuard.AgainstNullOrWhiteSpace(serverId, nameof(serverId));
        ToolName = IdentifierGuard.AgainstNullOrWhiteSpace(toolName, nameof(toolName));
        ToolId = IdentifierGuard.AgainstNullOrWhiteSpace(toolId, nameof(toolId));
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        Description = IdentifierGuard.AgainstNullOrWhiteSpace(description, nameof(description));
        InputSchema = inputSchema.Clone();
        OutputSchema = outputSchema?.Clone();
        SideEffectLevel = sideEffectLevel;
        RequiresHumanGate = requiresHumanGate;
        RequiredScopes = requiredScopes ?? [$"mcp.{serverId}.{toolName}"];
        ImplementationKind = implementationKind;
    }

    public string ServerId { get; }

    public string ToolName { get; }

    public string ToolId { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public JsonElement InputSchema { get; }

    public JsonElement? OutputSchema { get; }

    public SideEffectLevel SideEffectLevel { get; }

    public bool RequiresHumanGate { get; }

    public IReadOnlyList<string> RequiredScopes { get; }

    public ToolImplementationKind ImplementationKind { get; }
}

/// <summary>
/// MCP tool 调用请求。
/// MCP tool invocation request.
/// </summary>
public sealed record TianShuMcpToolRequest(
    string ServerId,
    string ToolName,
    string ToolKey,
    StructuredValue Arguments,
    string RuntimeStepId,
    string SourceGraphId,
    string SourceStageId,
    MetadataBag? Metadata = null)
{
    public MetadataBag Metadata { get; init; } = Metadata ?? MetadataBag.Empty;
}

/// <summary>
/// MCP tool 调用结果。
/// MCP tool invocation result.
/// </summary>
public sealed record TianShuMcpToolResult(
    bool Success,
    string OutputText,
    StructuredValue? StructuredOutput = null,
    IReadOnlyList<ToolOutputContentItem>? OutputContentItems = null,
    IReadOnlyList<JsonElement>? RawOutputContentItems = null,
    string? FailureCode = null,
    string? FailureMessage = null);

/// <summary>
/// MCP Tool 工具域可调用的受治理宿主能力。
/// Governed host capabilities available to the MCP Tool domain.
/// </summary>
public interface ITianShuMcpToolServices
{
    /// <summary>
    /// 通过宿主治理链路执行 MCP tool。
    /// Invokes an MCP tool through the host-governed execution path.
    /// </summary>
    Task<TianShuMcpToolResult> InvokeMcpToolAsync(TianShuMcpToolRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Code / REPL 工具调用请求。
/// Code / REPL tool invocation request.
/// </summary>
public sealed record TianShuCodeToolRequest(
    string ToolKey,
    StructuredValue Arguments,
    string? CustomInput = null);

/// <summary>
/// Code / REPL 工具调用结果。
/// Code / REPL tool invocation result.
/// </summary>
public sealed record TianShuCodeToolResult(
    bool Success,
    string OutputText,
    StructuredValue? StructuredOutput = null,
    IReadOnlyList<ToolOutputContentItem>? OutputContentItems = null,
    IReadOnlyList<JsonElement>? RawOutputContentItems = null);

/// <summary>
/// Artifact / View 工具调用请求。
/// Artifact / View tool invocation request.
/// </summary>
public sealed record TianShuArtifactToolRequest(
    string ToolKey,
    StructuredValue Arguments,
    string? CustomInput = null);

/// <summary>
/// Artifact / View 工具调用结果。
/// Artifact / View tool invocation result.
/// </summary>
public sealed record TianShuArtifactToolResult(
    bool Success,
    string OutputText,
    StructuredValue? StructuredOutput = null,
    IReadOnlyList<ToolOutputContentItem>? OutputContentItems = null,
    IReadOnlyList<JsonElement>? RawOutputContentItems = null);

/// <summary>
/// Fanout Jobs 工具调用请求。
/// Fanout Jobs tool invocation request.
/// </summary>
public sealed record TianShuFanoutToolRequest(string ToolKey, StructuredValue Arguments);

/// <summary>
/// Fanout Jobs 工具调用结果。
/// Fanout Jobs tool invocation result.
/// </summary>
public sealed record TianShuFanoutToolResult(
    bool Success,
    string OutputText,
    StructuredValue? StructuredOutput = null);

/// <summary>
/// Collaboration / Multi-agent 工具调用请求。
/// Collaboration / Multi-agent tool invocation request.
/// </summary>
public sealed record TianShuCollaborationToolRequest(string ToolKey, StructuredValue Arguments);

/// <summary>
/// Collaboration / Multi-agent 工具调用结果。
/// Collaboration / Multi-agent tool invocation result.
/// </summary>
public sealed record TianShuCollaborationToolResult(
    bool Success,
    string OutputText,
    StructuredValue? StructuredOutput = null);

/// <summary>
/// Interaction / Governance 工具调用请求。
/// Interaction / Governance tool invocation request.
/// </summary>
public sealed record TianShuInteractionToolRequest(string ToolKey, StructuredValue Arguments);

/// <summary>
/// Interaction / Governance 工具调用结果。
/// Interaction / Governance tool invocation result.
/// </summary>
public sealed record TianShuInteractionToolResult(
    bool Success,
    string OutputText,
    StructuredValue? StructuredOutput = null);

/// <summary>
/// Shell / Exec 工具调用请求。
/// Shell / Exec tool invocation request.
/// </summary>
public sealed record TianShuShellToolRequest(string ToolKey, StructuredValue Arguments);

/// <summary>
/// Shell / Exec 工具调用结果。
/// Shell / Exec tool invocation result.
/// </summary>
public sealed record TianShuShellToolResult(
    bool Success,
    string OutputText,
    StructuredValue? StructuredOutput = null,
    string? FailureCode = null);

/// <summary>
/// 可建议安装或启用的 connector。
/// Discoverable connector that can be suggested for install or enable flows.
/// </summary>
public sealed record TianShuToolSuggestConnectorInfo(
    string Id,
    string Name,
    string? Description,
    string? InstallUrl);

/// <summary>
/// 工具建议请求。
/// Tool suggestion request.
/// </summary>
public sealed record TianShuToolSuggestionRequest(
    string ToolType,
    string ActionType,
    string ToolId,
    string SuggestReason);

/// <summary>
/// 工具建议结果。
/// Tool suggestion result.
/// </summary>
public sealed record TianShuToolSuggestionResult(
    bool Completed,
    bool UserConfirmed,
    string ToolType,
    string ActionType,
    string ToolId,
    string ToolName,
    string SuggestReason);

/// <summary>
/// 受治理的动态工具发现描述，用于让外部工具 Provider 在不依赖 Runtime 私有类型的情况下实现工具发现。
/// Governed dynamic-tool discovery descriptor for external providers without depending on runtime-private types.
/// </summary>
public sealed record TianShuToolDiscoveryDescriptor(
    string FullName,
    string ShortName,
    string? Namespace = null,
    string? Server = null,
    string? Title = null,
    string? Description = null,
    string? ConnectorName = null,
    string? ConnectorDescription = null,
    JsonElement? InputSchema = null);

/// <summary>
/// 第三方工具 provider 公共入口。
/// Public entry point for third-party tool providers.
/// </summary>
public interface ITianShuToolProvider
{
    IReadOnlyList<ToolDescriptor> DescribeTools(TianShuToolRegistrationContext context);

    ITianShuToolHandler CreateHandler(string toolKey, TianShuToolActivationContext context);
}

/// <summary>
/// 第三方工具 handler 公共入口。
/// Public entry point for third-party tool handlers.
/// </summary>
public interface ITianShuToolHandler
{
    ToolDescriptor Descriptor { get; }

    ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// 新架构统一工具入口，供 registry、authorization、audit 和 Execution Runtime bridge 消费。
/// Unified tool entry point for registry, authorization, audit, and the Execution Runtime bridge.
/// </summary>
public interface ITianShuTool
{
    ToolDescriptor Descriptor { get; }

    ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationEnvelope invocation,
        ToolInvocationContext context,
        CancellationToken cancellationToken);
}
