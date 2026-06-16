namespace TianShu.Contracts.Catalog;

/// <summary>
/// 绑定 Provider 画像命令。
/// Command that binds a provider profile into the catalog.
/// </summary>
public sealed record BindProviderProfile(ProviderProfile Profile);

/// <summary>
/// 刷新能力目录命令。
/// Command that refreshes the capability catalog.
/// </summary>
public sealed record RefreshCatalog(string? WorkspacePath = null, bool IncludeHiddenModels = false);

/// <summary>
/// 安装插件命令。
/// Command that installs a plugin into the catalog surface.
/// </summary>
public sealed record InstallPlugin(string PluginId, string? Version = null);

/// <summary>
/// 刷新 MCP 服务清单命令。
/// Command that refreshes the MCP server registry.
/// </summary>
public sealed record RefreshMcpServers(string? WorkspacePath = null);
