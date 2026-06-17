using TianShu.Execution.Runtime;
using TianShu.AppHost.Configuration;
using TianShu.Contracts.Sessions;

namespace TianShu.Cli;

internal sealed record AppHostLaunchResolution(
    string? AppHostProjectPath,
    string? AppHostExecutablePath)
{
    public bool IsResolved
        => !string.IsNullOrWhiteSpace(AppHostProjectPath)
           || !string.IsNullOrWhiteSpace(AppHostExecutablePath);
}

internal static class CliAppHostLaunchResolver
{
    private const string PreferredProjectFileName = "TianShu.AppHost.csproj";
    private const string PreferredExecutableFileName = "TianShu.AppHost.exe";

    public static AppHostLaunchResolution Resolve(string? configuredProjectPath, string workingDirectory)
        => ResolveCore(
            configuredProjectPath,
            workingDirectory,
            AppContext.BaseDirectory,
            GetUserAppHostProbeRoot());

    internal static AppHostLaunchResolution ResolveForTesting(
        string? configuredProjectPath,
        string workingDirectory,
        string? baseDirectoryOverride,
        string? userProfileDirectoryOverride)
        => ResolveCore(
            configuredProjectPath,
            workingDirectory,
            baseDirectoryOverride,
            GetUserAppHostProbeRoot(userProfileDirectoryOverride));

    private static AppHostLaunchResolution ResolveCore(
        string? configuredProjectPath,
        string workingDirectory,
        string? baseDirectory,
        string? userAppHostProbeRoot)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return new AppHostLaunchResolution(null, null);
        }

        var normalizedWorkingDirectory = Path.GetFullPath(workingDirectory);
        var explicitProjectPath = ResolveConfiguredProjectPath(configuredProjectPath, normalizedWorkingDirectory);
        if (!string.IsNullOrWhiteSpace(explicitProjectPath))
        {
            return new AppHostLaunchResolution(explicitProjectPath, null);
        }

        var sourceTreeProjectPath = ResolveSourceTreeProjectPath(normalizedWorkingDirectory);
        if (!string.IsNullOrWhiteSpace(sourceTreeProjectPath))
        {
            return new AppHostLaunchResolution(sourceTreeProjectPath, null);
        }

        var executablePath = ResolvePublishedExecutablePath(normalizedWorkingDirectory, baseDirectory, userAppHostProbeRoot);
        return new AppHostLaunchResolution(null, executablePath);
    }

    public static void ApplyToRuntimeOptions(ControlPlaneInitializeRuntimeCommand options, AppHostLaunchResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resolution);

        if (!string.IsNullOrWhiteSpace(resolution.AppHostProjectPath))
        {
            options.AppHostProjectPath = resolution.AppHostProjectPath;
            RuntimeHostLaunchLocator.ApplyPreferredLaunchMode(options, resolution.AppHostProjectPath);
            return;
        }

        if (string.IsNullOrWhiteSpace(resolution.AppHostExecutablePath))
        {
            throw new InvalidOperationException("未找到 TianShu 宿主启动入口。");
        }

        options.AppHostProjectPath = null;
        options.UseDotNetProjectLauncher = false;
        options.ExecutablePath = resolution.AppHostExecutablePath;
    }

    public static string? ResolveAppHostProjectPath(string? configuredProjectPath, string workingDirectory)
        => Resolve(configuredProjectPath, workingDirectory).AppHostProjectPath;

    private static string? ResolveConfiguredProjectPath(string? configuredProjectPath, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredProjectPath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(configuredProjectPath, workingDirectory);
        return RuntimeHostLaunchLocator.NormalizeProjectPath(fullPath);
    }

    private static string? ResolveSourceTreeProjectPath(string workingDirectory)
    {
        foreach (var relativePath in new[]
                 {
                     new[] { "src", "Hosting", "TianShu.AppHost", PreferredProjectFileName },
                 })
        {
            var candidate = CombinePath(workingDirectory, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var solutionRoot = RuntimeHostLaunchLocator.TryLocateSolutionRoot(workingDirectory);
        return string.IsNullOrWhiteSpace(solutionRoot)
            ? null
            : RuntimeHostLaunchLocator.ResolvePreferredHostProjectPath(solutionRoot);
    }

    private static string? ResolvePublishedExecutablePath(
        string workingDirectory,
        string? baseDirectory,
        string? userAppHostProbeRoot)
    {
        foreach (var probeRoot in EnumerateExecutableProbeRoots(workingDirectory, baseDirectory, userAppHostProbeRoot))
        {
            var candidate = ProbeExecutableUnderRoot(probeRoot);
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateExecutableProbeRoots(
        string workingDirectory,
        string? baseDirectory,
        string? userAppHostProbeRoot)
    {
        yield return workingDirectory;

        if (!string.IsNullOrWhiteSpace(baseDirectory)
            && !string.Equals(baseDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase))
        {
            yield return baseDirectory;
        }

        var baseDirectoryParent = ResolveParentDirectory(baseDirectory);
        if (!string.IsNullOrWhiteSpace(baseDirectoryParent)
            && !string.Equals(baseDirectoryParent, workingDirectory, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(baseDirectoryParent, baseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            yield return baseDirectoryParent;
        }

        if (!string.IsNullOrWhiteSpace(userAppHostProbeRoot)
            && !string.Equals(userAppHostProbeRoot, workingDirectory, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(userAppHostProbeRoot, baseDirectory, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(userAppHostProbeRoot, baseDirectoryParent, StringComparison.OrdinalIgnoreCase))
        {
            yield return userAppHostProbeRoot;
        }
    }

    private static string? ProbeExecutableUnderRoot(string? probeRoot)
    {
        if (string.IsNullOrWhiteSpace(probeRoot) || !Directory.Exists(probeRoot))
        {
            return null;
        }

        foreach (var candidate in new[]
                 {
                     Path.Combine(probeRoot, PreferredExecutableFileName),
                     Path.Combine(probeRoot, "TianShu.AppHost", PreferredExecutableFileName),
                     TianShuHomePathUtilities.ResolveRuntimePathFromHome(probeRoot, "apphost", PreferredExecutableFileName),
                 })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolveParentDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        return Directory.GetParent(Path.GetFullPath(directory))?.FullName;
    }

    private static string? GetUserAppHostProbeRoot(string? userProfileDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(userProfileDirectory))
        {
            return TianShuHomePathUtilities.ResolveTianShuHomePath();
        }

        var profileDirectory = string.IsNullOrWhiteSpace(userProfileDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : userProfileDirectory;
        if (string.IsNullOrWhiteSpace(profileDirectory))
        {
            return null;
        }

        return Path.Combine(Path.GetFullPath(profileDirectory), ".tianshu");
    }

    private static string CombinePath(string root, IReadOnlyList<string> segments)
    {
        var path = root;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }
}
