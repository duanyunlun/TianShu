using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Catalog;

/// <summary>
/// 控制平面插件目录结果。
/// Control-plane plugin catalog result.
/// </summary>
public sealed record ControlPlanePluginCatalogResult
{
    public IReadOnlyList<ControlPlanePluginMarketplace> Marketplaces { get; init; } = Array.Empty<ControlPlanePluginMarketplace>();

    public string? RemoteSyncError { get; init; }
}

/// <summary>
/// 控制平面插件市场。
/// Control-plane plugin marketplace.
/// </summary>
public sealed record ControlPlanePluginMarketplace
{
    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public IReadOnlyList<ControlPlanePluginSummary> Plugins { get; init; } = Array.Empty<ControlPlanePluginSummary>();
}

/// <summary>
/// 控制平面插件摘要。
/// Control-plane plugin summary.
/// </summary>
public sealed record ControlPlanePluginSummary
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool Installed { get; init; }

    public bool Enabled { get; init; }

    public string InstallPolicy { get; init; } = string.Empty;

    public string AuthPolicy { get; init; } = string.Empty;

    public StructuredValue? Source { get; init; }

    public StructuredValue? Interface { get; init; }
}

/// <summary>
/// 控制平面插件读取结果。
/// Control-plane plugin read result.
/// </summary>
public sealed record ControlPlanePluginReadResult
{
    public ControlPlanePluginDetail? Plugin { get; init; }
}

/// <summary>
/// 控制平面插件详情。
/// Control-plane plugin detail.
/// </summary>
public sealed record ControlPlanePluginDetail
{
    public string MarketplaceName { get; init; } = string.Empty;

    public string MarketplacePath { get; init; } = string.Empty;

    public ControlPlanePluginSummary Summary { get; init; } = new();

    public string? Description { get; init; }

    public IReadOnlyList<ControlPlanePluginSkillReference> Skills { get; init; } = Array.Empty<ControlPlanePluginSkillReference>();

    public IReadOnlyList<ControlPlanePluginAppReference> Apps { get; init; } = Array.Empty<ControlPlanePluginAppReference>();

    public IReadOnlyList<string> McpServers { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 控制平面插件技能引用。
/// Control-plane plugin skill reference.
/// </summary>
public sealed record ControlPlanePluginSkillReference
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string? ShortDescription { get; init; }

    public string Path { get; init; } = string.Empty;

    public StructuredValue? Interface { get; init; }
}

/// <summary>
/// 控制平面插件应用引用。
/// Control-plane plugin app reference.
/// </summary>
public sealed record ControlPlanePluginAppReference
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? InstallUrl { get; init; }
}

/// <summary>
/// 控制平面插件安装结果。
/// Control-plane plugin install result.
/// </summary>
public sealed record ControlPlanePluginInstallResult
{
    public string AuthPolicy { get; init; } = string.Empty;

    public IReadOnlyList<ControlPlanePluginAppReference> AppsNeedingAuth { get; init; } = Array.Empty<ControlPlanePluginAppReference>();
}

/// <summary>
/// 控制平面插件卸载结果。
/// Control-plane plugin uninstall result.
/// </summary>
public sealed record ControlPlanePluginUninstallResult;

/// <summary>
/// 控制平面远程技能目录结果。
/// Control-plane remote skill catalog result.
/// </summary>
public sealed record ControlPlaneRemoteSkillCatalogResult
{
    public IReadOnlyList<ControlPlaneRemoteSkillSummary> Items { get; init; } = Array.Empty<ControlPlaneRemoteSkillSummary>();

    public string? NextCursor { get; init; }
}

/// <summary>
/// 控制平面远程技能摘要。
/// Control-plane remote skill summary.
/// </summary>
public sealed record ControlPlaneRemoteSkillSummary
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string? HazelnutScope { get; init; }
}

/// <summary>
/// 控制平面远程技能导出结果。
/// Control-plane remote skill export result.
/// </summary>
public sealed record ControlPlaneRemoteSkillExportResult
{
    public string Id { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;
}

/// <summary>
/// 控制平面应用目录结果。
/// Control-plane app catalog result.
/// </summary>
public sealed record ControlPlaneAppCatalogResult
{
    public string? NextCursor { get; init; }

    public IReadOnlyList<ControlPlaneAppDescriptor> Items { get; init; } = Array.Empty<ControlPlaneAppDescriptor>();
}

/// <summary>
/// 控制平面应用描述。
/// Control-plane app descriptor.
/// </summary>
public sealed record ControlPlaneAppDescriptor
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? LogoUrl { get; init; }

    public string? LogoUrlDark { get; init; }

    public string? DistributionChannel { get; init; }

    public StructuredValue? Branding { get; init; }

    public StructuredValue? Metadata { get; init; }

    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public string? InstallUrl { get; init; }

    public bool IsAccessible { get; init; }

    public bool IsEnabled { get; init; }

    public IReadOnlyList<string> PluginDisplayNames { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 控制平面实验特性目录结果。
/// Control-plane experimental feature catalog result.
/// </summary>
public sealed record ControlPlaneExperimentalFeatureCatalogResult
{
    public string? NextCursor { get; init; }

    public IReadOnlyList<ControlPlaneExperimentalFeatureDescriptor> Items { get; init; } =
        Array.Empty<ControlPlaneExperimentalFeatureDescriptor>();
}

/// <summary>
/// 控制平面实验特性描述。
/// Control-plane experimental feature descriptor.
/// </summary>
public sealed record ControlPlaneExperimentalFeatureDescriptor
{
    public string Name { get; init; } = string.Empty;

    public string Stage { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public string? Announcement { get; init; }

    public bool Enabled { get; init; }

    public bool DefaultEnabled { get; init; }
}

/// <summary>
/// 控制平面协作模式目录结果。
/// Control-plane collaboration mode catalog result.
/// </summary>
public sealed record ControlPlaneCollaborationModeCatalogResult
{
    public IReadOnlyList<ControlPlaneCollaborationModeDescriptor> Items { get; init; } =
        Array.Empty<ControlPlaneCollaborationModeDescriptor>();
}

/// <summary>
/// 控制平面协作模式描述。
/// Control-plane collaboration mode descriptor.
/// </summary>
public sealed record ControlPlaneCollaborationModeDescriptor
{
    public string Name { get; init; } = string.Empty;

    public string? Mode { get; init; }

    public string? Model { get; init; }

    public string? ReasoningEffort { get; init; }
}

/// <summary>
/// 控制平面 MCP Server 目录结果。
/// Control-plane MCP server catalog result.
/// </summary>
public sealed record ControlPlaneMcpServerCatalogResult
{
    public string? NextCursor { get; init; }

    public IReadOnlyList<ControlPlaneMcpServerDescriptor> Items { get; init; } = Array.Empty<ControlPlaneMcpServerDescriptor>();
}

/// <summary>
/// 控制平面 MCP Server 重新加载结果。
/// Control-plane MCP server reload result.
/// </summary>
public sealed record ControlPlaneMcpServerReloadResult;

/// <summary>
/// 控制平面模型 Provider 包重新加载结果。
/// Control-plane model provider package reload result.
/// </summary>
public sealed record ControlPlaneProviderPackageReloadResult
{
    public int LoadedAssemblyCount { get; init; }

    public int IssueCount { get; init; }

    public IReadOnlyList<string> SupportedProtocolAdapterIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SupportedWireApis { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 控制平面 MCP Server OAuth 登录启动结果。
/// Control-plane MCP server OAuth login start result.
/// </summary>
public sealed record ControlPlaneMcpServerOauthLoginStartResult
{
    public string? AuthorizationUrl { get; init; }
}

/// <summary>
/// 控制平面 MCP Server 描述。
/// Control-plane MCP server descriptor.
/// </summary>
public sealed record ControlPlaneMcpServerDescriptor
{
    public string Name { get; init; } = string.Empty;

    public string AuthStatus { get; init; } = string.Empty;

    public IReadOnlyList<string> ToolNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ResourceUris { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ResourceTemplateUris { get; init; } = Array.Empty<string>();
}
