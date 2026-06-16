using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Catalog;

/// <summary>
/// 控制平面配置快照结果。
/// Control-plane config snapshot result.
/// </summary>
public sealed record ControlPlaneConfigSnapshotResult
{
    public StructuredValue? Config { get; init; }

    public IReadOnlyDictionary<string, ControlPlaneConfigOrigin> Origins { get; init; } =
        new Dictionary<string, ControlPlaneConfigOrigin>(StringComparer.Ordinal);

    public IReadOnlyList<ControlPlaneConfigField> Fields { get; init; } = Array.Empty<ControlPlaneConfigField>();

    public IReadOnlyList<ControlPlaneConfigLayer> Layers { get; init; } = Array.Empty<ControlPlaneConfigLayer>();
}

/// <summary>
/// 控制平面配置来源。
/// Control-plane config origin.
/// </summary>
public sealed record ControlPlaneConfigOrigin
{
    public string? Type { get; init; }

    public string? File { get; init; }

    public string? DotTianShuFolder { get; init; }

    public string? Version { get; init; }
}

/// <summary>
/// 控制平面配置字段。
/// Control-plane config field.
/// </summary>
public sealed record ControlPlaneConfigField
{
    public string KeyPath { get; init; } = string.Empty;

    public string ValueKind { get; init; } = string.Empty;

    public string ValueText { get; init; } = string.Empty;

    public StructuredValue? Value { get; init; }

    public string SourceType { get; init; } = string.Empty;

    public string SourcePath { get; init; } = string.Empty;

    public string SourceText { get; init; } = string.Empty;
}

/// <summary>
/// 控制平面配置层。
/// Control-plane config layer.
/// </summary>
public sealed record ControlPlaneConfigLayer
{
    public StructuredValue? Name { get; init; }

    public string Version { get; init; } = string.Empty;

    public StructuredValue? Config { get; init; }

    public string? DisabledReason { get; init; }
}

/// <summary>
/// 控制平面配置要求结果。
/// Control-plane config requirements result.
/// </summary>
public sealed record ControlPlaneConfigRequirementsResult
{
    public bool IsDefined { get; init; }

    public IReadOnlyList<string> AllowedApprovalPolicies { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AllowedSandboxModes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AllowedWebSearchModes { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, bool> FeatureRequirements { get; init; } =
        new Dictionary<string, bool>(StringComparer.Ordinal);

    public string? EnforceResidency { get; init; }

    public ControlPlaneConfigRequirementsNetwork? Network { get; init; }
}

/// <summary>
/// 控制平面网络配置要求。
/// Control-plane network config requirements.
/// </summary>
public sealed record ControlPlaneConfigRequirementsNetwork
{
    public bool? Enabled { get; init; }

    public ushort? HttpPort { get; init; }

    public ushort? SocksPort { get; init; }

    public bool? AllowUpstreamProxy { get; init; }

    public bool? DangerouslyAllowNonLoopbackProxy { get; init; }

    public bool? DangerouslyAllowNonLoopbackAdmin { get; init; }

    public bool? DangerouslyAllowAllUnixSockets { get; init; }

    public IReadOnlyList<string> AllowedDomains { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DeniedDomains { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AllowUnixSockets { get; init; } = Array.Empty<string>();

    public bool? AllowLocalBinding { get; init; }
}

/// <summary>
/// 控制平面配置写入结果。
/// Control-plane config write result.
/// </summary>
public sealed record ControlPlaneConfigWriteResult
{
    public string Status { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string FilePath { get; init; } = string.Empty;

    public bool IsOverridden { get; init; }

    public ControlPlaneConfigWriteOverride? OverriddenMetadata { get; init; }
}

/// <summary>
/// 控制平面配置覆盖信息。
/// Control-plane config override metadata.
/// </summary>
public sealed record ControlPlaneConfigWriteOverride
{
    public string Message { get; init; } = string.Empty;

    public ControlPlaneConfigOrigin? OverridingLayer { get; init; }

    public StructuredValue? EffectiveValue { get; init; }
}

/// <summary>
/// 控制平面模型目录结果。
/// Control-plane model catalog result.
/// </summary>
public sealed record ControlPlaneModelCatalogResult
{
    public string? NextCursor { get; init; }

    public IReadOnlyList<ControlPlaneModelCatalogItem> Items { get; init; } = Array.Empty<ControlPlaneModelCatalogItem>();
}

/// <summary>
/// 控制平面模型目录项。
/// Control-plane model catalog item.
/// </summary>
public sealed record ControlPlaneModelCatalogItem
{
    public string Id { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string DefaultReasoningEffort { get; init; } = string.Empty;

    public IReadOnlyList<string> SupportedReasoningEfforts { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> InputModalities { get; init; } = Array.Empty<string>();

    public bool SupportsPersonality { get; init; }

    public bool Hidden { get; init; }

    public bool IsDefault { get; init; }

    public bool SupportsParallelToolCalls { get; init; }

    public bool SupportsReasoningSummaries { get; init; }

    public string? DefaultReasoningSummary { get; init; }

    public bool SupportsVerbosity { get; init; }

    public string? DefaultVerbosity { get; init; }

    public bool PreferWebsocketTransport { get; init; }

    public string Description { get; init; } = string.Empty;

    public string? AvailabilityNuxMessage { get; init; }

    public string? UpgradeModel { get; init; }

    public string? UpgradeMigrationMarkdown { get; init; }
}

/// <summary>
/// 控制平面技能目录结果。
/// Control-plane skill catalog result.
/// </summary>
public sealed record ControlPlaneSkillCatalogResult
{
    public IReadOnlyList<ControlPlaneSkillCatalogEntry> Entries { get; init; } = Array.Empty<ControlPlaneSkillCatalogEntry>();
}

/// <summary>
/// 控制平面技能目录项。
/// Control-plane skill catalog entry.
/// </summary>
public sealed record ControlPlaneSkillCatalogEntry
{
    public string WorkingDirectory { get; init; } = string.Empty;

    public IReadOnlyList<ControlPlaneSkillDescriptor> Skills { get; init; } = Array.Empty<ControlPlaneSkillDescriptor>();

    public IReadOnlyList<ControlPlaneSkillError> Errors { get; init; } = Array.Empty<ControlPlaneSkillError>();
}

/// <summary>
/// 控制平面技能描述。
/// Control-plane skill descriptor.
/// </summary>
public sealed record ControlPlaneSkillDescriptor
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string? ShortDescription { get; init; }

    public string PathToSkillsMd { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string Scope { get; init; } = string.Empty;

    public bool Enabled { get; init; }

    public StructuredValue? Interface { get; init; }

    public StructuredValue? Dependencies { get; init; }

    public StructuredValue? PermissionProfile { get; init; }

    public StructuredValue? ManagedNetworkOverride { get; init; }
}

/// <summary>
/// 控制平面技能错误。
/// Control-plane skill error.
/// </summary>
public sealed record ControlPlaneSkillError
{
    public string Path { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// 控制平面技能配置写入结果。
/// Control-plane skill config write result.
/// </summary>
public sealed record ControlPlaneSkillConfigWriteResult
{
    public bool EffectiveEnabled { get; init; }
}
