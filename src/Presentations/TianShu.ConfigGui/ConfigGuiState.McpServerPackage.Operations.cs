using TianShu.Configuration;
using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void RefreshMcpServerPackageProjection(string? selectedManifestPath)
    {
        var root = TianShuMcpServerManifestConfiguration.ResolveRootDirectory(ConfigPath);
        mcpServerPackageProjection = mcpServerManifestConfiguration.Load(root, selectedManifestPath);
        McpServerPackageRootText.Value = $"扫描目录：{mcpServerPackageProjection.McpServerRootDirectory}";
        McpServerPackageLabels = mcpServerPackageProjection.Files.Select(static file => file.DisplayName).ToArray();
        selectedMcpServerManifestPath = mcpServerPackageProjection.SelectedManifestPath;

        var packageIndex = mcpServerPackageProjection.SelectedManifestPath is null
            ? 0
            : mcpServerPackageProjection.Files
                .Select((file, index) => new { file, index })
                .FirstOrDefault(item => string.Equals(item.file.Path, mcpServerPackageProjection.SelectedManifestPath, StringComparison.OrdinalIgnoreCase))
                ?.index ?? 0;
        McpServerPackageIndex.Value = packageIndex;

        if (mcpServerPackageProjection.SelectedPackage is null)
        {
            BeginNewMcpServerPackage();
            McpServerPackageStatusText.Value = mcpServerPackageProjection.Issues.Count == 0
                ? "未发现 MCP Server 包 manifest；可新建用户 MCP Server 包。"
                : string.Join(Environment.NewLine, mcpServerPackageProjection.Issues.Select(static issue => issue.Message));
            return;
        }

        LoadMcpServerPackageIntoEditor(mcpServerPackageProjection.SelectedPackage);
        var issueText = mcpServerPackageProjection.Issues.Count == 0
            ? string.Empty
            : $"，{mcpServerPackageProjection.Issues.Count} 条问题";
        McpServerPackageStatusText.Value = $"已读取 {mcpServerPackageProjection.Files.Count} 个 MCP Server 包 manifest{issueText}。";
    }

    public void SelectMcpServerPackageIndex(int index)
    {
        if (mcpServerPackageProjection is null || mcpServerPackageProjection.Files.Count == 0)
        {
            BeginNewMcpServerPackage();
            return;
        }

        var packageIndex = Math.Clamp(index, 0, mcpServerPackageProjection.Files.Count - 1);
        RefreshMcpServerPackageProjection(mcpServerPackageProjection.Files[packageIndex].Path);
    }

    public void BeginNewMcpServerPackage()
    {
        selectedMcpServerManifestPath = null;
        selectedMcpServerId = null;
        McpServerPackageIndex.Value = 0;
        McpServerPackageId.Value = CreateUniqueMcpServerPackageId("custom-mcp");
        McpServerPackageDisplayName.Value = McpServerPackageId.Value;
        McpServerPackageEnabledIndex.Value = 0;
        SelectMcpServerPackageType("package");
        McpServerPackagePriority.Value = "0";
        McpServerLabels = [];
        BeginNewMcpServer();
        McpServerPackageManifestPathText.Value = "保存目标：新建后写入 modules/mcp-servers/<package-id>/server.toml";
        McpServerPackageStatusText.Value = "正在新建 MCP Server 包；保存时会写入 modules/mcp-servers 目录。";
    }

    public void CopySelectedMcpServerPackageToDraft()
    {
        if (mcpServerPackageProjection?.SelectedPackage is not { } package)
        {
            McpServerPackageStatusText.Value = "没有可复制的 MCP Server 包。";
            return;
        }

        selectedMcpServerManifestPath = null;
        McpServerPackageId.Value = CreateUniqueMcpServerPackageId(package.Id);
        McpServerPackageDisplayName.Value = package.DisplayName;
        McpServerPackageManifestPathText.Value = "保存目标：复制后写入 modules/mcp-servers/<package-id>/server.toml";
        McpServerPackageStatusText.Value = $"已复制 MCP Server 包到草稿：{package.Id} -> {McpServerPackageId.Value}";
    }

    public void DeleteSelectedMcpServerPackage()
    {
        if (string.IsNullOrWhiteSpace(selectedMcpServerManifestPath))
        {
            McpServerPackageStatusText.Value = "没有可删除的 MCP Server 包 manifest。";
            return;
        }

        try
        {
            var root = TianShuMcpServerManifestConfiguration.ResolveRootDirectory(ConfigPath);
            mcpServerManifestConfiguration.DeletePackage(root, selectedMcpServerManifestPath);
            selectedMcpServerManifestPath = null;
            RefreshMcpServerPackageProjection(null);
            McpServerPackageStatusText.Value = "已删除用户 MCP Server 包 manifest。";
        }
        catch (Exception ex)
        {
            McpServerPackageStatusText.Value = $"删除 MCP Server 包失败：{ex.Message}";
        }
    }

    public void SelectMcpServerIndex(int index)
    {
        var servers = mcpServerPackageProjection?.SelectedPackage?.Servers ?? [];
        if (servers.Count == 0)
        {
            BeginNewMcpServer();
            return;
        }

        var serverIndex = Math.Clamp(index, 0, servers.Count - 1);
        LoadMcpServerIntoEditor(servers[serverIndex], serverIndex);
    }

    public void BeginNewMcpServer()
    {
        selectedMcpServerId = null;
        McpServerIndex.Value = 0;
        McpServerId.Value = CreateUniqueMcpServerId("server");
        McpServerDisplayName.Value = McpServerId.Value;
        McpServerEnabledIndex.Value = 0;
        McpServerRequiredIndex.Value = 1;
        SelectMcpServerTransport("stdio");
        McpServerCommand.Value = string.Empty;
        McpServerArgs.Value = string.Empty;
        McpServerCwd.Value = string.Empty;
        McpServerUrl.Value = string.Empty;
        McpServerBearerTokenEnvVar.Value = string.Empty;
        McpServerEnvVars.Value = string.Empty;
        McpServerStartupTimeoutMs.Value = "10000";
        McpServerToolTimeoutMs.Value = "120000";
        McpServerEnabledTools.Value = string.Empty;
        McpServerDisabledTools.Value = string.Empty;
        McpServerResolvedPathText.Value = "Server 保存后会写入当前 MCP Server 包的 [[servers]]。";
    }

    public void DeleteSelectedMcpServer()
    {
        if (mcpServerPackageProjection?.SelectedPackage is not { } package || string.IsNullOrWhiteSpace(selectedMcpServerId))
        {
            McpServerPackageStatusText.Value = "没有可删除的 Server 条目。";
            return;
        }

        package.Servers = package.Servers
            .Where(server => !string.Equals(server.Id, selectedMcpServerId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        selectedMcpServerId = null;
        SaveMcpServerPackageValue(package);
    }

    public void SaveMcpServerPackage()
    {
        try
        {
            var package = BuildMcpServerPackageFromEditor();
            SaveMcpServerPackageValue(package);
        }
        catch (Exception ex)
        {
            McpServerPackageStatusText.Value = $"保存 MCP Server 包失败：{ex.Message}";
            UpdateContextPanel();
        }
    }

    private void SaveMcpServerPackageValue(McpServerPackageManifestValue package)
    {
        var root = TianShuMcpServerManifestConfiguration.ResolveRootDirectory(ConfigPath);
        var targetPath = string.IsNullOrWhiteSpace(selectedMcpServerManifestPath)
            ? mcpServerManifestConfiguration.CreatePackage(root, package.Id, overwrite: false)
            : selectedMcpServerManifestPath;
        package.ManifestPath = targetPath;
        package.PackageDirectory = Path.GetDirectoryName(targetPath)!;
        mcpServerManifestConfiguration.SavePackage(targetPath, package);
        RefreshMcpServerPackageProjection(targetPath);
        McpServerPackageStatusText.Value = $"已保存 MCP Server 包 manifest：{targetPath}";
        UpdateContextPanel();
    }

    private McpServerPackageManifestValue BuildMcpServerPackageFromEditor()
    {
        var package = mcpServerPackageProjection?.SelectedPackage is { } selectedPackage
            ? CloneMcpServerPackage(selectedPackage)
            : new McpServerPackageManifestValue();
        package.Id = McpServerPackageId.Value.Trim();
        package.DisplayName = McpServerPackageDisplayName.Value.Trim();
        package.Enabled = McpServerPackageEnabledIndex.Value != 1;
        package.Type = GetSelectedMcpServerPackageType();
        package.Priority = ParseIntOrDefault(McpServerPackagePriority.Value, 0);

        var server = new McpServerManifestValue
        {
            Id = McpServerId.Value.Trim(),
            DisplayName = McpServerDisplayName.Value.Trim(),
            Enabled = McpServerEnabledIndex.Value != 1,
            Required = McpServerRequiredIndex.Value == 0,
            Transport = GetSelectedMcpServerTransport(),
            Command = NullIfWhiteSpace(McpServerCommand.Value),
            Args = SplitCommaList(McpServerArgs.Value),
            EnvVars = SplitCommaList(McpServerEnvVars.Value),
            Cwd = NullIfWhiteSpace(McpServerCwd.Value),
            Url = NullIfWhiteSpace(McpServerUrl.Value),
            BearerTokenEnvVar = NullIfWhiteSpace(McpServerBearerTokenEnvVar.Value),
            StartupTimeoutMs = ParseNullableInt(McpServerStartupTimeoutMs.Value),
            ToolTimeoutMs = ParseNullableInt(McpServerToolTimeoutMs.Value),
            EnabledTools = SplitCommaList(McpServerEnabledTools.Value),
            DisabledTools = SplitCommaList(McpServerDisabledTools.Value),
        };

        var servers = package.Servers.ToList();
        if (!string.IsNullOrWhiteSpace(server.Id))
        {
            var index = servers.FindIndex(item => string.Equals(item.Id, selectedMcpServerId ?? server.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                servers[index] = server;
            }
            else
            {
                servers.Add(server);
            }
        }

        package.Servers = servers;
        return package;
    }

    private void LoadMcpServerPackageIntoEditor(McpServerPackageManifestValue package)
    {
        McpServerPackageId.Value = package.Id;
        McpServerPackageDisplayName.Value = package.DisplayName;
        McpServerPackageEnabledIndex.Value = package.Enabled ? 0 : 1;
        SelectMcpServerPackageType(package.Type);
        McpServerPackagePriority.Value = package.Priority.ToString();
        McpServerPackageManifestPathText.Value = $"Manifest：{package.ManifestPath}";
        McpServerLabels = package.Servers.Select(static server => server.Id).ToArray();
        if (package.Servers.Count == 0)
        {
            BeginNewMcpServer();
        }
        else
        {
            var index = string.IsNullOrWhiteSpace(selectedMcpServerId)
                ? 0
                : package.Servers
                    .Select((server, serverIndex) => new { server, serverIndex })
                    .FirstOrDefault(item => string.Equals(item.server.Id, selectedMcpServerId, StringComparison.OrdinalIgnoreCase))
                    ?.serverIndex ?? 0;
            LoadMcpServerIntoEditor(package.Servers[index], index);
        }
    }

    private void LoadMcpServerIntoEditor(McpServerManifestValue server, int index)
    {
        selectedMcpServerId = server.Id;
        McpServerIndex.Value = index;
        McpServerId.Value = server.Id;
        McpServerDisplayName.Value = server.DisplayName;
        McpServerEnabledIndex.Value = server.Enabled ? 0 : 1;
        McpServerRequiredIndex.Value = server.Required ? 0 : 1;
        SelectMcpServerTransport(server.Transport);
        McpServerCommand.Value = server.Command ?? string.Empty;
        McpServerArgs.Value = JoinList(server.Args);
        McpServerCwd.Value = server.Cwd ?? string.Empty;
        McpServerUrl.Value = server.Url ?? string.Empty;
        McpServerBearerTokenEnvVar.Value = server.BearerTokenEnvVar ?? string.Empty;
        McpServerEnvVars.Value = JoinList(server.EnvVars);
        McpServerStartupTimeoutMs.Value = server.StartupTimeoutMs?.ToString() ?? string.Empty;
        McpServerToolTimeoutMs.Value = server.ToolTimeoutMs?.ToString() ?? string.Empty;
        McpServerEnabledTools.Value = JoinList(server.EnabledTools);
        McpServerDisabledTools.Value = JoinList(server.DisabledTools);
        McpServerResolvedPathText.Value = mcpServerPackageProjection?.SelectedPackage is { } package
            ? $"工作目录解析路径：{TianShuMcpServerManifestConfiguration.ResolveServerCwdFullPath(package, server)}"
            : "Server 保存后会写入当前 MCP Server 包的 [[servers]]。";
    }

    private void SelectMcpServerPackageType(string? type)
    {
        var index = FindLabelIndex(McpServerPackageTypeLabels, type, fallbackIndex: 2);
        McpServerPackageTypeIndex.Value = index;
        McpServerPackageType.Value = McpServerPackageTypeLabels[index];
    }

    private void SelectMcpServerTransport(string? transport)
    {
        var index = FindLabelIndex(McpServerTransportLabels, transport, fallbackIndex: 0);
        McpServerTransportIndex.Value = index;
        McpServerTransport.Value = McpServerTransportLabels[index];
    }
}
