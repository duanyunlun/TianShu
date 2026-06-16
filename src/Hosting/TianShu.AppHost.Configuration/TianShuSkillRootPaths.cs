namespace TianShu.AppHost.Configuration;

/// <summary>
/// TianShu 技能根目录路径工具。
/// Path helpers for TianShu skill roots.
/// </summary>
public static class TianShuSkillRootPaths
{
    private const string DefaultProgramDataDirectory = @"C:\ProgramData";

    public static string ResolveSystemSkillsCacheRoot(string homePath)
        => TianShu.Configuration.TianShuSkillRootPaths.ResolveSystemSkillsCacheRoot(homePath);

    public static string ResolveAdminSkillsRoot(string? systemConfigRoot = null)
        => Path.Combine(
            NormalizePath(systemConfigRoot) ?? ResolveDefaultSystemConfigRoot(),
            "skills");

    public static string ResolveDefaultSystemConfigRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "/etc/tianshu";
        }

        var programData = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
            ?? DefaultProgramDataDirectory;
        return Path.Combine(programData, "TianShu");
    }

    private static string? NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Path.GetFullPath(value.Trim());
    }
}
