namespace TianShu.Contracts.Configuration;

/// <summary>
/// TianShu 用户级运行时目录布局解析工具。
/// TianShu user-level runtime layout path resolver.
/// </summary>
public static class TianShuRuntimeLayoutPaths
{
    private const string TianShuHomeEnvironmentVariable = "TIANSHU_HOME";
    private const string TianShuStateHomeEnvironmentVariable = "TIANSHU_STATE_HOME";
    private const string TianShuSessionsHomeEnvironmentVariable = "TIANSHU_SESSIONS_HOME";

    public static string ResolveTianShuHomePath()
    {
        var configured = Normalize(Environment.GetEnvironmentVariable(TianShuHomeEnvironmentVariable));
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured!;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".tianshu");
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
}
