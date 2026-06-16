using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Catalog;

/// <summary>
/// 控制平面配置读取查询。
/// Control-plane query that reads config.
/// </summary>
public sealed record ControlPlaneConfigReadQuery
{
    public string? WorkingDirectory { get; init; }

    public bool IncludeLayers { get; init; }
}

/// <summary>
/// 控制平面配置要求读取查询。
/// Control-plane query that reads config requirements.
/// </summary>
public sealed record ControlPlaneConfigRequirementsQuery
{
    public string? WorkingDirectory { get; init; }
}

/// <summary>
/// 控制平面模型目录查询。
/// Control-plane query that lists models.
/// </summary>
public sealed record ControlPlaneModelCatalogQuery
{
    public int Limit { get; init; } = 50;

    public string? Cursor { get; init; }

    public bool IncludeHidden { get; init; }

    /// <summary>
    /// 要求模型目录必须来自当前 provider endpoint，不允许回退内置离线目录。
    /// Requires the model catalog to come from the current provider endpoint without falling back to the bundled offline catalog.
    /// </summary>
    public bool RequireEndpoint { get; init; }
}

/// <summary>
/// 控制平面技能目录查询。
/// Control-plane query that lists skills.
/// </summary>
public sealed record ControlPlaneSkillCatalogQuery
{
    public IReadOnlyList<string> WorkingDirectories { get; init; } = Array.Empty<string>();

    public bool ForceReload { get; init; }

    public IReadOnlyList<ControlPlaneSkillsExtraRootsForWorkingDirectory> ExtraRootsByWorkingDirectory { get; init; } =
        Array.Empty<ControlPlaneSkillsExtraRootsForWorkingDirectory>();
}

/// <summary>
/// 控制平面远程技能目录查询。
/// Control-plane query that lists remote skills.
/// </summary>
public sealed record ControlPlaneRemoteSkillCatalogQuery
{
    public string? HazelnutScope { get; init; }

    public string? ProductSurface { get; init; }

    public bool? Enabled { get; init; }
}

/// <summary>
/// 工作目录附加技能根目录。
/// Extra skill roots for a working directory.
/// </summary>
public sealed record ControlPlaneSkillsExtraRootsForWorkingDirectory
{
    public string WorkingDirectory { get; init; } = string.Empty;

    public IReadOnlyList<string> ExtraUserRoots { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 控制平面插件目录查询。
/// Control-plane query that lists plugins.
/// </summary>
public sealed record ControlPlanePluginCatalogQuery
{
    public IReadOnlyList<string> WorkingDirectories { get; init; } = Array.Empty<string>();

    public bool ForceRemoteSync { get; init; }
}

/// <summary>
/// 控制平面插件读取查询。
/// Control-plane query that reads a plugin.
/// </summary>
public sealed record ControlPlanePluginReadQuery
{
    public string MarketplacePath { get; init; } = string.Empty;

    public string PluginName { get; init; } = string.Empty;
}

/// <summary>
/// 控制平面应用目录查询。
/// Control-plane query that lists apps.
/// </summary>
public sealed record ControlPlaneAppCatalogQuery
{
    public int? Limit { get; init; }

    public string? Cursor { get; init; }

    public ThreadId? ThreadId { get; init; }

    public bool ForceRefetch { get; init; }
}

/// <summary>
/// 控制平面实验特性查询。
/// Control-plane query that lists experimental features.
/// </summary>
public sealed record ControlPlaneExperimentalFeatureQuery
{
    public int? Limit { get; init; }

    public string? Cursor { get; init; }
}

/// <summary>
/// 控制平面 MCP Server 状态查询。
/// Control-plane query that lists MCP server status.
/// </summary>
public sealed record ControlPlaneMcpServerStatusQuery
{
    public int? Limit { get; init; }

    public string? Cursor { get; init; }
}
