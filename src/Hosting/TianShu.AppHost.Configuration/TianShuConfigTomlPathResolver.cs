namespace TianShu.AppHost.Configuration;

/// <summary>
/// TianShu 配置文件路径解析器。
/// Resolves TianShu configuration file paths across host environments.
/// </summary>
public static class TianShuConfigTomlPathResolver
{
    private const string SystemRootOverrideEnvironmentVariable = "TIANSHU_SYSTEM_CONFIG_ROOT";
    private const string UnixSystemConfigTomlPath = "/etc/tianshu/tianshu.toml";
    private const string UnixSystemRequirementsTomlPath = "/etc/tianshu/requirements.toml";
    private const string UnixLegacyManagedConfigTomlPath = "/etc/tianshu/managed_config.toml";
    private const string WindowsDefaultProgramDataPath = @"C:\ProgramData";

    public static string ResolveSystemConfigTomlPath()
    {
        var systemRootOverride = ResolveSystemRootOverride();
        if (!string.IsNullOrWhiteSpace(systemRootOverride))
        {
            return Path.Combine(systemRootOverride!, "tianshu.toml");
        }

        if (OperatingSystem.IsWindows())
        {
            var programData = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
                ?? NormalizePath(Environment.GetEnvironmentVariable("ProgramData"))
                ?? WindowsDefaultProgramDataPath;
            return Path.Combine(programData, "TianShu", "tianshu.toml");
        }

        return UnixSystemConfigTomlPath;
    }

    public static string ResolveSystemRequirementsTomlPath()
    {
        var systemRootOverride = ResolveSystemRootOverride();
        if (!string.IsNullOrWhiteSpace(systemRootOverride))
        {
            return Path.Combine(systemRootOverride!, "requirements.toml");
        }

        if (OperatingSystem.IsWindows())
        {
            var programData = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
                ?? NormalizePath(Environment.GetEnvironmentVariable("ProgramData"))
                ?? WindowsDefaultProgramDataPath;
            return Path.Combine(programData, "TianShu", "requirements.toml");
        }

        return UnixSystemRequirementsTomlPath;
    }

    public static string ResolveUserConfigTomlPath()
        => Path.Combine(TianShuHomePathUtilities.ResolveTianShuHomePath(), "tianshu.toml");

    public static string ResolveUserRequirementsTomlPath()
    {
        var userConfigPath = ResolveUserConfigTomlPath();
        var TianShuHome = NormalizeDirectory(Path.GetDirectoryName(userConfigPath));
        if (string.IsNullOrWhiteSpace(TianShuHome))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".tianshu",
                "requirements.toml");
        }

        return Path.Combine(TianShuHome, "requirements.toml");
    }

    public static string ResolveLegacyManagedConfigTomlPath()
    {
        var systemRootOverride = ResolveSystemRootOverride();
        if (!string.IsNullOrWhiteSpace(systemRootOverride))
        {
            return Path.Combine(systemRootOverride!, "managed_config.toml");
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return UnixLegacyManagedConfigTomlPath;
        }

        var TianShuHome = NormalizeDirectory(Path.GetDirectoryName(ResolveUserConfigTomlPath()));
        if (string.IsNullOrWhiteSpace(TianShuHome))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".tianshu",
                "managed_config.toml");
        }

        return Path.Combine(TianShuHome, "managed_config.toml");
    }

    public static string ResolveCwdConfigTomlPath(string? cwd)
    {
        var directory = NormalizeDirectory(cwd) ?? NormalizeDirectory(Environment.CurrentDirectory);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("无法确定工作区目录，无法读取 cwd tianshu.toml。");
        }

        return Path.Combine(directory, ".tianshu", "tianshu.toml");
    }

    public static IReadOnlyList<string> EnumerateProjectConfigPaths(
        string? cwd,
        IReadOnlyList<string>? projectRootMarkers = null)
    {
        var directory = NormalizeDirectory(cwd);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>();
        foreach (var dotTianShuDirectory in EnumerateProjectLayerDirectories(directory, projectRootMarkers))
        {
            var path = Path.Combine(dotTianShuDirectory, "tianshu.toml");
            if (File.Exists(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    public static IReadOnlyList<string> EnumerateProjectLayerDirectories(
        string? cwd,
        IReadOnlyList<string>? projectRootMarkers = null)
    {
        var directory = NormalizeDirectory(cwd);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>();
        foreach (var current in TianShuProjectRootResolver.EnumerateDirectoriesBetweenProjectRootAndCwd(directory, projectRootMarkers))
        {
            var dotTianShuDirectory = Path.Combine(current, ".tianshu");
            if (Directory.Exists(dotTianShuDirectory))
            {
                paths.Add(dotTianShuDirectory);
            }
        }

        return paths;
    }

    public static string ResolveWritableProjectConfigTomlPath(string? cwd)
    {
        var directory = NormalizeDirectory(cwd) ?? NormalizeDirectory(Environment.CurrentDirectory);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("无法确定工作区目录，无法写入工作区 tianshu.toml。");
        }

        return Path.Combine(directory, ".tianshu", "tianshu.toml");
    }

    public static string? NormalizeDirectory(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return null;
        }

        var fullPath = Path.IsPathRooted(cwd)
            ? Path.GetFullPath(cwd)
            : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, cwd));

        if (File.Exists(fullPath))
        {
            return Path.GetDirectoryName(fullPath);
        }

        return fullPath;
    }

    public static string? FindRepoRoot(string? directory)
        => TianShuProjectRootResolver.FindProjectRoot(directory);

    private static string? ResolveSystemRootOverride()
        => NormalizePath(Environment.GetEnvironmentVariable(SystemRootOverrideEnvironmentVariable));

    private static string? NormalizePath(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
