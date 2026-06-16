using System.IO;

namespace TianShu.Execution.Integration.Tests;

public sealed class TianShuExecutionRuntimeArchitectureTests
{
    [Fact]
    public void TestAssembly_ShouldUseNeutralExecutionIntegrationAssemblyName()
    {
        Assert.Equal(
            "TianShu.Execution.Integration.Tests",
            typeof(TianShuExecutionRuntimeArchitectureTests).Assembly.GetName().Name);
    }

    [Fact]
    public void TestProject_ShouldDeclareNeutralExecutionIntegrationRootNamespace()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "tests",
            "TianShu.Execution.Integration.Tests",
            "TianShu.Execution.Integration.Tests.csproj");

        var source = File.ReadAllText(projectFile);

        Assert.Contains(
            "<RootNamespace>TianShu.Execution.Integration.Tests</RootNamespace>",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TestSources_ShouldUseExecutionIntegrationRootNamespace()
    {
        var legacyNamespaceToken = "TianShu" + ".AgentRuntime.Tests";
        var legacyStaticUsingToken = "using static " + legacyNamespaceToken + ".";

        var testProjectDirectory = Path.Combine(
            FindRepoRoot(),
            "tests",
            "TianShu.Execution.Integration.Tests");

        var offenders = Directory
            .EnumerateFiles(testProjectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}FakeCliKernel{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file =>
            {
                var source = File.ReadAllText(file);
                return source.Contains($"namespace {legacyNamespaceToken}", StringComparison.Ordinal)
                    || source.Contains(legacyStaticUsingToken, StringComparison.Ordinal);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .OrderBy(static file => file, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"聚合测试工程源码不应继续保留旧根命名空间；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void DeprecatedSolution_ShouldBeRemoved()
    {
        var solutionFile = Path.Combine(
            FindRepoRoot(),
            "TianShu.Deperated.sln");

        Assert.False(File.Exists(solutionFile));
    }

    [Fact]
    public void TianShuExecutionRuntime_RuntimeNorthboundMainPath_DoesNotConstructLegacyAgentStreamEvent()
    {
        var runtimeFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.cs");

        var source = File.ReadAllText(runtimeFile);

        Assert.DoesNotContain("new AgentStreamEvent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TianShuExecutionRuntime_RuntimeNorthboundMainPath_DoesNotRetainAgentStreamEventRoundTripHelpers()
    {
        var runtimeFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.cs");

        var source = File.ReadAllText(runtimeFile);

        Assert.DoesNotContain("private void RaiseEvent(AgentStreamEvent streamEvent)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool ShouldSuppressTerminalTurnEvent(AgentStreamEvent streamEvent)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static AgentStreamEvent SanitizeDiagnostics(AgentStreamEvent streamEvent)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlPlaneConversationStreamEventCompatibility.ToAgentStreamEvent(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TianShuExecutionRuntime_RuntimeNorthboundMainPath_UsesControlPlaneConversationStreamEventKindFilters()
    {
        var runtimeFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.cs");

        var source = File.ReadAllText(runtimeFile);

        Assert.DoesNotContain("HashSet<AgentStreamEventKind>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("(AgentStreamEventKind)streamEvent.Kind", source, StringComparison.Ordinal);
        Assert.DoesNotContain("params AgentStreamEventKind[] kinds", source, StringComparison.Ordinal);
        Assert.DoesNotContain("(AgentStreamEventKind)projectedEvent.Kind", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentStreamEventKind.ToolCallStarted", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentStreamEventKind.ToolCallCompleted", source, StringComparison.Ordinal);
        Assert.Contains("HashSet<ControlPlaneConversationStreamEventKind>", source, StringComparison.Ordinal);
        Assert.Contains("params ControlPlaneConversationStreamEventKind[] kinds", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TianShuExecutionRuntime_RuntimeNorthboundCatalogAndArtifactSurface_UsesCanonicalLowerCaseMethodIds()
    {
        var runtimeFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.cs");

        var source = File.ReadAllText(runtimeFile);

        Assert.DoesNotContain("\"experimentalFeature/list\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"collaborationMode/list\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mcpServerStatus/list\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"config/mcpServer/reload\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"externalAgentConfig/detect\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"externalAgentConfig/import\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"artifact/conversationSummary/read\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"artifact/gitDiffToRemote/read\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentStreamEventCompatibility_CoreSource_IsSingleDirectionOnly()
    {
        var compatibilityFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "Events",
            "AgentStreamEvent.ControlPlaneCompatibility.cs");

        var source = File.ReadAllText(compatibilityFile);

        Assert.Contains(
            "public static ControlPlaneConversationStreamEvent ToControlPlaneConversationStreamEvent(AgentStreamEvent streamEvent)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("public static AgentStreamEvent ToAgentStreamEvent(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("implicit operator ControlPlaneConversationStreamEvent", source, StringComparison.Ordinal);
        Assert.DoesNotContain("implicit operator AgentStreamEvent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentRuntimeTests_Project_ShouldNotRetainDuplicatedStreamCompatibilityHelper()
    {
        var compatibilityFile = Path.Combine(
            FindRepoRoot(),
            "tests",
            "TianShu.Execution.Integration.Tests",
            "TestAgentStreamEventCompatibility.cs");

        Assert.False(File.Exists(compatibilityFile));
    }

    [Fact]
    public void AgentRuntime_RuntimeDirectory_DoesNotRetainLegacySessionSourceDefinitions()
    {
        var runtimeDirectory = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime");

        var legacySourceFile = Path.Combine(runtimeDirectory, "AgentSessionSource.cs");

        Assert.False(File.Exists(legacySourceFile));

        foreach (var sourceFile in Directory.EnumerateFiles(runtimeDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(sourceFile);

            Assert.DoesNotContain("internal sealed class AgentSessionSource", source, StringComparison.Ordinal);
            Assert.DoesNotContain("internal sealed class AgentThreadSourceKind", source, StringComparison.Ordinal);
            Assert.DoesNotContain("internal sealed class AgentSubAgentSource", source, StringComparison.Ordinal);
        }
    }

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
