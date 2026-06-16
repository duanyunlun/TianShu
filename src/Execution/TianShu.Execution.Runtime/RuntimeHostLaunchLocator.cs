using TianShu.Contracts.Sessions;

namespace TianShu.Execution.Runtime;

public static class RuntimeHostLaunchLocator
{
    private static readonly string[] PreferredProjectRelativePathSegments = ["src", "Hosting", "TianShu.AppHost", "TianShu.AppHost.csproj"];

    public static void ApplyPreferredLaunchMode(ControlPlaneInitializeRuntimeCommand options, string? appHostProjectPath)
    {
        ArgumentNullException.ThrowIfNull(options);

        var normalizedProjectPath = NormalizeProjectPath(appHostProjectPath) ?? NormalizeProjectPath(options.AppHostProjectPath);
        if (string.IsNullOrWhiteSpace(normalizedProjectPath))
        {
            return;
        }

        options.AppHostProjectPath = normalizedProjectPath;

        var executablePath = ResolveBuiltExecutablePath(normalizedProjectPath);
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            options.UseDotNetProjectLauncher = false;
            options.ExecutablePath = executablePath;
            return;
        }

        options.UseDotNetProjectLauncher = true;
        options.ExecutablePath = "dotnet";
    }

    public static string? ResolveBuiltExecutablePath(string? appHostProjectPath)
    {
        var normalizedProjectPath = NormalizeProjectPath(appHostProjectPath);
        if (string.IsNullOrWhiteSpace(normalizedProjectPath) || !File.Exists(normalizedProjectPath))
        {
            return null;
        }

        var projectDirectory = Path.GetDirectoryName(normalizedProjectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return null;
        }

        var binDirectory = Path.Combine(projectDirectory, "bin");
        if (!Directory.Exists(binDirectory))
        {
            return null;
        }

        var executableName = $"{Path.GetFileNameWithoutExtension(normalizedProjectPath)}.exe";
        return Directory.EnumerateFiles(binDirectory, executableName, SearchOption.AllDirectories)
            .Select(static path => new FileInfo(path))
            .OrderByDescending(static info => info.LastWriteTimeUtc)
            .Select(static info => info.FullName)
            .FirstOrDefault();
    }

    public static string? NormalizeProjectPath(string? appHostProjectPath)
    {
        var normalizedProjectPath = NormalizePath(appHostProjectPath);
        if (string.IsNullOrWhiteSpace(normalizedProjectPath))
        {
            return null;
        }

        if (!File.Exists(normalizedProjectPath))
        {
            return null;
        }

        return normalizedProjectPath;
    }

    public static string? ResolvePreferredHostProjectPath(string solutionRoot)
    {
        var normalizedSolutionRoot = NormalizePath(solutionRoot);
        if (string.IsNullOrWhiteSpace(normalizedSolutionRoot))
        {
            return null;
        }

        var candidate = CombinePath(normalizedSolutionRoot, PreferredProjectRelativePathSegments);
        return File.Exists(candidate) ? candidate : null;
    }

    public static string? TryLocateSolutionRoot(string? startPath)
    {
        var normalizedPath = NormalizePath(startPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return null;
        }

        var directoryPath = Directory.Exists(normalizedPath)
            ? normalizedPath
            : Path.GetDirectoryName(normalizedPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        var directory = new DirectoryInfo(directoryPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TianShu.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
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

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.GetFullPath(path);
    }
}
