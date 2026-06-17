using System.Text.Json;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Tools;

/// <summary>
/// Tool 模块 manifest；声明工具公开接入面，不包含宿主私有 handler 或运行时实现。
/// Tool module manifest; declares the public access surface without host-private handlers or runtime implementations.
/// </summary>
public sealed record ToolModuleManifest
{
    public ToolModuleManifest(
        string moduleId,
        string displayName,
        string version,
        string minimumTianShuVersion,
        IReadOnlyList<ToolModuleToolBinding> tools,
        IReadOnlyList<string>? diagnostics = null)
    {
        ModuleId = IdentifierGuard.AgainstNullOrWhiteSpace(moduleId, nameof(moduleId));
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        Version = IdentifierGuard.AgainstNullOrWhiteSpace(version, nameof(version));
        MinimumTianShuVersion = IdentifierGuard.AgainstNullOrWhiteSpace(minimumTianShuVersion, nameof(minimumTianShuVersion));
        Tools = tools is { Count: > 0 } ? tools : throw new ArgumentException("Tool module manifest requires at least one tool binding.", nameof(tools));
        Diagnostics = diagnostics ?? Array.Empty<string>();
    }

    public string ModuleId { get; }

    public string DisplayName { get; }

    public string Version { get; }

    public string MinimumTianShuVersion { get; }

    public IReadOnlyList<ToolModuleToolBinding> Tools { get; }

    public IReadOnlyList<string> Diagnostics { get; }
}

/// <summary>
/// Tool 模块中的单个工具绑定声明。
/// Single tool binding declaration inside a Tool module.
/// </summary>
public sealed record ToolModuleToolBinding
{
    public ToolModuleToolBinding(
        string toolKey,
        string displayName,
        string description,
        ToolKind kind = ToolKind.Capability,
        JsonElement? inputSchema = null,
        JsonElement? outputSchema = null,
        ToolCustomInputDefinition? customInputDefinition = null,
        JsonSchemaRef? inputSchemaRef = null,
        JsonSchemaRef? outputSchemaRef = null,
        PermissionDeclaration? permission = null,
        SideEffectProfile? sideEffects = null,
        ToolApprovalRequirement approvalRequirement = ToolApprovalRequirement.None,
        ToolConcurrencyClass concurrencyClass = ToolConcurrencyClass.Sequential,
        ToolImplementationBinding? implementationBinding = null,
        bool requiresHumanGate = false,
        bool enabled = true,
        IReadOnlyList<string>? diagnostics = null)
    {
        ToolKey = IdentifierGuard.AgainstNullOrWhiteSpace(toolKey, nameof(toolKey));
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        Description = IdentifierGuard.AgainstNullOrWhiteSpace(description, nameof(description));
        Kind = kind;
        InputSchema = CloneNullable(inputSchema);
        OutputSchema = CloneNullable(outputSchema);
        CustomInputDefinition = customInputDefinition;
        InputSchemaRef = inputSchemaRef;
        OutputSchemaRef = outputSchemaRef;
        Permission = permission ?? new PermissionDeclaration(requiredScopes: [$"tool.{NormalizePolicyToken(toolKey)}"], requiresHumanGate: requiresHumanGate);
        SideEffects = sideEffects ?? new SideEffectProfile(SideEffectLevel.Unspecified);
        ApprovalRequirement = approvalRequirement;
        ConcurrencyClass = concurrencyClass;
        ImplementationBinding = implementationBinding;
        RequiresHumanGate = requiresHumanGate;
        Enabled = enabled;
        Diagnostics = diagnostics ?? Array.Empty<string>();
    }

    public string ToolKey { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public ToolKind Kind { get; }

    public JsonElement? InputSchema { get; }

    public JsonElement? OutputSchema { get; }

    public ToolCustomInputDefinition? CustomInputDefinition { get; }

    public JsonSchemaRef? InputSchemaRef { get; }

    public JsonSchemaRef? OutputSchemaRef { get; }

    public PermissionDeclaration Permission { get; }

    public SideEffectProfile SideEffects { get; }

    public ToolApprovalRequirement ApprovalRequirement { get; }

    public ToolConcurrencyClass ConcurrencyClass { get; }

    public ToolImplementationBinding? ImplementationBinding { get; }

    public bool RequiresHumanGate { get; }

    public bool Enabled { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    private static JsonElement? CloneNullable(JsonElement? element)
        => element.HasValue ? element.Value.Clone() : null;

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
/// Tool 模块公开接入描述；Runtime binding 只能消费 validated access。
/// Tool module public access descriptor; Runtime binding can consume only validated access.
/// </summary>
public sealed record ToolModuleAccessDescriptor
{
    public ToolModuleAccessDescriptor(
        ToolModuleManifest manifest,
        IReadOnlyList<ToolDescriptor> tools,
        GovernanceEnvelope governance)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Tools = tools is { Count: > 0 } ? tools : throw new ArgumentException("Tool module access requires at least one validated tool.", nameof(tools));
        Governance = governance ?? throw new ArgumentNullException(nameof(governance));
    }

    public ToolModuleManifest Manifest { get; }

    public IReadOnlyList<ToolDescriptor> Tools { get; }

    public GovernanceEnvelope Governance { get; }
}

/// <summary>
/// Tool 模块公开接入校验结果。
/// Tool module public access validation result.
/// </summary>
public sealed record ToolModuleAccessValidationResult(
    ToolModuleAccessDescriptor? Access,
    IReadOnlyList<ToolModuleAccessIssue> Issues)
{
    public bool IsValid => Access is not null && Issues.All(static issue => issue.Severity != ToolModuleAccessIssueSeverity.Error);
}

/// <summary>
/// Tool 模块公开接入问题严重性。
/// Tool module public access issue severity.
/// </summary>
public enum ToolModuleAccessIssueSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>
/// Tool 模块公开接入问题。
/// Tool module public access issue.
/// </summary>
public sealed record ToolModuleAccessIssue(
    string Code,
    string Message,
    ToolModuleAccessIssueSeverity Severity = ToolModuleAccessIssueSeverity.Error)
{
    public string Code { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Code, nameof(Code));

    public string Message { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Message, nameof(Message));
}

/// <summary>
/// Tool 结果状态。
/// Tool result status.
/// </summary>
public enum ToolModuleResultStatus
{
    Succeeded = 0,
    Failed = 1,
    Blocked = 2,
    Cancelled = 3,
    ApprovalRequired = 4,
    Timeout = 5,
}

/// <summary>
/// Tool 模块结果投影；面向 provider follow-up、诊断和 replay。
/// Tool module result projection for provider follow-up, diagnostics, and replay.
/// </summary>
public sealed record ToolModuleResultProjection
{
    public ToolModuleResultProjection(
        CallId callId,
        string toolKey,
        ToolModuleResultStatus status,
        bool success,
        string outputText,
        JsonElement structuredOutput,
        ToolInvocationFailure? failure = null,
        IReadOnlyList<ToolOutputContentItem>? outputContentItems = null,
        IReadOnlyList<JsonElement>? rawOutputContentItems = null,
        IReadOnlyList<string>? diagnosticsRefs = null)
    {
        CallId = callId;
        ToolKey = IdentifierGuard.AgainstNullOrWhiteSpace(toolKey, nameof(toolKey));
        Status = status;
        Success = success;
        OutputText = outputText ?? string.Empty;
        StructuredOutput = structuredOutput.Clone();
        Failure = failure;
        OutputContentItems = outputContentItems ?? Array.Empty<ToolOutputContentItem>();
        RawOutputContentItems = rawOutputContentItems is null
            ? Array.Empty<JsonElement>()
            : rawOutputContentItems.Select(static item => item.Clone()).ToArray();
        DiagnosticsRefs = diagnosticsRefs ?? Array.Empty<string>();
    }

    public CallId CallId { get; }

    public string ToolKey { get; }

    public ToolModuleResultStatus Status { get; }

    public bool Success { get; }

    public string OutputText { get; }

    public JsonElement StructuredOutput { get; }

    public ToolInvocationFailure? Failure { get; }

    public IReadOnlyList<ToolOutputContentItem> OutputContentItems { get; }

    public IReadOnlyList<JsonElement> RawOutputContentItems { get; }

    public IReadOnlyList<string> DiagnosticsRefs { get; }
}

/// <summary>
/// Tool 模块公开接入校验器。
/// Tool module public access validator.
/// </summary>
public static class ToolModuleAccessValidator
{
    public static ToolModuleAccessValidationResult Validate(
        ToolModuleManifest manifest,
        IReadOnlyList<ToolDescriptor> descriptors,
        GovernanceEnvelope governance)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(governance);

        var issues = new List<ToolModuleAccessIssue>();
        if (descriptors.Count == 0)
        {
            issues.Add(Error("tool_access.descriptor_missing", "Tool module access requires at least one ToolDescriptor."));
        }

        if (!governance.AllowedModuleIds.Contains(manifest.ModuleId, StringComparer.Ordinal))
        {
            issues.Add(Error("tool_access.module_not_allowed", "GovernanceEnvelope 未允许当前 Tool module。"));
        }

        var descriptorByKey = new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            if (!descriptorByKey.TryAdd(descriptor.ToolId, descriptor))
            {
                issues.Add(Error("tool_access.duplicate_descriptor", $"ToolDescriptor 重复：{descriptor.ToolId}。"));
            }
        }

        var bindingKeys = new HashSet<string>(StringComparer.Ordinal);
        var validatedTools = new List<ToolDescriptor>();
        foreach (var binding in manifest.Tools.Where(static tool => tool.Enabled))
        {
            if (!bindingKeys.Add(binding.ToolKey))
            {
                issues.Add(Error("tool_access.duplicate_binding", $"Tool binding 重复：{binding.ToolKey}。"));
                continue;
            }

            if (!descriptorByKey.TryGetValue(binding.ToolKey, out var descriptor))
            {
                issues.Add(Error("tool_access.descriptor_not_found", $"Tool binding 缺少匹配 ToolDescriptor：{binding.ToolKey}。"));
                continue;
            }

            ValidateBinding(binding, descriptor, governance, issues);
            validatedTools.Add(descriptor);
        }

        if (validatedTools.Count == 0)
        {
            issues.Add(Error("tool_access.no_enabled_tool", "Tool module manifest 没有可用工具。"));
        }

        if (issues.Any(static issue => issue.Severity == ToolModuleAccessIssueSeverity.Error))
        {
            return new ToolModuleAccessValidationResult(null, issues);
        }

        return new ToolModuleAccessValidationResult(
            new ToolModuleAccessDescriptor(manifest, validatedTools, governance),
            issues);
    }

    public static ToolModuleResultProjection ProjectResult(
        ToolInvocationResult result,
        IReadOnlyList<string>? diagnosticsRefs = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var projection = ToolInvocationResultProjector.Project(result);
        return new ToolModuleResultProjection(
            result.CallId,
            result.ToolKey,
            result.Failure is null ? ToolModuleResultStatus.Succeeded : ToolModuleResultStatus.Failed,
            projection.Success,
            projection.OutputText,
            projection.StructuredOutput,
            projection.Failure,
            projection.OutputContentItems,
            projection.RawOutputContentItems,
            diagnosticsRefs);
    }

    public static ToolModuleResultProjection ProjectBlockedResult(
        CallId callId,
        string toolKey,
        string code,
        string message,
        ToolModuleResultStatus status = ToolModuleResultStatus.Blocked,
        IReadOnlyList<string>? diagnosticsRefs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (status == ToolModuleResultStatus.Succeeded)
        {
            throw new ArgumentException("Blocked result status cannot be Succeeded.", nameof(status));
        }

        var failure = new ToolInvocationFailure(code, message);
        var structuredOutput = JsonSerializer.SerializeToElement(new
        {
            callId = callId.Value,
            toolKey,
            status = status.ToString(),
            failure = new
            {
                code,
                message,
            },
        });

        return new ToolModuleResultProjection(
            callId,
            toolKey,
            status,
            success: false,
            outputText: message,
            structuredOutput,
            failure,
            diagnosticsRefs: diagnosticsRefs);
    }

    private static void ValidateBinding(
        ToolModuleToolBinding binding,
        ToolDescriptor descriptor,
        GovernanceEnvelope governance,
        List<ToolModuleAccessIssue> issues)
    {
        if (binding.Kind != descriptor.Kind)
        {
            issues.Add(Error("tool_access.kind_mismatch", $"Tool kind 不一致：{binding.ToolKey}。"));
        }

        if (!HasInputSchema(binding, descriptor))
        {
            issues.Add(Error("tool_access.schema_missing", $"Tool 缺少 input schema：{binding.ToolKey}。"));
        }

        if (binding.SideEffects.Level == SideEffectLevel.Unspecified || descriptor.SideEffects.Level == SideEffectLevel.Unspecified)
        {
            issues.Add(Error("tool_access.side_effect_unspecified", $"Tool 副作用等级未声明：{binding.ToolKey}。"));
        }
        else if (binding.SideEffects.Level < descriptor.SideEffects.Level)
        {
            issues.Add(Error("tool_access.side_effect_weakened", $"Tool manifest 不得弱化 descriptor 副作用等级：{binding.ToolKey}。"));
        }

        var descriptorRequiresGate = descriptor.Permissions.RequiresHumanGate
                                     || descriptor.ApprovalRequirement == ToolApprovalRequirement.Required;
        if (descriptorRequiresGate && !binding.RequiresHumanGate)
        {
            issues.Add(Error("tool_access.human_gate_weakened", $"Tool manifest 不得关闭 descriptor 要求的 human gate：{binding.ToolKey}。"));
        }

        if (binding.Permission.RequiresHumanGate != binding.RequiresHumanGate)
        {
            issues.Add(Error("tool_access.permission_gate_mismatch", $"Tool binding 的 permission human gate 与绑定声明不一致：{binding.ToolKey}。"));
        }

        if (!descriptor.IsAllowedBy(governance))
        {
            issues.Add(Error("tool_access.governance_denied", $"GovernanceEnvelope 不允许当前 ToolDescriptor：{binding.ToolKey}。"));
        }
    }

    private static bool HasInputSchema(ToolModuleToolBinding binding, ToolDescriptor descriptor)
        => binding.InputSchema.HasValue
           || binding.InputSchemaRef is not null
           || binding.CustomInputDefinition is not null
           || descriptor.InputSchema.HasValue
           || descriptor.InputSchemaRef is not null
           || descriptor.CustomInputDefinition is not null;

    private static ToolModuleAccessIssue Error(string code, string message)
        => new(code, message, ToolModuleAccessIssueSeverity.Error);
}
