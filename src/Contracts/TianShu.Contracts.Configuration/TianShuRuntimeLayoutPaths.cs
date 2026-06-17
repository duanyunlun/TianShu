using System.Security.Cryptography;
using System.Text;

namespace TianShu.Contracts.Configuration;

/// <summary>
/// 便携包目录布局。包根直接等价于 TianShuHome。
/// Portable package layout. The package root is the TianShuHome root.
/// </summary>
public sealed record PortableTianShuHomeLayout(
    string PackageRoot,
    string BinRoot,
    string ConfigFilePath,
    string ModulesRoot,
    string RuntimeRoot,
    string DataRoot);

/// <summary>
/// TianShu 用户级运行时目录布局解析工具。
/// TianShu user-level runtime layout path resolver.
/// </summary>
public static class TianShuRuntimeLayoutPaths
{
    private const string TianShuHomeEnvironmentVariable = "TIANSHU_HOME";
    private const string TianShuStateHomeEnvironmentVariable = "TIANSHU_STATE_HOME";
    private const string TianShuSessionsHomeEnvironmentVariable = "TIANSHU_SESSIONS_HOME";
    private static readonly StringComparison PathNameComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static string ResolveTianShuHomePath()
        => ResolveTianShuHomePathFrom(
            AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable(TianShuHomeEnvironmentVariable),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static string ResolveTianShuHomePathFrom(
        string programDirectory,
        string? configuredTianShuHome,
        string? userProfileDirectory)
    {
        var portableLayout = TryResolvePortableTianShuHomeLayoutFrom(programDirectory);
        if (portableLayout is not null)
        {
            return portableLayout.PackageRoot;
        }

        var configured = Normalize(configuredTianShuHome);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured!;
        }

        var userProfile = Normalize(userProfileDirectory)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".tianshu");
    }

    public static PortableTianShuHomeLayout? TryResolvePortableTianShuHomeLayout()
        => TryResolvePortableTianShuHomeLayoutFrom(AppContext.BaseDirectory);

    public static PortableTianShuHomeLayout? TryResolvePortableTianShuHomeLayoutFrom(string programDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programDirectory);

        var binRoot = Path.GetFullPath(programDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        if (!string.Equals(Path.GetFileName(binRoot), "bin", PathNameComparison))
        {
            return null;
        }

        var packageRoot = Directory.GetParent(binRoot)?.FullName;
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            return null;
        }

        var configFilePath = Path.Combine(packageRoot, "tianshu.toml");
        var modulesRoot = Path.Combine(packageRoot, "modules");
        if (!File.Exists(configFilePath) || !Directory.Exists(modulesRoot))
        {
            return null;
        }

        return new PortableTianShuHomeLayout(
            PackageRoot: packageRoot,
            BinRoot: binRoot,
            ConfigFilePath: configFilePath,
            ModulesRoot: modulesRoot,
            RuntimeRoot: Path.Combine(packageRoot, "runtime"),
            DataRoot: Path.Combine(packageRoot, "data"));
    }

    public static string ResolveTianShuStateRootPath()
    {
        var configured = Normalize(Environment.GetEnvironmentVariable(TianShuStateHomeEnvironmentVariable));
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured!;
        }

        return ResolveDataPathFromHome(ResolveTianShuHomePath(), "state");
    }

    public static string ResolveTianShuSessionsRootPath()
    {
        var configured = Normalize(Environment.GetEnvironmentVariable(TianShuSessionsHomeEnvironmentVariable));
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured!;
        }

        return ResolveDataPathFromHome(ResolveTianShuHomePath(), "sessions");
    }

    public static string ResolveTianShuRuntimeRootPath()
        => ResolveRuntimePathFromHome(ResolveTianShuHomePath());

    public static string ResolveTianShuDataRootPath()
        => ResolveDataPathFromHome(ResolveTianShuHomePath());

    public static string ResolveTianShuModulesRootPath()
        => ResolveModulePathFromHome(ResolveTianShuHomePath());

    public static string ResolveTianShuConfigFilePath()
        => ResolveTianShuConfigFilePathFromHome(ResolveTianShuHomePath());

    public static string ResolveTianShuConfigFilePathFromHome(string tianShuHomePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tianShuHomePath);
        return Path.Combine(Path.GetFullPath(tianShuHomePath), "tianshu.toml");
    }

    public static string ResolveTianShuModulePath(params string[] segments)
        => ResolveModulePathFromHome(ResolveTianShuHomePath(), segments);

    public static string ResolveWorkspaceKey(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        var normalized = NormalizeWorkspaceDirectory(workingDirectory);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return $"workspace-{hash[..16]}";
    }

    public static string ResolveRuntimeWorkspacePath(string area, string workingDirectory, params string[] segments)
        => ResolveRuntimeWorkspacePathFromHome(ResolveTianShuHomePath(), area, workingDirectory, segments);

    public static string ResolveRuntimeWorkspacePathFromHome(
        string tianShuHomePath,
        string area,
        string workingDirectory,
        params string[] segments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tianShuHomePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        var root = Path.Combine(
            Path.GetFullPath(tianShuHomePath),
            "runtime",
            SanitizePathSegment(area),
            ResolveWorkspaceKey(workingDirectory));
        return CombinePath(root, segments);
    }

    public static string ResolveRuntimePathFromHome(string tianShuHomePath, params string[] segments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tianShuHomePath);
        return CombinePath(Path.Combine(Path.GetFullPath(tianShuHomePath), "runtime"), segments);
    }

    public static string ResolveDataPathFromHome(string tianShuHomePath, params string[] segments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tianShuHomePath);
        return CombinePath(Path.Combine(Path.GetFullPath(tianShuHomePath), "data"), segments);
    }

    public static string ResolveModulePathFromHome(string tianShuHomePath, params string[] segments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tianShuHomePath);
        return CombinePath(Path.Combine(Path.GetFullPath(tianShuHomePath), "modules"), segments);
    }

    public static string ResolveModulePathFromConfig(string tianShuConfigPath, params string[] segments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tianShuConfigPath);
        var root = Path.GetDirectoryName(Path.GetFullPath(tianShuConfigPath)) ?? Environment.CurrentDirectory;
        return CombinePath(Path.Combine(root, "modules"), segments);
    }

    public static string ResolveDataPathFromConfig(string tianShuConfigPath, params string[] segments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tianShuConfigPath);
        var root = Path.GetDirectoryName(Path.GetFullPath(tianShuConfigPath)) ?? Environment.CurrentDirectory;
        return CombinePath(Path.Combine(root, "data"), segments);
    }

    public static string ResolveSystemSkillsCacheRoot(string homePath)
        => ResolveModulePathFromHome(homePath, "skills", ".system");

    private static string CombinePath(string root, IReadOnlyList<string> segments)
    {
        var path = root;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
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

    private static string NormalizeWorkspaceDirectory(string workingDirectory)
    {
        var normalized = Path.GetFullPath(workingDirectory.Trim()).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (normalized.Length == 0)
        {
            normalized = Path.GetPathRoot(Path.GetFullPath(workingDirectory.Trim())) ?? Path.GetFullPath(workingDirectory.Trim());
        }

        return OperatingSystem.IsWindows()
            ? normalized.ToUpperInvariant()
            : normalized;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }
}
