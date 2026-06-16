namespace TianShu.AppHost.Configuration;

/// <summary>
/// TianShu 宿主 home 目录解析辅助件。
/// Helpers for resolving the TianShu host home directory.
/// </summary>
public static class TianShuHomePathUtilities
{
    public static string ResolveTianShuHomePath()
        => TianShu.Configuration.TianShuHomePathUtilities.ResolveTianShuHomePath();

    public static string ResolveTianShuStateRootPath()
        => TianShu.Configuration.TianShuHomePathUtilities.ResolveTianShuStateRootPath();

    public static string ResolveTianShuSessionsRootPath()
        => TianShu.Configuration.TianShuHomePathUtilities.ResolveTianShuSessionsRootPath();

    public static string ResolveTianShuRuntimeRootPath()
        => TianShu.Configuration.TianShuHomePathUtilities.ResolveTianShuRuntimeRootPath();

    public static string ResolveTianShuDataRootPath()
        => TianShu.Configuration.TianShuHomePathUtilities.ResolveTianShuDataRootPath();

    public static string ResolveTianShuModulesRootPath()
        => TianShu.Configuration.TianShuHomePathUtilities.ResolveTianShuModulesRootPath();

    public static string ResolveTianShuModulePath(params string[] segments)
        => TianShu.Configuration.TianShuHomePathUtilities.ResolveTianShuModulePath(segments);

    public static string ResolveRuntimePathFromHome(string tianShuHomePath, params string[] segments)
        => TianShu.Configuration.TianShuHomePathUtilities.ResolveRuntimePathFromHome(tianShuHomePath, segments);

    public static string ResolveDataPathFromHome(string tianShuHomePath, params string[] segments)
        => TianShu.Configuration.TianShuHomePathUtilities.ResolveDataPathFromHome(tianShuHomePath, segments);

    public static string ResolveModulePathFromHome(string tianShuHomePath, params string[] segments)
        => TianShu.Configuration.TianShuHomePathUtilities.ResolveModulePathFromHome(tianShuHomePath, segments);

    public static string ResolveModulePathFromConfig(string tianShuConfigPath, params string[] segments)
        => TianShu.Configuration.TianShuHomePathUtilities.ResolveModulePathFromConfig(tianShuConfigPath, segments);

    public static string ResolveDataPathFromConfig(string tianShuConfigPath, params string[] segments)
        => TianShu.Configuration.TianShuHomePathUtilities.ResolveDataPathFromConfig(tianShuConfigPath, segments);
}
