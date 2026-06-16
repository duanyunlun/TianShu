using TianShu.Configuration;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 扫描用户级 MCP Server 包 manifest，并归一为 Kernel MCP manager 可消费的定义。
/// Scans user-level MCP Server package manifests and normalizes them for the Kernel MCP manager.
/// </summary>
internal static class KernelMcpServerPackageManifestLoader
{
    public const string McpServerDirectoryName = "mcp-servers";
    public const string ManifestFileName = "server.toml";

    public static IReadOnlyDictionary<string, KernelMcpServerPackageDefinition> Load(string tianShuHomePath)
    {
        var root = Path.GetFullPath(tianShuHomePath);
        var result = new Dictionary<string, KernelMcpServerPackageDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in new TianShuMcpServerManifestConfiguration().LoadEnabledPackages(root))
        {
            foreach (var server in package.Servers
                         .Where(static server => server.Enabled)
                         .Select(server => ToPackageDefinition(package, server)))
            {
                if (!string.IsNullOrWhiteSpace(server.Name))
                {
                    result[server.Name] = server;
                }
            }
        }

        return result;
    }

    public static IReadOnlyList<string> ListServerNames(string tianShuHomePath)
        => Load(tianShuHomePath).Keys.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    public static string? ResolveServerUrl(string tianShuHomePath, string serverName)
    {
        var servers = Load(tianShuHomePath);
        return servers.TryGetValue(serverName, out var server)
            ? Normalize(server.Url)
            : null;
    }

    private static KernelMcpServerPackageDefinition ToPackageDefinition(
        McpServerPackageManifestValue package,
        McpServerManifestValue server)
    {
        var name = Normalize(server.Id);
        var cwd = NormalizePath(package.PackageDirectory, server.Cwd);
        TimeSpan? startupTimeout = server.StartupTimeoutMs is null ? null : TimeSpan.FromMilliseconds(server.StartupTimeoutMs.Value);
        TimeSpan? toolTimeout = server.ToolTimeoutMs is null ? null : TimeSpan.FromMilliseconds(server.ToolTimeoutMs.Value);
        return new KernelMcpServerPackageDefinition(
            Name: name ?? string.Empty,
            Enabled: server.Enabled,
            Required: server.Required,
            Command: Normalize(server.Command),
            Args: server.Args,
            Env: server.Env,
            EnvVars: server.EnvVars,
            Cwd: cwd,
            Url: Normalize(server.Url),
            BearerTokenEnvVar: Normalize(server.BearerTokenEnvVar),
            HttpHeaders: server.HttpHeaders,
            EnvHttpHeaders: server.EnvHttpHeaders,
            StartupTimeout: startupTimeout,
            ToolTimeout: toolTimeout,
            EnabledTools: server.EnabledTools,
            DisabledTools: server.DisabledTools);
    }

    private static string? NormalizePath(string packageDirectory, string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        return Path.IsPathFullyQualified(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(packageDirectory, normalized));
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

}

internal sealed record KernelMcpServerPackageDefinition(
    string Name,
    bool Enabled,
    bool Required,
    string? Command,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyList<string> EnvVars,
    string? Cwd,
    string? Url,
    string? BearerTokenEnvVar,
    IReadOnlyDictionary<string, string> HttpHeaders,
    IReadOnlyDictionary<string, string> EnvHttpHeaders,
    TimeSpan? StartupTimeout,
    TimeSpan? ToolTimeout,
    IReadOnlyList<string> EnabledTools,
    IReadOnlyList<string> DisabledTools);
