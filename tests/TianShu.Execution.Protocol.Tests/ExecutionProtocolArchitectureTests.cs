using System.IO;

namespace TianShu.Execution.Protocol.Tests;

public sealed class ExecutionProtocolArchitectureTests
{
    [Fact]
    public void PrimarySolution_ShouldIncludeExecutionProtocolTestsProject()
    {
        var solutionFile = Path.Combine(FindRepoRoot(), "TianShu.sln");
        var source = File.ReadAllText(solutionFile);

        Assert.Contains(
            "\"TianShu.Execution.Protocol.Tests\", \"tests\\TianShu.Execution.Protocol.Tests\\TianShu.Execution.Protocol.Tests.csproj\"",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AgentRuntimeTests_Project_ShouldNotRetainExecutionProtocolArchitectureLock()
    {
        var oldFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Infrastructure",
            "TianShu.AgentRuntime.Tests",
            "ExecutionProtocolArchitectureTests.cs");

        Assert.False(File.Exists(oldFile));
    }

    [Fact]
    public void AppServerProtocolFiles_ShouldLiveUnderExecutionProtocolProject()
    {
        var repoRoot = FindRepoRoot();
        var oldDirectory = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.AgentRuntime",
            "Runtime",
            "Protocol");
        var newDirectory = Path.Combine(
            repoRoot,
            "src",
            "Execution",
            "TianShu.Execution.Protocol");
        var fileNames = new[]
        {
            "AppServerIncomingEnvelope.cs",
            "AppServerJsonHelpers.cs",
            "AppServerNotificationModels.cs",
            "AppServerProtocolParser.cs",
            "AppServerServerRequestModels.cs",
            "AppServerThreadModels.cs",
        };

        foreach (var fileName in fileNames)
        {
            Assert.False(
                File.Exists(Path.Combine(oldDirectory, fileName)),
                $"旧协议文件仍存在：{fileName}");
            Assert.True(
                File.Exists(Path.Combine(newDirectory, fileName)),
                $"新协议文件缺失：{fileName}");
        }
    }

    [Fact]
    public void ExecutionProtocolProject_ShouldOnlyExposeInternalsToExecutionRuntimeAndTests()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Protocol",
            "TianShu.Execution.Protocol.csproj");
        var source = File.ReadAllText(projectFile);

        Assert.Contains(
            "<InternalsVisibleTo Include=\"TianShu.Execution.Runtime\" />",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "<InternalsVisibleTo Include=\"TianShu.Execution.Integration.Tests\" />",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<InternalsVisibleTo Include=\"TianShu.AgentRuntime.Tests\" />",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<InternalsVisibleTo Include=\"TianShu.AgentRuntime\" />",
            source,
            StringComparison.Ordinal);
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
