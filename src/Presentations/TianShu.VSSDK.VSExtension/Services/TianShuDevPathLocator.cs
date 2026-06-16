using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace TianShu.VSSDK.VSExtension.Services;

[SuppressMessage("Usage", "VSTHRD010:Invoke single-threaded types on Main thread", Justification = "仅 ResolveCurrentSolutionDirectory 访问 VS 单线程服务，内部已自行兜底。")]
internal static class TianShuDevPathLocator
{
    public static string ResolveDefaultWorkingDirectory()
        => ResolveCurrentSolutionDirectory()
           ?? ResolveRepositoryRoot(Environment.CurrentDirectory)
           ?? Environment.CurrentDirectory;

    public static string ResolveTianShuConfigPath()
    {
        var repoRoot = ResolveRepositoryRoot(Environment.CurrentDirectory) ?? ResolveCurrentSolutionDirectory();
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            var workspaceConfig = Path.Combine(repoRoot, ".tianshu", "tianshu.toml");
            if (File.Exists(workspaceConfig))
            {
                return workspaceConfig;
            }
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".tianshu", "tianshu.toml");
    }

    public static string ResolveTianShuHomePath()
    {
        var configured = Normalize(Environment.GetEnvironmentVariable("TIANSHU_HOME"));
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
        var configured = Normalize(Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME"));
        return configured ?? Path.Combine(ResolveTianShuHomePath(), "data", "state");
    }

    public static string ResolveTianShuSessionsRootPath()
    {
        var configured = Normalize(Environment.GetEnvironmentVariable("TIANSHU_SESSIONS_HOME"));
        return configured ?? Path.Combine(ResolveTianShuHomePath(), "data", "sessions");
    }

    public static string? ResolveRepositoryRoot(string? preferredPath = null)
    {
        foreach (var candidate in EnumerateProbeDirectories(preferredPath))
        {
            var resolved = SearchForRepositoryRoot(candidate);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    public static string? ResolveAppHostProjectPath(string? preferredPath = null)
    {
        var root = ResolveRepositoryRoot(preferredPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var path = Path.Combine(root, "src", "Hosting", "TianShu.AppHost", "TianShu.AppHost.csproj");
        return File.Exists(path) ? path : null;
    }

    public static string? ResolveSidecarProjectPath(string? preferredPath = null)
    {
        var root = ResolveRepositoryRoot(preferredPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var path = Path.Combine(root, "src", "Presentations", "TianShu.VSSDK.Sidecar", "TianShu.VSSDK.Sidecar.csproj");
        return File.Exists(path) ? path : null;
    }

    private static string? ResolveCurrentSolutionDirectory()
    {
        try
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Package.GetGlobalService(typeof(SVsSolution)) is not IVsSolution solution)
            {
                return null;
            }

            solution.GetSolutionInfo(out var solutionDirectory, out _, out _);
            return string.IsNullOrWhiteSpace(solutionDirectory) ? null : solutionDirectory;
        }
        catch
        {
            return null;
        }
    }

    private static string? SearchForRepositoryRoot(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        var directoryPath = Directory.Exists(startPath)
            ? startPath
            : Path.GetDirectoryName(startPath);
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

    private static IEnumerable<string?> EnumerateProbeDirectories(string? preferredPath)
    {
        yield return preferredPath;
        yield return ResolveCurrentSolutionDirectory();
        yield return Environment.CurrentDirectory;
        yield return AppContext.BaseDirectory;

        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            yield return assemblyDirectory;
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
