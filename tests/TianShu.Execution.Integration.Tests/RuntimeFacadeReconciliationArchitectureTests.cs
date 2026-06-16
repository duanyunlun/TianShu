using System.IO;
using System.Text.RegularExpressions;

namespace TianShu.Execution.Integration.Tests;

public sealed class RuntimeFacadeReconciliationArchitectureTests
{
    private static readonly Regex AgentRuntimeFacadeDtoDeclarationRegex = new(
        @"(?m)^\s*(?:public|internal|file|sealed|abstract|static|partial|\s)*(?:record|class)\s+(?<typeName>AgentRuntime[A-Za-z0-9_]*(?:Request|Result|Details|Summary|Catalog|Snapshot))\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex NonInterfaceDeclarationRegex = new(
        @"(?m)^\s*(?:public|internal|file|sealed|abstract|static|partial|\s)*(?:record|class)\s+\w+\b",
        RegexOptions.CultureInvariant);

    [Fact]
    public void ProductionSources_DoNotDefineResidualAgentRuntimeFacadeDtos()
    {
        var repoRoot = FindRepoRoot();
        var sourceRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.AgentRuntime"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar"),
            Path.Combine(repoRoot, "src", "Core", "TianShu.ControlPlane.Abstractions"),
        };
        var allowedTypeNames = new HashSet<string>(StringComparer.Ordinal);

        var offenders = sourceRoots
            .Where(Directory.Exists)
            .SelectMany(EnumerateSourceFiles)
            .SelectMany(file => AgentRuntimeFacadeDtoDeclarationRegex
                .Matches(File.ReadAllText(file))
                .Select(match => new
                {
                    File = Path.GetRelativePath(repoRoot, file),
                    TypeName = match.Groups["typeName"].Value,
                }))
            .Where(item => !allowedTypeNames.Contains(item.TypeName))
            .Select(item => $"{item.TypeName}:{item.File}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"生产源码中不应再定义残留的 AgentRuntime façade DTO；当前违规项：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void ControlPlaneAbstractions_Sources_DefineOnlyInterfaces()
    {
        var repoRoot = FindRepoRoot();
        var abstractionsRoot = Path.Combine(repoRoot, "src", "Core", "TianShu.ControlPlane.Abstractions");

        var offenders = EnumerateSourceFiles(abstractionsRoot)
            .Where(file => NonInterfaceDeclarationRegex.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(repoRoot, file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"ControlPlane.Abstractions 不应再定义 DTO/实现类型；当前违规文件：{string.Join(", ", offenders)}");
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root)
        => Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TianShu.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到 TianShu.sln。");
    }
}
