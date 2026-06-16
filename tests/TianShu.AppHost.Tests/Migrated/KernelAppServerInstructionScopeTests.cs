using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostServerInstructionScopeTests
{
    [Fact]
    public void BuildScopedDeveloperInstructions_ShouldReturnConfiguredDeveloperInstructionsOnly()
    {
        var root = CreateTempDirectory();
        var workspaceRoot = Path.Combine(root, "workspace");

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".git"));
            File.WriteAllText(Path.Combine(workspaceRoot, "AGENTS.md"), "workspace agents");

            var combined = AppHostServer.BuildScopedDeveloperInstructions(
                workspaceRoot,
                configuredDeveloperInstructions: "configured developer instructions");

            Assert.Equal("configured developer instructions", combined);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void BuildScopedUserInstructions_ShouldPreferHomeAgentsOverrideOverAgents()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var emptyWorkspace = Path.Combine(root, "empty-workspace");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(emptyWorkspace);
            File.WriteAllText(Path.Combine(tianShuHome, "AGENTS.md"), "home agents");
            File.WriteAllText(Path.Combine(tianShuHome, "AGENTS.override.md"), "home override");

            var combined = AppHostServer.BuildScopedUserInstructions(emptyWorkspace);

            Assert.Equal("home override", combined);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void BuildScopedUserInstructions_ShouldCombineHomeAndProjectDocsWithSeparator()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var workspaceRoot = Path.Combine(root, "workspace");
        var nestedWorkspace = Path.Combine(workspaceRoot, "src", "feature");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".git"));
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(Path.Combine(tianShuHome, "AGENTS.md"), "home agents");
            File.WriteAllText(Path.Combine(workspaceRoot, "AGENTS.md"), "workspace agents");
            File.WriteAllText(Path.Combine(nestedWorkspace, "AGENTS.md"), "nested agents");

            var combined = AppHostServer.BuildScopedUserInstructions(nestedWorkspace);

            Assert.NotNull(combined);
            Assert.Contains("home agents", combined!, StringComparison.Ordinal);
            Assert.Contains("--- project-doc ---", combined!, StringComparison.Ordinal);
            AssertOrdered(combined!, "home agents", "workspace agents");
            AssertOrdered(combined!, "workspace agents", "nested agents");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void BuildScopedUserInstructions_ShouldUseConfiguredFallbackFileNames()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var workspaceRoot = Path.Combine(root, "workspace");
        var nestedWorkspace = Path.Combine(workspaceRoot, "src", "feature");

        try
        {
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".git"));
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(Path.Combine(workspaceRoot, "RULES.md"), "workspace fallback instructions");

            var config = CreateInstructionConfig(projectDocFallbackFilenames: ["RULES.md"]);
            var combined = AppHostServer.BuildScopedUserInstructions(
                nestedWorkspace,
                config,
                tianShuHome);

            Assert.NotNull(combined);
            Assert.Contains("workspace fallback instructions", combined!, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void BuildScopedUserInstructions_ShouldRespectProjectRootMarkers()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var parentWorkspace = Path.Combine(root, "parent");
        var projectRoot = Path.Combine(parentWorkspace, "workspace");
        var nestedWorkspace = Path.Combine(projectRoot, "src", "feature");

        try
        {
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(parentWorkspace);
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(Path.Combine(parentWorkspace, "AGENTS.md"), "parent instructions");
            File.WriteAllText(Path.Combine(projectRoot, ".project-root"), string.Empty);
            File.WriteAllText(Path.Combine(projectRoot, "AGENTS.md"), "project root instructions");
            File.WriteAllText(Path.Combine(nestedWorkspace, "AGENTS.md"), "nested instructions");

            var config = CreateInstructionConfig(projectRootMarkers: [".project-root"]);
            var combined = AppHostServer.BuildScopedUserInstructions(
                nestedWorkspace,
                config,
                tianShuHome);

            Assert.NotNull(combined);
            Assert.DoesNotContain("parent instructions", combined!, StringComparison.Ordinal);
            AssertOrdered(combined!, "project root instructions", "nested instructions");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void BuildScopedUserInstructions_ShouldTreatEmptyProjectRootMarkersAsStopAtCwd()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var parentWorkspace = Path.Combine(root, "parent");
        var projectRoot = Path.Combine(parentWorkspace, "workspace");
        var nestedWorkspace = Path.Combine(projectRoot, "src", "feature");

        try
        {
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(parentWorkspace, ".git"));
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(Path.Combine(parentWorkspace, "AGENTS.md"), "parent instructions");
            File.WriteAllText(Path.Combine(projectRoot, "AGENTS.md"), "project root instructions");
            File.WriteAllText(Path.Combine(nestedWorkspace, "AGENTS.md"), "nested instructions");

            var config = CreateInstructionConfig(projectRootMarkers: []);
            var combined = AppHostServer.BuildScopedUserInstructions(
                nestedWorkspace,
                config,
                tianShuHome);

            Assert.NotNull(combined);
            Assert.DoesNotContain("parent instructions", combined!, StringComparison.Ordinal);
            Assert.DoesNotContain("project root instructions", combined!, StringComparison.Ordinal);
            Assert.Contains("nested instructions", combined!, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void BuildScopedUserInstructions_ShouldRespectProjectDocMaxBytes()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var workspaceRoot = Path.Combine(root, "workspace");

        try
        {
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".git"));
            File.WriteAllText(Path.Combine(workspaceRoot, "AGENTS.md"), "0123456789ABCDEF");

            var truncateConfig = CreateInstructionConfig(projectDocMaxBytes: 5);
            var truncated = AppHostServer.BuildScopedUserInstructions(
                workspaceRoot,
                truncateConfig,
                tianShuHome);

            Assert.NotNull(truncated);
            Assert.Contains("01234", truncated!, StringComparison.Ordinal);
            Assert.DoesNotContain("012345", truncated!, StringComparison.Ordinal);

            var disabledConfig = CreateInstructionConfig(projectDocMaxBytes: 0);
            var disabled = AppHostServer.BuildScopedUserInstructions(
                workspaceRoot,
                disabledConfig,
                tianShuHome);

            Assert.Null(disabled);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void BuildScopedUserInstructions_ShouldReadProjectDocThroughSymlink()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var workspaceRoot = Path.Combine(root, "workspace");
        var sharedRoot = Path.Combine(root, "shared");

        try
        {
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".git"));
            Directory.CreateDirectory(sharedRoot);
            File.WriteAllText(Path.Combine(sharedRoot, "shared-agents.md"), "symlinked instructions");

            var linkPath = Path.Combine(workspaceRoot, "AGENTS.md");
            try
            {
                File.CreateSymbolicLink(linkPath, Path.Combine(sharedRoot, "shared-agents.md"));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException or NotSupportedException)
            {
                return;
            }

            var combined = AppHostServer.BuildScopedUserInstructions(workspaceRoot, null, tianShuHome);

            Assert.NotNull(combined);
            Assert.Contains("symlinked instructions", combined!, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static Dictionary<string, object?> CreateInstructionConfig(
        string[]? projectDocFallbackFilenames = null,
        string[]? projectRootMarkers = null,
        long? projectDocMaxBytes = null)
    {
        var config = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (projectDocFallbackFilenames is not null)
        {
            config["project_doc_fallback_filenames"] = projectDocFallbackFilenames
                .Cast<object?>()
                .ToList();
        }

        if (projectRootMarkers is not null)
        {
            config["project_root_markers"] = projectRootMarkers
                .Cast<object?>()
                .ToList();
        }

        if (projectDocMaxBytes is not null)
        {
            config["project_doc_max_bytes"] = projectDocMaxBytes.Value;
        }

        return config;
    }

    private static void AssertOrdered(string text, string first, string second)
    {
        var firstIndex = text.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = text.IndexOf(second, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"未找到片段：{first}");
        Assert.True(secondIndex >= 0, $"未找到片段：{second}");
        Assert.True(firstIndex < secondIndex, $"片段顺序不符合预期：{first} 应位于 {second} 之前。");
    }

    private static string CreateTempDirectory()
    {
        var root = Path.GetPathRoot(AppContext.BaseDirectory) ?? Path.GetTempPath();
        var path = Path.Combine(root, "TianShuTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
