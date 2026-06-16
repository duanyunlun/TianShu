namespace TianShu.AppHost.Configuration;

/// <summary>
/// 托管网络能力的 requirements 配置载体。
/// Requirements carrier for managed-network configuration constraints.
/// </summary>
internal sealed record KernelManagedNetworkRequirements(
    bool? Enabled,
    int? HttpPort,
    int? SocksPort,
    bool? AllowUpstreamProxy,
    bool? DangerouslyAllowNonLoopbackProxy,
    bool? DangerouslyAllowNonLoopbackAdmin,
    bool? DangerouslyAllowAllUnixSockets,
    IReadOnlyList<string>? AllowedDomains,
    IReadOnlyList<string>? DeniedDomains,
    IReadOnlyList<string>? AllowUnixSockets,
    bool? AllowLocalBinding);

/// <summary>
/// 已按优先级合并后的 requirements 快照。
/// Merged requirements snapshot after precedence resolution.
/// </summary>
internal sealed record KernelMergedConfigRequirements(
    IReadOnlyList<string>? AllowedApprovalPolicies,
    IReadOnlyList<string>? AllowedSandboxModes,
    IReadOnlyList<string>? AllowedWebSearchModes,
    IReadOnlyDictionary<string, bool>? FeatureRequirements,
    string? EnforceResidency,
    KernelManagedNetworkRequirements? Network);

/// <summary>
/// 从单个来源解析出的 requirements 载体。
/// Parsed requirements carrier from one source.
/// </summary>
internal sealed record KernelParsedRequirements(
    List<string>? AllowedApprovalPolicies,
    List<string>? AllowedSandboxModes,
    List<string>? AllowedWebSearchModes,
    Dictionary<string, bool>? FeatureRequirements,
    string? EnforceResidency,
    KernelManagedNetworkRequirements? Network);

/// <summary>
/// requirements 合并状态。
/// Merge state that keeps the highest-precedence value for each requirements field.
/// </summary>
internal sealed class KernelRequirementsMergeState
{
    private bool hasAllowedApprovalPolicies;
    private bool hasAllowedSandboxModes;
    private bool hasAllowedWebSearchModes;
    private bool hasFeatureRequirements;
    private bool hasEnforceResidency;
    private bool hasNetwork;

    public List<string>? AllowedApprovalPolicies { get; private set; }

    public List<string>? AllowedSandboxModes { get; private set; }

    public List<string>? AllowedWebSearchModes { get; private set; }

    public Dictionary<string, bool>? FeatureRequirements { get; private set; }

    public string? EnforceResidency { get; private set; }

    public KernelManagedNetworkRequirements? Network { get; private set; }

    public void Merge(KernelParsedRequirements? requirements)
    {
        if (requirements is null)
        {
            return;
        }

        if (!hasAllowedApprovalPolicies && requirements.AllowedApprovalPolicies is not null)
        {
            hasAllowedApprovalPolicies = true;
            AllowedApprovalPolicies = requirements.AllowedApprovalPolicies;
        }

        if (!hasAllowedSandboxModes && requirements.AllowedSandboxModes is not null)
        {
            hasAllowedSandboxModes = true;
            AllowedSandboxModes = requirements.AllowedSandboxModes;
        }

        if (!hasAllowedWebSearchModes && requirements.AllowedWebSearchModes is not null)
        {
            hasAllowedWebSearchModes = true;
            AllowedWebSearchModes = requirements.AllowedWebSearchModes;
        }

        if (!hasFeatureRequirements && requirements.FeatureRequirements is not null)
        {
            hasFeatureRequirements = true;
            FeatureRequirements = requirements.FeatureRequirements;
        }

        if (!hasEnforceResidency && requirements.EnforceResidency is not null)
        {
            hasEnforceResidency = true;
            EnforceResidency = requirements.EnforceResidency;
        }

        if (!hasNetwork && requirements.Network is not null)
        {
            hasNetwork = true;
            Network = requirements.Network;
        }
    }

    public KernelMergedConfigRequirements Build()
    {
        var allowedWebSearchModes = AllowedWebSearchModes is null
            ? null
            : new List<string>(AllowedWebSearchModes);
        if (allowedWebSearchModes is not null
            && !allowedWebSearchModes.Contains("disabled", StringComparer.OrdinalIgnoreCase))
        {
            allowedWebSearchModes.Add("disabled");
        }

        return new KernelMergedConfigRequirements(
            AllowedApprovalPolicies?.ToArray(),
            AllowedSandboxModes?.ToArray(),
            allowedWebSearchModes?.ToArray(),
            FeatureRequirements is null
                ? null
                : new Dictionary<string, bool>(FeatureRequirements, StringComparer.OrdinalIgnoreCase),
            EnforceResidency,
            Network);
    }
}
