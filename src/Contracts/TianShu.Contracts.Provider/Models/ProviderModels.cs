using TianShu.Contracts.Primitives;
using TianShu.Contracts.Kernel;

namespace TianShu.Contracts.Provider;

/// <summary>
/// Provider 输入项基类。
/// Base type for provider input items.
/// </summary>
public abstract record ProviderInputItem(string Kind);

/// <summary>
/// 文本 Provider 输入项。
/// Text provider input item.
/// </summary>
public sealed record TextProviderInputItem(string Text) : ProviderInputItem("text")
{
    public string Text { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Text, nameof(Text));
}

/// <summary>
/// 图像 Provider 输入项。
/// Image provider input item.
/// </summary>
public sealed record ImageProviderInputItem(string Url) : ProviderInputItem("image")
{
    public string Url { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Url, nameof(Url));
}

/// <summary>
/// 模型工具调用 Provider 输入项，用于在工具结果回流前保留上一轮模型 tool call 事实。
/// Provider input item for a model tool call, preserving the previous model tool-call fact before tool-result replay.
/// </summary>
public sealed record ToolCallProviderInputItem(
    CallId CallId,
    string ToolId,
    StructuredValue Arguments,
    string? Content = null,
    string? ReasoningContent = null) : ProviderInputItem("tool_call")
{
    public string ToolId { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(ToolId, nameof(ToolId));

    public StructuredValue Arguments { get; } = Arguments ?? throw new ArgumentNullException(nameof(Arguments));
}

/// <summary>
/// 工具结果 Provider 输入项。
/// Tool-result provider input item.
/// </summary>
public sealed record ToolResultProviderInputItem(CallId CallId, StructuredValue Result) : ProviderInputItem("tool_result")
{
    public StructuredValue Result { get; } = Result ?? throw new ArgumentNullException(nameof(Result));
}

/// <summary>
/// Provider 会话上下文。
/// Provider conversation context.
/// </summary>
public sealed record ProviderConversationContext(
    ThreadId? ThreadId = null,
    TurnId? TurnId = null,
    string? SystemPrompt = null,
    IReadOnlyList<ProviderInputItem>? History = null)
{
    public IReadOnlyList<ProviderInputItem> History { get; } = History ?? Array.Empty<ProviderInputItem>();
}

/// <summary>
/// Provider 轮次状态。
/// Provider turn state.
/// </summary>
public sealed record ProviderTurnState(string? ProviderThreadId = null, string? ProviderTurnId = null, MetadataBag? Metadata = null)
{
    public MetadataBag Metadata { get; } = Metadata ?? MetadataBag.Empty;
}

/// <summary>
/// Provider 协议族。
/// Provider protocol family.
/// </summary>
public enum ProviderProtocolKind
{
    OpenAiChatCompletions = 0,
    OpenAiResponses = 1,
    AnthropicMessages = 2,
    GoogleGenerative = 3,
    Custom = 100,
}

/// <summary>
/// Provider 模块描述符。
/// Provider module descriptor.
/// </summary>
public sealed record ProviderDescriptor
{
    public ProviderDescriptor(
        string providerId,
        string displayName,
        ProviderProtocolKind protocolKind,
        ProviderCapabilityProfile capabilities,
        IReadOnlyList<ProviderModelDescriptor>? models = null,
        ProviderEndpointDescriptor? endpoint = null,
        PermissionEnvelope? permission = null,
        SideEffectProfile? sideEffects = null,
        MetadataBag? metadata = null)
    {
        ProviderId = IdentifierGuard.AgainstNullOrWhiteSpace(providerId, nameof(providerId));
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        ProtocolKind = protocolKind;
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        Models = models ?? Array.Empty<ProviderModelDescriptor>();
        Endpoint = endpoint;
        Permission = permission ?? CreateDefaultPermission(providerId);
        SideEffects = sideEffects ?? CreateDefaultSideEffects(providerId);
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string ProviderId { get; }

    public string DisplayName { get; }

    public ProviderProtocolKind ProtocolKind { get; }

    public ProviderCapabilityProfile Capabilities { get; }

    public IReadOnlyList<ProviderModelDescriptor> Models { get; }

    public ProviderEndpointDescriptor? Endpoint { get; }

    public PermissionEnvelope Permission { get; }

    public SideEffectProfile SideEffects { get; }

    public MetadataBag Metadata { get; }

    private static PermissionEnvelope CreateDefaultPermission(string providerId)
        => new(
            scopes: [$"provider.{NormalizePolicyToken(providerId)}.invoke"],
            requiresHumanGate: false,
            reason: "Provider invocation must be approved by Execution Runtime through ProviderInvocationRequest.");

    private static SideEffectProfile CreateDefaultSideEffects(string providerId)
        => new(
            SideEffectLevel.ExternalNetwork,
            affectedResources: [$"provider:{NormalizePolicyToken(providerId)}", "network"],
            reversible: false,
            requiresAudit: true);

    private static string NormalizePolicyToken(string value)
    {
        var chars = value.Select(static character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '.').ToArray();
        var normalized = new string(chars);
        while (normalized.Contains("..", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("..", ".", StringComparison.Ordinal);
        }

        return normalized.Trim('.');
    }
}

/// <summary>
/// Provider endpoint 描述。
/// Provider endpoint descriptor.
/// </summary>
public sealed record ProviderEndpointDescriptor
{
    /// <summary>
    /// 初始化 Provider endpoint 描述。
    /// Initializes a provider endpoint descriptor.
    /// </summary>
    public ProviderEndpointDescriptor(
        string providerId,
        ProviderProtocolKind protocolKind,
        string baseUrl,
        string? apiKeyEnvironmentVariable = null)
    {
        ProviderId = IdentifierGuard.AgainstNullOrWhiteSpace(providerId, nameof(providerId));
        ProtocolKind = protocolKind;
        BaseUrl = IdentifierGuard.AgainstNullOrWhiteSpace(baseUrl, nameof(baseUrl));
        ApiKeyEnvironmentVariable = apiKeyEnvironmentVariable;
    }

    public string ProviderId { get; }

    public ProviderProtocolKind ProtocolKind { get; }

    public string BaseUrl { get; }

    public string? ApiKeyEnvironmentVariable { get; }
}

/// <summary>
/// Provider 能力 profile。
/// Provider capability profile.
/// </summary>
public sealed record ProviderCapabilityProfile(
    bool SupportsStreaming = true,
    bool SupportsTools = false,
    bool SupportsReasoning = false,
    bool SupportsJsonSchema = false,
    bool SupportsWebSockets = false);

/// <summary>
/// Provider 模型描述。
/// Provider model descriptor.
/// </summary>
public sealed record ProviderModelDescriptor
{
    /// <summary>
    /// 初始化 Provider 模型描述。
    /// Initializes a provider model descriptor.
    /// </summary>
    public ProviderModelDescriptor(
        string name,
        string? displayName = null,
        string? family = null,
        ProviderCapabilityProfile? capabilities = null)
    {
        Name = IdentifierGuard.AgainstNullOrWhiteSpace(name, nameof(name));
        DisplayName = displayName;
        Family = family;
        Capabilities = capabilities;
    }

    public string Name { get; }

    public string? DisplayName { get; }

    public string? Family { get; }

    public ProviderCapabilityProfile? Capabilities { get; }
}

/// <summary>
/// Provider 对话消息。
/// Provider conversation message.
/// </summary>
public sealed record ProviderConversationMessage
{
    /// <summary>
    /// 初始化 Provider 对话消息。
    /// Initializes a provider conversation message.
    /// </summary>
    public ProviderConversationMessage(string role, IReadOnlyList<ProviderInputItem> content)
    {
        if (content is null || content.Count == 0)
        {
            throw new ArgumentException("Provider 对话消息至少需要一个内容项。", nameof(content));
        }

        Role = IdentifierGuard.AgainstNullOrWhiteSpace(role, nameof(role));
        Content = content;
    }

    public string Role { get; }

    public IReadOnlyList<ProviderInputItem> Content { get; }
}

/// <summary>
/// Provider 工具描述。
/// Provider tool descriptor.
/// </summary>
public sealed record ProviderToolDescriptor(string Name, StructuredValue Schema)
{
    public string Name { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Name, nameof(Name));

    public StructuredValue Schema { get; } = Schema ?? throw new ArgumentNullException(nameof(Schema));
}

/// <summary>
/// Provider canonical 对话请求。
/// Provider canonical conversation request.
/// </summary>
public sealed record ProviderConversationRequest
{
    /// <summary>
    /// 初始化 Provider canonical 对话请求。
    /// Initializes a provider canonical conversation request.
    /// </summary>
    public ProviderConversationRequest(
        string providerId,
        ProviderProtocolKind protocolKind,
        string model,
        IReadOnlyList<ProviderConversationMessage> messages,
        string? systemInstructions = null,
        IReadOnlyList<ProviderToolDescriptor>? tools = null,
        string? toolChoice = null,
        StructuredValue? responseFormat = null,
        StructuredValue? reasoningOptions = null,
        bool stream = true,
        MetadataBag? metadata = null)
    {
        if (messages is null || messages.Count == 0)
        {
            throw new ArgumentException("Provider 对话请求至少需要一条消息。", nameof(messages));
        }

        ProviderId = IdentifierGuard.AgainstNullOrWhiteSpace(providerId, nameof(providerId));
        ProtocolKind = protocolKind;
        Model = IdentifierGuard.AgainstNullOrWhiteSpace(model, nameof(model));
        Messages = messages;
        SystemInstructions = systemInstructions;
        Tools = tools ?? Array.Empty<ProviderToolDescriptor>();
        ToolChoice = toolChoice;
        ResponseFormat = responseFormat;
        ReasoningOptions = reasoningOptions;
        Stream = stream;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string ProviderId { get; }

    public ProviderProtocolKind ProtocolKind { get; }

    public string Model { get; }

    public IReadOnlyList<ProviderConversationMessage> Messages { get; }

    public string? SystemInstructions { get; }

    public IReadOnlyList<ProviderToolDescriptor> Tools { get; }

    public string? ToolChoice { get; }

    public StructuredValue? ResponseFormat { get; }

    public StructuredValue? ReasoningOptions { get; }

    public bool Stream { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Provider 运行期探测结果。
/// Provider runtime probe result.
/// </summary>
public sealed record ProviderProbeResult
{
    /// <summary>
    /// 初始化 Provider 运行期探测结果。
    /// Initializes a provider runtime probe result.
    /// </summary>
    public ProviderProbeResult(
        bool available,
        string providerId,
        ProviderProtocolKind protocolKind,
        string? model = null,
        string? endpoint = null,
        string? reason = null,
        DateTimeOffset? probedAt = null)
    {
        Available = available;
        ProviderId = IdentifierGuard.AgainstNullOrWhiteSpace(providerId, nameof(providerId));
        ProtocolKind = protocolKind;
        Model = model;
        Endpoint = endpoint;
        Reason = reason;
        ProbedAt = probedAt;
    }

    public bool Available { get; }

    public string ProviderId { get; }

    public ProviderProtocolKind ProtocolKind { get; }

    public string? Model { get; }

    public string? Endpoint { get; }

    public string? Reason { get; }

    public DateTimeOffset? ProbedAt { get; }
}

/// <summary>
/// Provider 调用请求。
/// Provider invocation request.
/// </summary>
public sealed record ProviderInvocationRequest
{
    /// <summary>
    /// 初始化 Provider 调用请求。
    /// Initializes a provider-invocation request.
    /// </summary>
    public ProviderInvocationRequest(
        ExecutionId executionId,
        string providerKey,
        string model,
        ProviderConversationContext conversation,
        IReadOnlyList<ProviderInputItem> inputs,
        ProviderTurnState? previousTurnState = null,
        MetadataBag? metadata = null,
        ProviderInvocationContext? invocationContext = null)
    {
        if (inputs is null || inputs.Count == 0)
        {
            throw new ArgumentException("Provider 调用至少需要一个输入项。", nameof(inputs));
        }

        ExecutionId = executionId;
        ProviderKey = IdentifierGuard.AgainstNullOrWhiteSpace(providerKey, nameof(providerKey));
        Model = IdentifierGuard.AgainstNullOrWhiteSpace(model, nameof(model));
        Conversation = conversation ?? throw new ArgumentNullException(nameof(conversation));
        Inputs = inputs;
        PreviousTurnState = previousTurnState;
        Metadata = metadata ?? MetadataBag.Empty;
        InvocationContext = invocationContext;
    }

    public ExecutionId ExecutionId { get; }

    public string ProviderKey { get; }

    public string Model { get; }

    public ProviderConversationContext Conversation { get; }

    public IReadOnlyList<ProviderInputItem> Inputs { get; }

    public ProviderTurnState? PreviousTurnState { get; }

    public MetadataBag Metadata { get; }

    public ProviderInvocationContext? InvocationContext { get; }
}

/// <summary>
/// Provider 调用上下文，承载 Execution Runtime 批准后的来源追踪与权限边界。
/// Provider invocation context carrying source tracing and permission boundary approved by Execution Runtime.
/// </summary>
public sealed record ProviderInvocationContext
{
    public ProviderInvocationContext(
        string runtimeStepId,
        string sourceIntentId,
        string sourceGraphId,
        string sourceStageId,
        string sourceKernelOperationId,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        MetadataBag? metadata = null)
    {
        RuntimeStepId = IdentifierGuard.AgainstNullOrWhiteSpace(runtimeStepId, nameof(runtimeStepId));
        SourceIntentId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceIntentId, nameof(sourceIntentId));
        SourceGraphId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceGraphId, nameof(sourceGraphId));
        SourceStageId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceStageId, nameof(sourceStageId));
        SourceKernelOperationId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceKernelOperationId, nameof(sourceKernelOperationId));
        Permission = permission ?? throw new ArgumentNullException(nameof(permission));
        SideEffect = sideEffect ?? throw new ArgumentNullException(nameof(sideEffect));
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string RuntimeStepId { get; }

    public string SourceIntentId { get; }

    public string SourceGraphId { get; }

    public string SourceStageId { get; }

    public string SourceKernelOperationId { get; }

    public PermissionEnvelope Permission { get; }

    public SideEffectProfile SideEffect { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Provider 工具指令。
/// Provider tool directive.
/// </summary>
public sealed record ProviderToolDirective
{
    /// <summary>
    /// 初始化 Provider 工具指令。
    /// Initializes a provider tool directive.
    /// </summary>
    public ProviderToolDirective(CallId callId, string toolKey, StructuredValue input, bool requiresApproval = false)
    {
        CallId = callId;
        ToolKey = IdentifierGuard.AgainstNullOrWhiteSpace(toolKey, nameof(toolKey));
        Input = input ?? throw new ArgumentNullException(nameof(input));
        RequiresApproval = requiresApproval;
    }

    public CallId CallId { get; }

    public string ToolKey { get; }

    public StructuredValue Input { get; }

    public bool RequiresApproval { get; }
}

/// <summary>
/// Provider 工具输出增量。
/// Provider tool-output delta.
/// </summary>
public sealed record ProviderToolOutputDelta
{
    /// <summary>
    /// 初始化 Provider 工具输出增量。
    /// Initializes a provider tool-output delta.
    /// </summary>
    public ProviderToolOutputDelta(
        CallId callId,
        string toolKey,
        string? outputText,
        StructuredValue? input = null,
        bool requiresApproval = false)
    {
        CallId = callId;
        ToolKey = IdentifierGuard.AgainstNullOrWhiteSpace(toolKey, nameof(toolKey));
        OutputText = outputText;
        Input = input;
        RequiresApproval = requiresApproval;
    }

    public CallId CallId { get; }

    public string ToolKey { get; }

    public string? OutputText { get; }

    public StructuredValue? Input { get; }

    public bool RequiresApproval { get; }
}

/// <summary>
/// Provider 工具终态结果。
/// Provider terminal tool result.
/// </summary>
public sealed record ProviderToolResult
{
    /// <summary>
    /// 初始化 Provider 工具终态结果。
    /// Initializes a provider terminal tool result.
    /// </summary>
    public ProviderToolResult(
        CallId callId,
        string toolKey,
        StructuredValue? input = null,
        StructuredValue? output = null,
        string? outputText = null,
        bool requiresApproval = false)
    {
        CallId = callId;
        ToolKey = IdentifierGuard.AgainstNullOrWhiteSpace(toolKey, nameof(toolKey));
        Input = input;
        Output = output;
        OutputText = outputText;
        RequiresApproval = requiresApproval;
    }

    public CallId CallId { get; }

    public string ToolKey { get; }

    public StructuredValue? Input { get; }

    public StructuredValue? Output { get; }

    public string? OutputText { get; }

    public bool RequiresApproval { get; }
}

/// <summary>
/// Provider 使用量快照。
/// Provider usage snapshot.
/// </summary>
public sealed record ProviderUsage(int InputTokens, int OutputTokens, int? ReasoningTokens = null);

/// <summary>
/// Provider 完成结果。
/// Provider completion result.
/// </summary>
public sealed record ProviderCompletion(
    string OutputText,
    ProviderUsage? Usage = null,
    ProviderTurnState? TurnState = null)
{
    public string OutputText { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(OutputText, nameof(OutputText));
}

/// <summary>
/// Provider 失败模型。
/// Provider failure model.
/// </summary>
public sealed record ProviderFailure
{
    /// <summary>
    /// 初始化 Provider 失败模型。
    /// Initializes a provider failure model.
    /// </summary>
    public ProviderFailure(
        string code,
        string message,
        bool isRetryable = false,
        string? additionalDetails = null,
        ProviderTurnState? turnState = null)
    {
        Code = IdentifierGuard.AgainstNullOrWhiteSpace(code, nameof(code));
        Message = IdentifierGuard.AgainstNullOrWhiteSpace(message, nameof(message));
        IsRetryable = isRetryable;
        AdditionalDetails = additionalDetails;
        TurnState = turnState;
    }

    public string Code { get; }

    public string Message { get; }

    public bool IsRetryable { get; }

    /// <summary>
    /// Provider 附加失败细节，例如 stderr 摘要或 transport 诊断信息。
    /// Optional provider failure details such as stderr summaries or transport diagnostics.
    /// </summary>
    public string? AdditionalDetails { get; }

    public ProviderTurnState? TurnState { get; }
}

/// <summary>
/// Provider 流式事件基类。
/// Base type for provider stream events.
/// </summary>
public abstract record ProviderStreamEvent(string Kind);

/// <summary>
/// 文本增量事件。
/// Text-delta event.
/// </summary>
public sealed record ProviderTextDeltaEvent(string TextDelta) : ProviderStreamEvent("text_delta")
{
    public string TextDelta { get; } = TextDelta ?? throw new ArgumentNullException(nameof(TextDelta));
}

/// <summary>
/// 推理增量事件。
/// Reasoning-delta event.
/// </summary>
public sealed record ProviderReasoningDeltaEvent(string TextDelta) : ProviderStreamEvent("reasoning_delta")
{
    public string TextDelta { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(TextDelta, nameof(TextDelta));
}

/// <summary>
/// 工具指令事件。
/// Tool-directive event.
/// </summary>
public sealed record ProviderToolDirectiveEvent(ProviderToolDirective Directive) : ProviderStreamEvent("tool_directive")
{
    public ProviderToolDirective Directive { get; } = Directive ?? throw new ArgumentNullException(nameof(Directive));
}

/// <summary>
/// Provider 请求工具面事件，仅用于审计本次调用暴露的工具名集合。
/// Provider request tool-surface event used only to audit the tool names exposed for this invocation.
/// </summary>
public sealed record ProviderToolSurfaceEvent(IReadOnlyList<string> ToolNames, string WireApi) : ProviderStreamEvent("tool_surface")
{
    public IReadOnlyList<string> ToolNames { get; } = ToolNames ?? throw new ArgumentNullException(nameof(ToolNames));

    public string WireApi { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(WireApi, nameof(WireApi));
}

/// <summary>
/// 工具输出增量事件。
/// Tool-output-delta event.
/// </summary>
public sealed record ProviderToolOutputDeltaEvent(ProviderToolOutputDelta Delta) : ProviderStreamEvent("tool_output_delta")
{
    public ProviderToolOutputDelta Delta { get; } = Delta ?? throw new ArgumentNullException(nameof(Delta));
}

/// <summary>
/// 工具终态结果事件。
/// Terminal tool-result event.
/// </summary>
public sealed record ProviderToolResultEvent(ProviderToolResult Result) : ProviderStreamEvent("tool_result")
{
    public ProviderToolResult Result { get; } = Result ?? throw new ArgumentNullException(nameof(Result));
}

/// <summary>
/// 完成事件。
/// Completion event.
/// </summary>
public sealed record ProviderCompletionEvent(ProviderCompletion Completion) : ProviderStreamEvent("completion")
{
    public ProviderCompletion Completion { get; } = Completion ?? throw new ArgumentNullException(nameof(Completion));
}

/// <summary>
/// 失败事件。
/// Failure event.
/// </summary>
public sealed record ProviderFailureEvent(ProviderFailure Failure) : ProviderStreamEvent("failure")
{
    public ProviderFailure Failure { get; } = Failure ?? throw new ArgumentNullException(nameof(Failure));
}
