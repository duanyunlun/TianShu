namespace TianShu.VSSDK.Sidecar.Tests;

public sealed class SidecarProjectBoundaryTests
{
    private static readonly string[] ForbiddenInternalRuntimeTokens =
    [
        "TianShu.Kernel",
        "CoreIntent",
        "StageGraph",
        "RuntimeStep",
        "ExecutionPlan",
        "StableKernelCore",
        "AdaptiveRuntimeExecutionLoop",
        "KernelRuntimeProductTerminalProjection",
    ];

    [Fact]
    public void PrimarySolution_ShouldIncludeSidecarTestsProject()
    {
        var solutionFile = Path.Combine(FindRepoRoot(), "TianShu.sln");
        var source = File.ReadAllText(solutionFile);

        Assert.Contains(
            "\"TianShu.VSSDK.Sidecar.Tests\", \"tests\\TianShu.VSSDK.Sidecar.Tests\\TianShu.VSSDK.Sidecar.Tests.csproj\"",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutionIntegrationProject_ShouldNotRetainSidecarSpecificTests()
    {
        var repoRoot = FindRepoRoot();
        var oldFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.AgentRuntime.Tests", "SidecarTypedSerializationTests.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.AgentRuntime.Tests", "VsixBuildDependencyTests.cs"),
        };

        Assert.All(oldFiles, static path => Assert.False(File.Exists(path), $"execution 聚合测试工程仍保留 sidecar 专属测试：{path}"));
    }

    [Fact]
    public void Sidecar_DoesNotReferenceKernelOrRuntimeInternalObjects()
    {
        var repoRoot = FindRepoRoot();
        var sidecarRoot = Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar");
        var violations = FindForbiddenTokens(repoRoot, sidecarRoot, ForbiddenInternalRuntimeTokens).ToList();
        var projectSource = File.ReadAllText(Path.Combine(sidecarRoot, "TianShu.VSSDK.Sidecar.csproj"));

        foreach (var token in ForbiddenInternalRuntimeTokens)
        {
            if (projectSource.Contains(token, StringComparison.Ordinal))
            {
                violations.Add($"src\\Presentations\\TianShu.VSSDK.Sidecar\\TianShu.VSSDK.Sidecar.csproj: forbidden project token '{token}'.");
            }
        }

        Assert.True(
            violations.Count == 0,
            "VSSDK.Sidecar 可以作为进程内 composition bridge 装配 runtime，但不得直接引用 Kernel / Runtime 内部对象。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Sidecar_RuntimeReferencesStayAtCompositionBoundary()
    {
        var repoRoot = FindRepoRoot();
        var projectPath = Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar", "TianShu.VSSDK.Sidecar.csproj");
        var projectSource = File.ReadAllText(projectPath);

        Assert.Contains(@"..\..\Core\TianShu.HostGateway\TianShu.HostGateway.csproj", projectSource, StringComparison.Ordinal);
        Assert.Contains(@"..\..\Core\TianShu.ControlPlane\TianShu.ControlPlane.csproj", projectSource, StringComparison.Ordinal);
        Assert.Contains(@"..\..\Core\TianShu.RuntimeComposition\TianShu.RuntimeComposition.csproj", projectSource, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\TianShu.Kernel", projectSource, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\TianShu.Kernel.", projectSource, StringComparison.Ordinal);
    }

    [Fact]
    public void VsixExtension_DoesNotReferenceCoreRuntimeProjectsOrInternalObjects()
    {
        var repoRoot = FindRepoRoot();
        var vsixRoot = Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.VSExtension");
        var projectPath = Path.Combine(vsixRoot, "TianShu.VSSDK.VSExtension.csproj");
        var violations = FindForbiddenTokens(repoRoot, vsixRoot, ForbiddenInternalRuntimeTokens).ToList();
        var projectSource = File.ReadAllText(projectPath);
        var forbiddenProjectReferences = new[]
        {
            "TianShu.Kernel",
            "TianShu.RuntimeComposition",
            "TianShu.Execution.Runtime",
            "TianShu.HostGateway",
            "TianShu.ControlPlane",
        };

        foreach (var token in forbiddenProjectReferences.Concat(ForbiddenInternalRuntimeTokens))
        {
            if (projectSource.Contains(token, StringComparison.Ordinal))
            {
                violations.Add($"{Path.GetRelativePath(repoRoot, projectPath)}: forbidden project token '{token}'.");
            }
        }

        Assert.True(
            violations.Count == 0,
            "VSSDK.VSExtension 只能通过 sidecar typed protocol 发送宿主操作和消费投影，不得引用核心 runtime 项目或内部对象。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> FindForbiddenTokens(string repoRoot, string sourceRoot, IReadOnlyList<string> forbiddenTokens)
    {
        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGeneratedOutput(path))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(repoRoot, path);
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                foreach (var token in forbiddenTokens)
                {
                    if (line.Contains(token, StringComparison.Ordinal))
                    {
                        yield return $"{relativePath}:{index + 1}: forbidden token '{token}' in '{line.Trim()}'.";
                    }
                }
            }
        }
    }

    private static bool IsGeneratedOutput(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TianShu.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
