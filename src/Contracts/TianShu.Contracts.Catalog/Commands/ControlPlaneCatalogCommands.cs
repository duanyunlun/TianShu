using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Catalog;

/// <summary>
/// 控制平面配置值写入命令。
/// Control-plane command that writes a config value.
/// </summary>
public sealed record ControlPlaneConfigValueWriteCommand
{
    public string KeyPath { get; init; } = string.Empty;

    public StructuredValue? Value { get; init; }

    public string MergeStrategy { get; init; } = "replace";

    public string? WorkingDirectory { get; init; }

    public string? FilePath { get; init; }

    public string? ExpectedVersion { get; init; }
}

/// <summary>
/// 控制平面配置批量写入项。
/// Control-plane batch config write item.
/// </summary>
public sealed record ControlPlaneConfigWriteItem
{
    public string KeyPath { get; init; } = string.Empty;

    public StructuredValue? Value { get; init; }

    public string MergeStrategy { get; init; } = "replace";
}

/// <summary>
/// 控制平面配置批量写入命令。
/// Control-plane command that writes multiple config values.
/// </summary>
public sealed record ControlPlaneConfigBatchWriteCommand
{
    public IReadOnlyList<ControlPlaneConfigWriteItem> Items { get; init; } = Array.Empty<ControlPlaneConfigWriteItem>();

    public string? WorkingDirectory { get; init; }

    public string? FilePath { get; init; }

    public string? ExpectedVersion { get; init; }

    public bool ReloadUserConfig { get; init; }
}

/// <summary>
/// 控制平面技能配置写入命令。
/// Control-plane command that writes skill config.
/// </summary>
public sealed record ControlPlaneSkillConfigWriteCommand
{
    public string Path { get; init; } = string.Empty;

    public bool Enabled { get; init; }

    public string? WorkingDirectory { get; init; }
}

/// <summary>
/// 控制平面远程技能导出命令。
/// Control-plane command that exports a remote skill.
/// </summary>
public sealed record ControlPlaneRemoteSkillExportCommand
{
    public string HazelnutId { get; init; } = string.Empty;
}

/// <summary>
/// 控制平面插件安装命令。
/// Control-plane command that installs a plugin.
/// </summary>
public sealed record ControlPlanePluginInstallCommand
{
    public string MarketplacePath { get; init; } = string.Empty;

    public string PluginName { get; init; } = string.Empty;

    public string? WorkingDirectory { get; init; }
}

/// <summary>
/// 控制平面插件卸载命令。
/// Control-plane command that uninstalls a plugin.
/// </summary>
public sealed record ControlPlanePluginUninstallCommand
{
    public string PluginId { get; init; } = string.Empty;

    public string? WorkingDirectory { get; init; }
}

/// <summary>
/// 控制平面 MCP Server 重新加载命令。
/// Control-plane command that reloads MCP servers.
/// </summary>
public sealed record ControlPlaneMcpServerReloadCommand;

/// <summary>
/// 控制平面模型 Provider 包重新加载命令。
/// Control-plane command that reloads model provider packages.
/// </summary>
public sealed record ControlPlaneProviderPackageReloadCommand;

/// <summary>
/// 控制平面 MCP Server OAuth 登录启动命令。
/// Control-plane command that starts MCP server OAuth login.
/// </summary>
public sealed record ControlPlaneMcpServerOauthLoginStartCommand
{
    public string Name { get; init; } = string.Empty;

    public long? TimeoutSecs { get; init; }
}
