using TianShu.Contracts.Configuration;

namespace TianShu.Cli;

internal sealed record CliRuntimeWriteCheckResult(
    bool Available,
    string TianShuHome,
    string RuntimeWorkspaceRoot,
    string? FailureCode,
    string? FailureMessage);

internal static class CliRuntimeWriteGuard
{
    public const string RuntimeNotWritableCode = "tian_shu_home_runtime_not_writable";

    public static CliRuntimeWriteCheckResult CheckKernelRuntimeWorkspace(
        string configFilePath,
        string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var home = ResolveTianShuHomeFromConfig(configFilePath);
        var runtimeRoot = TianShuRuntimeLayoutPaths.ResolveRuntimeWorkspacePathFromHome(
            home,
            "kernel-runtime",
            workingDirectory);
        return CheckWritable(home, runtimeRoot);
    }

    public static CliRuntimeWriteCheckResult CheckTianShuHomeRuntimePath(
        string tianShuHome,
        string workingDirectory,
        string area)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tianShuHome);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(area);

        var runtimeRoot = TianShuRuntimeLayoutPaths.ResolveRuntimeWorkspacePathFromHome(
            tianShuHome,
            area,
            workingDirectory);
        return CheckWritable(Path.GetFullPath(tianShuHome), runtimeRoot);
    }

    public static string ResolveTianShuHomeFromConfig(string configFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(configFilePath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"无法解析 TianShuHome：{configFilePath}");
        }

        return directory;
    }

    private static CliRuntimeWriteCheckResult CheckWritable(string tianShuHome, string runtimeWorkspaceRoot)
    {
        try
        {
            Directory.CreateDirectory(runtimeWorkspaceRoot);
            var probePath = Path.Combine(runtimeWorkspaceRoot, $".write-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "tianshu runtime write probe");
            File.Delete(probePath);
            return new CliRuntimeWriteCheckResult(true, tianShuHome, runtimeWorkspaceRoot, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new CliRuntimeWriteCheckResult(
                false,
                tianShuHome,
                runtimeWorkspaceRoot,
                RuntimeNotWritableCode,
                $"TianShuHome runtime root is not writable. Move the portable package to a writable directory or fix permissions. 天枢运行目录不可写，请将便携包移动到可写目录，或修正目录权限。Path: {runtimeWorkspaceRoot}");
        }
    }
}
