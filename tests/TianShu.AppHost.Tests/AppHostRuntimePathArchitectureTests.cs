using System.Text.RegularExpressions;

namespace TianShu.AppHost.Tests;

public sealed class AppHostRuntimePathArchitectureTests
{
    [Fact]
    public void McpServer_ShouldBridgeThroughAppServerProcess()
    {
        var sourcePath = Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost", "AppHostMcpServer.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("ProcessAppHostServerBridge", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InProcessAppHostServerBridge", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new AppHostServer(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHost_ShouldDelegateTurnDispatchConstructionThroughExecutionRuntimeFactory()
    {
        var repoRoot = FindRepoRoot();
        var appHostRoot = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost");
        var dispatchRuntimeCompositionPath = Path.Combine(
            repoRoot,
            "src",
            "Core",
            "TianShu.RuntimeComposition",
            "StageExecutorDispatchRuntimeComposition.cs");
        var routeDispatchPlannerPath = Path.Combine(
            repoRoot,
            "src",
            "Core",
            "TianShu.RuntimeComposition",
            "AppHostCoreLoopRouteDispatchPlanner.cs");
        var routingEntryPlannerSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Core",
            "TianShu.RuntimeComposition",
            "AppHostCoreLoopRoutingEntryPlanner.cs"));
        var appHostSource = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(appHostRoot, "*.cs", SearchOption.AllDirectories)
                .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText));

        Assert.False(File.Exists(dispatchRuntimeCompositionPath), "RuntimeComposition must not reintroduce a dedicated StageExecutor dispatch pure-forwarding bridge.");
        Assert.False(File.Exists(routeDispatchPlannerPath), "RuntimeComposition must not reintroduce a dedicated route dispatch planner bridge.");
        Assert.Contains("SessionCoreLoopRoutingPlanFactory.Plan(", routingEntryPlannerSource, StringComparison.Ordinal);
        Assert.Contains("TurnExecutionDispatchContextFactory.FromExecutionEntry(", routingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorDispatchPlanFactory.Bind(", routingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorDispatcher.FromStageDefinitions(", routingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AppHostStageExecutorDispatchBinder", appHostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorDispatcher.FromStageDefinitions(", appHostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostExecutable_ShouldKeepKernelDependencyBehindRuntimeComposition()
    {
        var repoRoot = FindRepoRoot();
        var appHostRoot = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost");
        var projectSource = File.ReadAllText(Path.Combine(appHostRoot, "TianShu.AppHost.csproj"));
        var runtimeCompositionProjectSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Core",
            "TianShu.RuntimeComposition",
            "TianShu.RuntimeComposition.csproj"));
        var appHostSource = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(appHostRoot, "*.cs", SearchOption.AllDirectories)
                .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText));

        Assert.Contains(
            "<ProjectReference Include=\"..\\..\\Core\\TianShu.RuntimeComposition\\TianShu.RuntimeComposition.csproj\" />",
            projectSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("TianShu.Kernel.csproj", projectSource, StringComparison.Ordinal);
        Assert.Contains(
            "<ProjectReference Include=\"..\\TianShu.Kernel\\TianShu.Kernel.csproj\" />",
            runtimeCompositionProjectSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("using TianShu.Kernel;", appHostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel", appHostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHost_ShouldRestrictMutableStateReferencesToHostRootAndMigrationBridges()
    {
        var repoRoot = FindRepoRoot();
        var appHostRoot = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost");
        var mutableStateTypes = new[]
        {
            "KernelThreadStore",
            "KernelThreadManager",
            "KernelAgentOrchestrationManager",
            "KernelQueuePair",
            "KernelStateSqliteStore",
            "KernelRolloutRecorder",
            "KernelSpawnAgentGuardState",
        };
        var allowedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "src/Hosting/TianShu.AppHost/AppHostMcpServer.cs",
            "src/Hosting/TianShu.AppHost/AppHostServer.cs",
            "src/Hosting/TianShu.AppHost/AppHostWebSocketTransportHost.cs",
            "src/Hosting/TianShu.AppHost/Program.cs",
        };
        var violations = Directory
            .EnumerateFiles(appHostRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(file => new
            {
                RelativePath = NormalizeRelativePath(repoRoot, file),
                Source = File.ReadAllText(file),
            })
            .Where(file => mutableStateTypes.Any(type => ContainsIdentifier(file.Source, type)))
            .Where(file => !allowedFiles.Contains(file.RelativePath))
            .Select(file => file.RelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "AppHost 对可变状态对象的直接引用只能保留在宿主组合根、transport 入口和显式迁移 bridge 中。违规文件："
            + string.Join(", ", violations));
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

    private static string NormalizeRelativePath(string repoRoot, string path)
        => Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private static bool ContainsIdentifier(string source, string identifier)
        => Regex.IsMatch(
            source,
            $@"(?<![A-Za-z0-9_]){Regex.Escape(identifier)}(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant);
}
