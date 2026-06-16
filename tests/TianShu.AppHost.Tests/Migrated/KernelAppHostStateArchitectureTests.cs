using System.IO;

namespace TianShu.AppHost.Tests;

public sealed class KernelAppHostStateArchitectureTests
{
    [Fact]
    public void KernelStorageAndSqliteState_ShouldLiveUnderAppHostStateProject()
    {
        var repoRoot = FindRepoRoot();
        var oldStoragePathsFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelStoragePaths.cs");
        var oldSqliteStoreFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelStateSqliteStore.cs");
        var newStoragePathsFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.State",
            "KernelStoragePaths.cs");
        var newSqliteStoreFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.State",
            "KernelStateSqliteStore.cs");

        Assert.False(File.Exists(oldStoragePathsFile));
        Assert.False(File.Exists(oldSqliteStoreFile));
        Assert.True(File.Exists(newStoragePathsFile));
        Assert.True(File.Exists(newSqliteStoreFile));
    }

    [Fact]
    public void KernelProject_ShouldReferenceAppHostStateProject()
    {
        var repoRoot = FindRepoRoot();
        var projectFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "TianShu.AppHost.csproj");
        var deletedKernelProjectFile = GetDeletedKernelProjectFilePath(repoRoot);

        var source = File.ReadAllText(projectFile);
        _ = AssertFileDeletedAndReturnEmpty(deletedKernelProjectFile);

        Assert.Contains(
            "<ProjectReference Include=\"..\\TianShu.AppHost.State\\TianShu.AppHost.State.csproj\" />",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyKernelInfrastructureDirectories_ShouldBeRemoved()
    {
        var repoRoot = FindRepoRoot();
        var legacyDirectories = new[]
        {
            Path.Combine(repoRoot, "src", "Infrastructure"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel"),
        };

        Assert.All(
            legacyDirectories,
            directory => Assert.False(
                Directory.Exists(directory),
                $"旧 Kernel 过渡目录不应继续保留：{Path.GetRelativePath(repoRoot, directory)}"));
    }

    [Fact]
    public void KernelRolloutRecorder_ShouldLiveUnderAppHostStateProject()
    {
        var repoRoot = FindRepoRoot();
        var oldRolloutRecorderFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelRolloutRecorder.cs");
        var newRolloutRecorderFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.State",
            "KernelRolloutRecorder.cs");
        var rolloutModelsFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.State",
            "KernelRolloutModels.cs");

        Assert.False(File.Exists(oldRolloutRecorderFile));
        Assert.True(File.Exists(newRolloutRecorderFile));
        Assert.True(File.Exists(rolloutModelsFile));
    }

    [Fact]
    public void KernelRolloutStateMapper_ShouldLiveUnderAppHostStateProject()
    {
        var repoRoot = FindRepoRoot();
        var oldMapperFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelRolloutStateMapper.cs");
        var newMapperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.State",
            "KernelRolloutStateMapper.cs");

        Assert.False(File.Exists(oldMapperFile));
        Assert.True(File.Exists(newMapperFile));
    }

    [Fact]
    public void KernelThreadStore_ShouldLiveUnderAppHostStateProject()
    {
        var repoRoot = FindRepoRoot();
        var oldThreadStoreFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelThreadStore.cs");
        var newThreadStoreFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.State",
            "KernelThreadStore.cs");

        Assert.False(File.Exists(oldThreadStoreFile));
        Assert.True(File.Exists(newThreadStoreFile));

        var source = File.ReadAllText(newThreadStoreFile);
        Assert.Contains("internal sealed record KernelThreadListQuery(", source, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelThreadListPage(", source, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelThreadStore", source, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelSpawnAgentGuardState", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelAgentOrchestrationManager_ShouldLiveUnderAppHostStateProject()
    {
        var repoRoot = FindRepoRoot();
        var oldManagerFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelAgentOrchestrationManager.cs");
        var newManagerFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.State",
            "KernelAgentOrchestrationManager.cs");

        Assert.False(File.Exists(oldManagerFile));
        Assert.True(File.Exists(newManagerFile));

        var source = File.ReadAllText(newManagerFile);
        Assert.Contains("namespace TianShu.AppHost.State;", source, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelAgentJobProgressSnapshot(", source, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelAgentOrchestrationManager", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelAgentJobRuntimeHelpers_ShouldLiveUnderAppHostStateProject()
    {
        var repoRoot = FindRepoRoot();
        var agentJobsRuntimeFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.AgentJobsToolRuntime.cs");
        var runtimeHelpersFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.State",
            "KernelAgentJobRuntimeHelpers.cs");
        var spawnCsvRuntimeFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelSpawnAgentsOnCsvAppHostRuntime.cs");

        Assert.False(File.Exists(agentJobsRuntimeFile));
        Assert.True(File.Exists(runtimeHelpersFile));
        Assert.True(File.Exists(spawnCsvRuntimeFile));

        var runtimeSource = File.ReadAllText(runtimeHelpersFile);
        var spawnCsvRuntimeSource = File.ReadAllText(spawnCsvRuntimeFile);

        Assert.Contains("namespace TianShu.AppHost.State;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelAgentJobActiveWorker", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelAgentJobProgressEmitter", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelAgentJobActiveWorker", spawnCsvRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed class KernelAgentJobProgressEmitter", spawnCsvRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelThreadHistoryRuntimeHelpers_ShouldLiveUnderAppHostStateProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelThreadHistoryFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.ThreadHistory.cs");
        var runtimeHelpersFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.State",
            "KernelThreadHistoryRuntimeHelpers.cs");

        Assert.False(File.Exists(kernelThreadHistoryFile));
        Assert.True(File.Exists(runtimeHelpersFile));

        var runtimeSource = File.ReadAllText(runtimeHelpersFile);

        Assert.Contains("namespace TianShu.AppHost.State;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelTrackedTurnHistory", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnHistoryAccumulator", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelThreadExecutionState", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelSpawnAgentGuardRuntimeHelpers_ShouldLiveUnderAppHostStateProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAgentGuardsFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.AgentGuards.cs");
        var runtimeHelpersFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.State",
            "KernelSpawnAgentGuardRuntimeHelpers.cs");
        var runtimePath = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.State",
            "KernelSpawnAgentGuardAppHostRuntime.cs");
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);

        Assert.False(File.Exists(kernelAgentGuardsFile));
        Assert.True(File.Exists(runtimeHelpersFile));
        Assert.True(File.Exists(runtimePath));

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var runtimeSource = File.ReadAllText(runtimeHelpersFile);
        var appHostRuntimeSource = File.ReadAllText(runtimePath);

        Assert.Contains("namespace TianShu.AppHost.State;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelSpawnAgentGuardConfiguration", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelSpawnSlotReservation", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelSpawnAgentGuardAppHostRuntime spawnAgentGuardAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("this.spawnAgentGuardAppHostRuntime = new KernelSpawnAgentGuardAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("spawnAgentGuardAppHostRuntime.ResolveSpawnAgentGuardConfigurationAsync", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("spawnAgentGuardAppHostRuntime.ReserveSpawnAgentSlot", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("spawnAgentGuardAppHostRuntime.IsTrackedSpawnAgentThread", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("spawnAgentGuardAppHostRuntime.ReleaseSpawnedAgentThread", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.State;", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelSpawnAgentGuardAppHostRuntime", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelSpawnAgentGuardConfiguration> ResolveSpawnAgentGuardConfigurationAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public KernelSpawnSlotReservation ReserveSpawnAgentSlot(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public bool IsTrackedSpawnAgentThread(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public void ReleaseSpawnedAgentThread(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public static int GetNextThreadSpawnDepth(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public static int ResolveConfiguredSpawnAgentPositiveInt(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", appHostRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostStateSources_ShouldUseAppHostStateNamespace()
    {
        var files = new[]
        {
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.State", "KernelRolloutModels.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.State", "KernelStoragePaths.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.State", "KernelStateSqliteStore.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.State", "KernelRolloutRecorder.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.State", "KernelRolloutStateMapper.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.State", "KernelThreadStore.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.State", "KernelAgentOrchestrationManager.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.State", "KernelAgentJobRuntimeHelpers.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.State", "KernelThreadHistoryRuntimeHelpers.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.State", "KernelSpawnAgentGuardRuntimeHelpers.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.State", "KernelSpawnAgentGuardAppHostRuntime.cs"),
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);

            Assert.Contains("namespace TianShu.AppHost.State;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AppHostStateProject_ShouldReferenceExecutionContracts()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.State",
            "TianShu.AppHost.State.csproj");

        var source = File.ReadAllText(projectFile);

        Assert.Contains(
            "<ProjectReference Include=\"..\\..\\Contracts\\TianShu.Contracts.Execution\\TianShu.Contracts.Execution.csproj\" />",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "<ProjectReference Include=\"..\\..\\Execution\\TianShu.Execution.Runtime\\TianShu.Execution.Runtime.csproj\" />",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostStateProject_ShouldExposeInternalsToAppHostToolsRuntime()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.State",
            "TianShu.AppHost.State.csproj");

        var source = File.ReadAllText(projectFile);

        Assert.Contains(
            "<InternalsVisibleTo Include=\"TianShu.AppHost.Tools.Runtime\" />",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostTestsProject_ShouldOwnKernelThreadStoreTests()
    {
        var repoRoot = FindRepoRoot();
        var projectFile = Path.Combine(
            repoRoot,
            "tests",
            "TianShu.AppHost.Tests",
            "TianShu.AppHost.Tests.csproj");
        var migratedFile = Path.Combine(repoRoot, "tests", "TianShu.AppHost.Tests", "Migrated", "KernelThreadStoreTests.cs");

        var source = File.ReadAllText(projectFile);

        Assert.True(File.Exists(migratedFile));
        Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests\\KernelThreadStoreTests.cs", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostTestsProject_ShouldOwnKernelAgentOrchestrationManagerTests()
    {
        var repoRoot = FindRepoRoot();
        var projectFile = Path.Combine(
            repoRoot,
            "tests",
            "TianShu.AppHost.Tests",
            "TianShu.AppHost.Tests.csproj");
        var migratedFile = Path.Combine(repoRoot, "tests", "TianShu.AppHost.Tests", "Migrated", "KernelAgentOrchestrationManagerTests.cs");

        var source = File.ReadAllText(projectFile);

        Assert.True(File.Exists(migratedFile));
        Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests\\KernelAgentOrchestrationManagerTests.cs", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelShellHelpers_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var oldShellCommandBuilderFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelShellCommandBuilder.cs");
        var oldCommandSafetyClassifierFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelCommandSafetyClassifier.cs");
        var newShellCommandBuilderFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelShellCommandBuilder.cs");
        var newCommandSafetyClassifierFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelCommandSafetyClassifier.cs");

        Assert.False(File.Exists(oldShellCommandBuilderFile));
        Assert.False(File.Exists(oldCommandSafetyClassifierFile));
        Assert.True(File.Exists(newShellCommandBuilderFile));
        Assert.True(File.Exists(newCommandSafetyClassifierFile));
    }

    [Fact]
    public void KernelProject_ShouldReferenceAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var projectFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "TianShu.AppHost.csproj");
        var deletedKernelProjectFile = GetDeletedKernelProjectFilePath(repoRoot);

        var source = File.ReadAllText(projectFile);
        _ = AssertFileDeletedAndReturnEmpty(deletedKernelProjectFile);

        Assert.Contains(
            "<ProjectReference Include=\"..\\TianShu.AppHost.Tools\\TianShu.AppHost.Tools.csproj\" />",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void KernelShellExecutionHelpers_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var appHostToolsDirectory = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools");

        var expectedFiles = new[]
        {
            "KernelExecOutputFormatting.cs",
            "KernelExecToolCallOutput.cs",
            "KernelExecRunner.cs",
            "KernelShellToolSchemaFactory.cs",
            "KernelTextTruncator.cs",
            "KernelToolOutputContentItem.cs",
        };

        foreach (var fileName in expectedFiles)
        {
            Assert.True(File.Exists(Path.Combine(appHostToolsDirectory, fileName)), $"缺少文件: {fileName}");
        }
    }

    [Fact]
    public void KernelLegacyShellHelperDefinitions_ShouldNotRemainInKernelSources()
    {
        var repoRoot = FindRepoRoot();
        var shellToolHandlersFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "ShellToolHandlers.cs");
        var kernelToolsFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelTools.cs");

        var kernelToolsSource = AssertFileDeletedAndReturnEmpty(kernelToolsFile);

        Assert.False(File.Exists(shellToolHandlersFile));
        Assert.DoesNotContain("internal sealed record KernelToolOutputContentItem", kernelToolsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostToolsShellExecutionSources_ShouldUseAppHostToolsNamespace()
    {
        var files = new[]
        {
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Tools", "KernelExecOutputFormatting.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Tools", "KernelExecToolCallOutput.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Tools", "KernelExecRunner.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Tools", "KernelShellToolSchemaFactory.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Tools", "KernelTextTruncator.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Tools", "KernelToolOutputContentItem.cs"),
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);

            Assert.Contains("namespace TianShu.AppHost.Tools;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KernelToolArgumentTypes_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var oldArgumentTypesFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelToolArgumentTypes.cs");
        var newArgumentTypesFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelToolArgumentTypes.cs");

        Assert.False(File.Exists(oldArgumentTypesFile));
        Assert.True(File.Exists(newArgumentTypesFile));
    }

    [Fact]
    public void AppHostToolsArgumentSources_ShouldUseAppHostToolsNamespace()
    {
        var argumentTypesFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelToolArgumentTypes.cs");

        var source = File.ReadAllText(argumentTypesFile);

        Assert.Contains("namespace TianShu.AppHost.Tools;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelPermissionAndApprovalPrimitives_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var oldPermissionProfilesFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelPermissionGrantProfiles.cs");
        var oldCommandApprovalResolverFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelToolCommandApprovalResolver.cs");
        var permissionProfilesFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelPermissionGrantProfiles.cs");
        var commandApprovalResolverFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelToolCommandApprovalResolver.cs");
        var toolJsonHelpersFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelToolJsonHelpers.cs");
        var kernelToolsFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelTools.cs");

        Assert.False(File.Exists(oldPermissionProfilesFile));
        Assert.False(File.Exists(oldCommandApprovalResolverFile));
        Assert.True(File.Exists(permissionProfilesFile));
        Assert.True(File.Exists(commandApprovalResolverFile));
        Assert.True(File.Exists(toolJsonHelpersFile));

        var permissionSource = File.ReadAllText(permissionProfilesFile);
        var approvalSource = File.ReadAllText(commandApprovalResolverFile);
        var helpersSource = File.ReadAllText(toolJsonHelpersFile);
        var kernelToolsSource = AssertFileDeletedAndReturnEmpty(kernelToolsFile);

        Assert.Contains("namespace TianShu.AppHost.Tools;", permissionSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelPermissionGrantProfile", permissionSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools;", approvalSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelToolCommandApprovalResolver", approvalSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools;", helpersSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelToolJsonHelpers", helpersSource, StringComparison.Ordinal);
        Assert.Contains("public static List<string> ReadStringArray", helpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryReadInputArray", helpersSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, string[]> TryReadExtraSkillRoots", helpersSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelJsonSchemaSanitizer", helpersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal static class KernelToolJsonHelpers", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal static class KernelJsonSchemaSanitizer", kernelToolsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelExecPolicyDecisionModels_ShouldLiveUnderAppHostToolsProject()
    {
        var decisionModelsFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelExecPolicyDecisionModels.cs");
        var managedNetworkPrimitiveTypesFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelManagedNetworkPrimitiveTypes.cs");
        var ruleModelsFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelExecPolicyRuleModels.cs");
        var oldKernelManagerFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelExecPolicyManager.cs");
        var oldKernelManagedNetworkFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelManagedNetwork.cs");
        var runtimeManagerFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelExecPolicyManager.cs");
        var runtimeManagedNetworkFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelManagedNetwork.cs");

        var decisionSource = File.ReadAllText(decisionModelsFile);
        var managedNetworkPrimitiveTypesSource = File.ReadAllText(managedNetworkPrimitiveTypesFile);
        var ruleSource = File.ReadAllText(ruleModelsFile);
        var runtimeManagerSource = File.ReadAllText(runtimeManagerFile);
        var runtimeManagedNetworkSource = File.ReadAllText(runtimeManagedNetworkFile);

        Assert.False(File.Exists(oldKernelManagerFile));
        Assert.False(File.Exists(oldKernelManagedNetworkFile));
        Assert.True(File.Exists(runtimeManagerFile));
        Assert.True(File.Exists(runtimeManagedNetworkFile));

        Assert.Contains("namespace TianShu.AppHost.Tools;", decisionSource, StringComparison.Ordinal);
        Assert.Contains("internal enum KernelExecPolicyDecisionKind", decisionSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelExecPolicyDecision", decisionSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelExecPolicyApprovalResponseReader", decisionSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools;", managedNetworkPrimitiveTypesSource, StringComparison.Ordinal);
        Assert.Contains("internal enum KernelManagedNetworkProtocol", managedNetworkPrimitiveTypesSource, StringComparison.Ordinal);
        Assert.Contains("internal enum KernelManagedNetworkRuleAction", managedNetworkPrimitiveTypesSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools;", ruleSource, StringComparison.Ordinal);
        Assert.Contains("internal enum KernelExecPolicyRuleDecision", ruleSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelExecPolicyRule", ruleSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelExecPolicyNetworkRule", ruleSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeManagerSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelExecPolicyManager", runtimeManagerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal enum KernelExecPolicyRuleDecision", runtimeManagerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelExecPolicyRule(", runtimeManagerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelExecPolicyNetworkRule(", runtimeManagerSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeManagedNetworkSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelManagedNetworkManager", runtimeManagedNetworkSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelManagedNetworkExecutionLease", runtimeManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal enum KernelManagedNetworkProtocol", runtimeManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal enum KernelManagedNetworkRuleAction", runtimeManagedNetworkSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelCommandApprovalUtilities_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelParityFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var kernelGlobalUsingsFile = GetHostGlobalUsingsPath(repoRoot);
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelCommandApprovalUtilities.cs");

        var kernelParitySource = AssertFileDeletedAndReturnEmpty(kernelParityFile);
        var kernelGlobalUsingsSource = File.ReadAllText(kernelGlobalUsingsFile);
        var helperSource = File.ReadAllText(helperFile);

        Assert.DoesNotContain("private static string? BuildCommandApprovalSessionKey(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string NormalizeSandboxPermissionsForApprovalKey(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelCommandExecutionApprovalSkillMetadata(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelCommandExecutionApprovalSkillMetadata? TryResolveCommandExecutionApprovalSkillMetadata(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static IReadOnlyList<object?> BuildCommandExecutionAvailableDecisions(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string BuildCommandApprovalDeclinedMessage(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string BuildCommandPolicyDeniedMessage(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryResolveCommandApprovalDecision(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool RequiresCommandApproval(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool IsCommandAllowedBySandbox(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool LooksMutatingCommand(", kernelParitySource, StringComparison.Ordinal);

        Assert.Contains("global using static TianShu.AppHost.Tools.KernelCommandApprovalUtilities;", kernelGlobalUsingsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelCommandApprovalUtilities", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string? BuildCommandApprovalSessionKey", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string NormalizeSandboxPermissionsForApprovalKey", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelCommandExecutionApprovalSkillMetadata? TryResolveCommandExecutionApprovalSkillMetadata", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<object?> BuildCommandExecutionAvailableDecisions", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string BuildCommandApprovalDeclinedMessage", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string BuildCommandPolicyDeniedMessage", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryResolveCommandApprovalDecision", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool RequiresCommandApproval", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool IsCommandAllowedBySandbox", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool LooksMutatingCommand", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelCommandExecutionApprovalSkillMetadata", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelManagedNetworkHostModelsAndHelpers_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var hostModelsFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelManagedNetworkHostModels.cs");
        var helpersFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelManagedNetworkHelpers.cs");
        var oldKernelManagedNetworkFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelManagedNetwork.cs");
        var runtimeManagedNetworkFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelManagedNetwork.cs");

        Assert.True(File.Exists(hostModelsFile));
        Assert.True(File.Exists(helpersFile));
        Assert.False(File.Exists(oldKernelManagedNetworkFile));
        Assert.True(File.Exists(runtimeManagedNetworkFile));

        var hostModelsSource = File.ReadAllText(hostModelsFile);
        var helpersSource = File.ReadAllText(helpersFile);
        var runtimeManagedNetworkSource = File.ReadAllText(runtimeManagedNetworkFile);

        Assert.Contains("namespace TianShu.AppHost.Tools;", hostModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal enum KernelManagedNetworkOutcomeKind", hostModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelManagedNetworkSettings(", hostModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelManagedNetworkApprovalRequest(", hostModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelManagedNetworkBlockedHttpPayload(", hostModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelManagedNetworkOutcome(", hostModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal readonly record struct KernelManagedNetworkHostKey", hostModelsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools;", helpersSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelManagedNetworkHelpers", helpersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helpersSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal enum KernelManagedNetworkOutcomeKind", runtimeManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelManagedNetworkSettings(", runtimeManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelManagedNetworkApprovalRequest(", runtimeManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelManagedNetworkBlockedHttpPayload(", runtimeManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelManagedNetworkOutcome(", runtimeManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal readonly record struct KernelManagedNetworkHostKey", runtimeManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal static class KernelManagedNetworkHelpers", runtimeManagedNetworkSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelManagedNetworkSettingsHelpers_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerFile = GetAppHostServerSourcePath(repoRoot);
        var kernelManagedNetworkFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.ManagedNetwork.cs");
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelManagedNetworkSettingsUtilities.cs");

        Assert.True(File.Exists(kernelAppServerFile));
        Assert.False(File.Exists(kernelManagedNetworkFile));
        Assert.True(File.Exists(helperFile));

        var kernelSource = File.ReadAllText(kernelAppServerFile);
        var helperSource = File.ReadAllText(helperFile);

        Assert.Contains("KernelManagedNetworkSettingsUtilities.ResolveManagedNetworkSettingsWithSkillOverride(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private KernelManagedNetworkSettings ResolveManagedNetworkSettings(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private KernelManagedNetworkSettings ResolveManagedNetworkSettingsWithSkillOverride(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelManagedNetworkSettings CreateDefaultManagedNetworkSettings(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelManagedNetworkSettings ApplyConfiguredManagedNetworkSettings(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelManagedNetworkSettings ApplyManagedNetworkRequirements(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void ValidateManagedNetworkSettings(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void ValidateManagedNetworkDomainPatterns(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void ValidateManagedNetworkUnixSocketAllowlist(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool IsAbsoluteManagedNetworkSocketPath(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryReadConfiguredStringArrayValue(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string NormalizeManagedNetworkMode(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string[] NormalizeManagedNetworkList(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static InvalidOperationException CreateManagedNetworkConstraintError(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static (string Host, int Port) ResolveProxyBinding(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static (string Host, int Port) ParseManagedNetworkHostAndPort(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static (string Host, int Port) ParseManagedNetworkHostAndPortFallback(", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelManagedNetworkRequirements? ReadManagedNetworkRequirements()", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelManagedNetworkRequirements? TryParseManagedNetworkRequirements(", kernelSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelManagedNetworkSettingsUtilities", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelManagedNetworkSettings ResolveManagedNetworkSettings(KernelConfigReadSnapshot snapshot)", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelManagedNetworkSettings ResolveManagedNetworkSettingsWithSkillOverride(", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelCommandExecRequestPrimitives_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var commandExecSourceFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.CommandExec.cs");
        var commandExecHelpersFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelCommandExecRequestHelpers.cs");

        Assert.False(File.Exists(commandExecSourceFile));
        Assert.True(File.Exists(commandExecHelpersFile));

        var helperSource = File.ReadAllText(commandExecHelpersFile);

        Assert.Contains("namespace TianShu.AppHost.Tools;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelCommandExecRequestHelpers", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal readonly record struct KernelCommandExecTerminalSize", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelWindowsSandboxSetupUtilities_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var runtimeFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelWindowsSandboxSurfaceAppHostRuntime.cs");
        var kernelGlobalUsingsFile = GetHostGlobalUsingsPath(repoRoot);
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelWindowsSandboxSetupUtilities.cs");

        var runtimeSource = File.ReadAllText(runtimeFile);
        var kernelGlobalUsingsSource = File.ReadAllText(kernelGlobalUsingsFile);
        var helperSource = File.ReadAllText(helperFile);

        Assert.Contains("KernelWindowsSandboxSetupUtilities.TryNormalizeSetupMode", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelWindowsSandboxSetupUtilities.RunWindowsSandboxSetupAsync", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<(bool Success, string? Error)> RunWindowsSandboxSetupAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<string> PrepareWindowsSandboxArtifactsAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string BuildWindowsSandboxConfigXml(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<(bool Success, string? Error)> ProbeWindowsSandboxAvailabilityAsync(", runtimeSource, StringComparison.Ordinal);

        Assert.Contains("global using static TianShu.AppHost.Tools.KernelWindowsSandboxSetupUtilities;", kernelGlobalUsingsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelWindowsSandboxSetupUtilities", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryNormalizeSetupMode", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static async Task<(bool Success, string? Error)> RunWindowsSandboxSetupAsync", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static async Task<string> PrepareWindowsSandboxArtifactsAsync", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string BuildWindowsSandboxConfigXml", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<string> BuildWindowsSandboxProbeCommand", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static async Task<(bool Success, string? Error)> ProbeWindowsSandboxAvailabilityAsync", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelWindowsSandboxProbeCommandResult", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelUnifiedExecToolPrimitives_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var unifiedExecSourceFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelCommittedUnifiedExec.cs");
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelUnifiedExecToolHelpers.cs");

        Assert.True(File.Exists(helperFile));
        Assert.False(File.Exists(unifiedExecSourceFile));

        var helperSource = File.ReadAllText(helperFile);

        Assert.Contains("namespace TianShu.AppHost.Tools;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelUnifiedExecToolHelpers", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelUserShellHistoryPrimitives_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var userShellSourceFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.UserShell.cs");
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelUserShellToolHelpers.cs");

        Assert.True(File.Exists(helperFile));
        Assert.False(File.Exists(userShellSourceFile));

        var helperSource = File.ReadAllText(helperFile);

        Assert.Contains("namespace TianShu.AppHost.Tools;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelUserShellToolHelpers", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelUserShellRunResultPayload", helperSource, StringComparison.Ordinal);
        Assert.Contains("public const int UserShellCommandReplayTokenLimit", helperSource, StringComparison.Ordinal);
        Assert.Contains("public const string UserShellCommandRecordType", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelUserShellRunResultPayload BuildRunResult(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string BuildCommandHistoryText(", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelUnifiedExecRuntimeCore_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelUnifiedExecFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelCommittedUnifiedExec.cs");
        var runtimeFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelUnifiedExecRuntime.cs");
        var leaseContractFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelManagedNetworkExecutionLeaseContract.cs");

        Assert.True(File.Exists(runtimeFile));
        Assert.True(File.Exists(leaseContractFile));
        Assert.False(File.Exists(kernelUnifiedExecFile));

        var runtimeSource = File.ReadAllText(runtimeFile);
        var leaseContractSource = File.ReadAllText(leaseContractFile);

        Assert.Contains("namespace TianShu.AppHost.Tools;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelCommittedUnifiedExecProcessManager", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelCommittedUnifiedExecSession", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("IKernelManagedNetworkExecutionLease", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools;", leaseContractSource, StringComparison.Ordinal);
        Assert.Contains("internal interface IKernelManagedNetworkExecutionLease", leaseContractSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelUserShellRuntimeHelpers_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var userShellSourceFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.UserShell.cs");
        var runtimeHelperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelUserShellRuntimeHelpers.cs");

        Assert.True(File.Exists(runtimeHelperFile));
        Assert.False(File.Exists(userShellSourceFile));

        var helperSource = File.ReadAllText(runtimeHelperFile);

        Assert.Contains("namespace TianShu.AppHost.Tools;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelUserShellRuntimeHelpers", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string NextItemId()", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryBuildCommandHistoryText", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelTrackedCommandExecRuntimeCore_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelCommandExecFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.CommandExec.cs");
        var runtimeFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelTrackedCommandExecRuntime.cs");

        Assert.False(File.Exists(kernelCommandExecFile));
        Assert.True(File.Exists(runtimeFile));

        var runtimeSource = File.ReadAllText(runtimeFile);

        Assert.Contains("namespace TianShu.AppHost.Tools;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal interface IKernelTrackedCommandExecRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelPipeCommandExecRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTrackedCommandExecSession", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelTrackedCommandExecResult", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelCommandExecAppHostRuntime_ShouldLiveUnderAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var kernelCommandExecFile = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.CommandExec.cs");
        var runtimeFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelCommandExecAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var runtimeSource = File.ReadAllText(runtimeFile);

        Assert.False(File.Exists(kernelCommandExecFile));
        Assert.Contains("new KernelCommandExecAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("commandExecAppHostRuntime.DisposeTrackedCommandExecSessions();", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("commandExecAppHostRuntime.HandleCommandExecWriteAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("commandExecAppHostRuntime.HandleCommandExecTerminateAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("commandExecAppHostRuntime.HandleCommandExecResizeAsync(", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelCommandExecAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task StartTrackedCommandExecAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleCommandExecWriteAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleCommandExecTerminateAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleCommandExecResizeAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public void DisposeTrackedCommandExecSessions()", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelCommandRunResult ApplyCommandExecOutputCap(", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelCommandExecSurfaceAppHostRuntime_ShouldOwnCommandExecNorthboundSurface()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var parityPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var runtimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelCommandExecSurfaceAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var paritySource = AssertFileDeletedAndReturnEmpty(parityPath);
        var runtimeSource = File.ReadAllText(runtimePath);

        Assert.Contains("new KernelCommandExecSurfaceAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("commandExecSurfaceAppHostRuntime.HandleCommandExecAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);

        Assert.DoesNotContain("private async Task HandleCommandExecAsync(", paritySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool IsCommandApprovalAcceptedForSession(", paritySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private void MarkCommandApprovalAcceptedForSession(", paritySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<(string? Decision, string? Error, KernelExecPolicyAmendment? ApplyAmendment)> RequestCommandExecutionApprovalAsync(", paritySource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelCommandExecSurfaceAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleCommandExecAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelCommandApprovalUtilities.BuildCommandExecutionAvailableDecisions(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("managedNetworkAppHostRuntime.BeginExecutionAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("commandExecAppHostRuntime.StartTrackedCommandExecAsync(", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelWindowsSandboxSurfaceAppHostRuntime_ShouldOwnWindowsSandboxNorthboundSurface()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var parityPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var runtimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelWindowsSandboxSurfaceAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var paritySource = AssertFileDeletedAndReturnEmpty(parityPath);
        var runtimeSource = File.ReadAllText(runtimePath);

        Assert.Contains("new KernelWindowsSandboxSurfaceAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("windowsSandboxSurfaceAppHostRuntime.HandleWindowsSandboxSetupStartAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);

        Assert.DoesNotContain("private async Task HandleWindowsSandboxSetupStartAsync(", paritySource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelWindowsSandboxSurfaceAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleWindowsSandboxSetupStartAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelWindowsSandboxSetupUtilities.TryNormalizeSetupMode", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelWindowsSandboxSetupUtilities.RunWindowsSandboxSetupAsync", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelFeedbackAppHostRuntime_ShouldOwnFeedbackUploadNorthboundSurface()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var parityPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var runtimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelFeedbackAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var paritySource = AssertFileDeletedAndReturnEmpty(parityPath);
        var runtimeSource = File.ReadAllText(runtimePath);

        Assert.Contains("new KernelFeedbackAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("feedbackAppHostRuntime.HandleFeedbackUploadAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);

        Assert.DoesNotContain("private async Task HandleFeedbackUploadAsync(", paritySource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelFeedbackAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleFeedbackUploadAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelStoragePaths.ResolveDefault()", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("threadStore.GetThreadAsync", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelProcessExecutionAppHostRuntime_ShouldOwnProcessExecutionAndBackgroundTerminalGlue()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var parityPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var runtimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelProcessExecutionAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var paritySource = AssertFileDeletedAndReturnEmpty(parityPath);
        var runtimeSource = File.ReadAllText(runtimePath);

        Assert.Contains("private readonly KernelProcessExecutionAppHostRuntime processExecutionAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("this.processExecutionAppHostRuntime = new KernelProcessExecutionAppHostRuntime();", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await processExecutionAppHostRuntime.DisposeAsync().ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("processExecutionAppHostRuntime.ExecuteCommandAsync", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("processExecutionAppHostRuntime.StartBackgroundCommandAsync", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("processExecutionAppHostRuntime.CleanBackgroundTerminals", kernelAppServerSource, StringComparison.Ordinal);

        Assert.DoesNotContain("private async Task<KernelBackgroundCommandStartResult> StartBackgroundCommandAsync(", paritySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private void TrackBackgroundTerminal(", paritySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private int CleanBackgroundTerminals(", paritySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private void UntrackBackgroundTerminal(", paritySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private void TryDisposeBackgroundManagedNetworkLease(", paritySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void TryDisposeProcess(", paritySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<KernelCommandRunResult> ExecuteCommandAsync(", paritySource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelProcessExecutionAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Process>> backgroundTerminalsByThread", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly ConcurrentDictionary<int, KernelManagedNetworkExecutionLease> backgroundManagedNetworkLeasesByPid", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<int> StartBackgroundCommandAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public int CleanBackgroundTerminals(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelCommandRunResult> ExecuteCommandAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public ValueTask DisposeAsync()", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelArtifactSurfaceAppHostRuntime_ShouldOwnArtifactReadSurfaces()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var parityPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var runtimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelArtifactSurfaceAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var paritySource = AssertFileDeletedAndReturnEmpty(parityPath);
        var runtimeSource = File.ReadAllText(runtimePath);

        Assert.Contains("private readonly KernelArtifactSurfaceAppHostRuntime artifactSurfaceAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("new KernelArtifactSurfaceAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("case \"artifact/conversationsummary/read\":", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("artifactSurfaceAppHostRuntime.HandleConversationSummaryReadAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("case \"artifact/gitdifftoremote/read\":", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("artifactSurfaceAppHostRuntime.HandleGitDiffToRemoteReadAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("case \"getConversationSummary\":", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("case \"gitDiffToRemote\":", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("case \"getAuthStatus\":", kernelAppServerSource, StringComparison.Ordinal);

        Assert.DoesNotContain("private async Task HandleGetConversationSummaryAsync(", paritySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandleGitDiffToRemoteAsync(", paritySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandleGetAuthStatusAsync(", paritySource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelArtifactSurfaceAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleConversationSummaryReadAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleGitDiffToRemoteReadAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelConversationSummaryUtilities.BuildConversationSummaryPayload(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("captureThreadGitDiffAsync", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("deprecationNotice", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelWindowsPseudoConsoleRuntime_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var oldRuntimeFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelWindowsPseudoConsoleRuntime.cs");
        var newRuntimeFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelWindowsPseudoConsoleRuntime.cs");

        Assert.False(File.Exists(oldRuntimeFile));
        Assert.True(File.Exists(newRuntimeFile));

        var source = File.ReadAllText(newRuntimeFile);
        Assert.Contains("namespace TianShu.AppHost.Tools;", source, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelWindowsPseudoConsoleRuntime", source, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelExecutionEnvelopePrimitives_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var oldEnvelopeFactoryFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelExecutionEnvelopeFactory.cs");
        var newEnvelopeFactoryFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelExecutionEnvelopeFactory.cs");
        var executionModelsFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelEnvironmentExecutionModels.cs");

        Assert.False(File.Exists(oldEnvelopeFactoryFile));
        Assert.True(File.Exists(newEnvelopeFactoryFile));
        Assert.True(File.Exists(executionModelsFile));
    }

    [Fact]
    public void KernelExecutionEnvelopeSources_ShouldUseAppHostToolsNamespace()
    {
        var repoRoot = FindRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelExecutionEnvelopeFactory.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelEnvironmentExecutionModels.cs"),
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);

            Assert.Contains("namespace TianShu.AppHost.Tools;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AppHostStateSources_ShouldNotOwnKernelStateMachineSemantics()
    {
        var stateRoot = Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.State");
        var forbiddenTokens = new[]
        {
            "IStableKernelCore",
            "IAdaptiveOrchestrator",
            "IStageGraphInterpreter",
            "IKernelValidator",
            "KernelRunStateMachine",
            "StageGraphInterpreter",
            "ValidateStageGraph",
        };

        foreach (var file in Directory.EnumerateFiles(stateRoot, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);

            foreach (var token in forbiddenTokens)
            {
                Assert.DoesNotContain(token, source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void KernelExecutionEnvelopeMetadata_ShouldUseCurrentExecutionKindKey()
    {
        var repoRoot = FindRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelExecutionEnvelopeFactory.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.State", "KernelRolloutRecorder.cs"),
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);

            Assert.Contains("executionKind", source, StringComparison.Ordinal);
            Assert.DoesNotContain("legacyExecutionKind", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KernelLocalRuntimeManagers_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var oldFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelArtifactsRuntimeManager.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelCodeModeManager.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelJsReplManager.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "CodeMode", "runner.cjs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "CodeMode", "bridge.js"),
        };
        var newFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelArtifactsRuntimeManager.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelCodeModeManager.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelJsReplManager.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "CodeMode", "runner.cjs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "CodeMode", "bridge.js"),
        };

        foreach (var file in oldFiles)
        {
            Assert.False(File.Exists(file), $"旧文件不应继续存在: {file}");
        }

        foreach (var file in newFiles)
        {
            Assert.True(File.Exists(file), $"缺少已归位文件: {file}");
        }
    }

    [Fact]
    public void AppHostToolsLocalRuntimeManagerSources_ShouldUseAppHostToolsNamespace()
    {
        var repoRoot = FindRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelArtifactsRuntimeManager.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelCodeModeManager.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelJsReplManager.cs"),
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);

            Assert.Contains("namespace TianShu.AppHost.Tools;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AppHostToolsProject_ShouldOwnCodeModeEmbeddedResources()
    {
        var repoRoot = FindRepoRoot();
        var appHostToolsProjectFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "TianShu.AppHost.Tools.csproj");
        var kernelProjectFile = GetDeletedKernelProjectFilePath(repoRoot);

        var appHostToolsSource = File.ReadAllText(appHostToolsProjectFile);
        var kernelSource = AssertFileDeletedAndReturnEmpty(kernelProjectFile);

        Assert.Contains("<EmbeddedResource Include=\"CodeMode\\runner.cjs\" LogicalName=\"TianShu.AppHost.Tools.Resources.code-mode.runner.cjs\" />", appHostToolsSource, StringComparison.Ordinal);
        Assert.Contains("<EmbeddedResource Include=\"CodeMode\\bridge.js\" LogicalName=\"TianShu.AppHost.Tools.Resources.code-mode.bridge.js\" />", appHostToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AppServer\\CodeMode\\runner.cjs", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AppServer\\CodeMode\\bridge.js", kernelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelLegacyExecutionEnvelopeDefinitions_ShouldNotRemainInlineInKernelSources()
    {
        var repoRoot = FindRepoRoot();
        var oldArtifactsFile = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelArtifactsToolHandler.cs");
        var oldCodeModeFile = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelCodeModeToolHandlers.cs");
        var oldJsReplFile = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelJsReplToolHandlers.cs");
        var artifactsSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelArtifactsRuntimeSupport.cs"));
        var codeModeSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelCodeModeRuntimeSupport.cs"));
        var jsReplSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelJsReplRuntimeSupport.cs"));

        Assert.False(File.Exists(oldArtifactsFile));
        Assert.False(File.Exists(oldCodeModeFile));
        Assert.False(File.Exists(oldJsReplFile));

        Assert.DoesNotContain("internal sealed record KernelArtifactsExecutionRequest", artifactsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelArtifactsExecutionResult", artifactsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelCodeModeExecutionRequest", codeModeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelCodeModeWaitRequest", codeModeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelCodeModeOperationResult", codeModeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelJsReplExecutionRequest", jsReplSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelJsReplExecutionResult", jsReplSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostTestsProject_ShouldOwnLocalRuntimeManagerTests()
    {
        var repoRoot = FindRepoRoot();
        var appHostProjectFile = Path.Combine(repoRoot, "tests", "TianShu.AppHost.Tests", "TianShu.AppHost.Tests.csproj");
        var integrationProjectFile = Path.Combine(repoRoot, "tests", "TianShu.Execution.Integration.Tests", "TianShu.Execution.Integration.Tests.csproj");
        var runtimeProjectFile = Path.Combine(repoRoot, "tests", "TianShu.Execution.Runtime.Tests", "TianShu.Execution.Runtime.Tests.csproj");

        var appHostSource = File.ReadAllText(appHostProjectFile);
        var integrationSource = File.ReadAllText(integrationProjectFile);
        var runtimeSource = File.ReadAllText(runtimeProjectFile);

        foreach (var fileName in new[]
                 {
                     "KernelArtifactsRuntimeManagerTests.cs",
                     "KernelCodeModeManagerTests.cs",
                     "KernelJsReplManagerTests.cs",
                 })
        {
            Assert.True(File.Exists(Path.Combine(repoRoot, "tests", "TianShu.AppHost.Tests", "Migrated", fileName)));
            Assert.False(File.Exists(Path.Combine(repoRoot, "tests", "TianShu.Execution.Integration.Tests", "Migrated", fileName)));
            Assert.False(File.Exists(Path.Combine(repoRoot, "tests", "TianShu.Execution.Runtime.Tests", "Migrated", "KernelTests", fileName)));
        }

        Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests", appHostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests", integrationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelMcpRuntimePrimitives_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var oldMcpManagerFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelMcpManager.cs");
        var oldTransportClientsFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelMcpTransportClients.cs");
        var oldCustomCaFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelCustomCaSupport.cs");
        var newMcpManagerFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelMcpManager.cs");
        var newTransportClientsFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelMcpTransportClients.cs");
        var newCustomCaFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelCustomCaSupport.cs");
        var pluginMcpDefinitionFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelPluginMcpServerDefinition.cs");

        Assert.False(File.Exists(oldMcpManagerFile));
        Assert.False(File.Exists(oldTransportClientsFile));
        Assert.False(File.Exists(oldCustomCaFile));
        Assert.True(File.Exists(newMcpManagerFile));
        Assert.True(File.Exists(newTransportClientsFile));
        Assert.True(File.Exists(newCustomCaFile));
        Assert.True(File.Exists(pluginMcpDefinitionFile));
    }

    [Fact]
    public void AppHostToolsMcpSources_ShouldUseAppHostToolsNamespace_AndDropKernelRuntimeDependencies()
    {
        var repoRoot = FindRepoRoot();
        var mcpManagerFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelMcpManager.cs");
        var transportClientsFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelMcpTransportClients.cs");
        var customCaFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelCustomCaSupport.cs");
        var pluginMcpDefinitionFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelPluginMcpServerDefinition.cs");
        var pluginManagerFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelPluginsManager.cs");

        var files = new[]
        {
            mcpManagerFile,
            transportClientsFile,
            customCaFile,
            pluginMcpDefinitionFile,
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);

            Assert.Contains("namespace TianShu.AppHost.Tools;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", source, StringComparison.Ordinal);
        }

        var mcpManagerSource = File.ReadAllText(mcpManagerFile);
        var transportClientsSource = File.ReadAllText(transportClientsFile);
        var pluginManagerSource = File.ReadAllText(pluginManagerFile);

        Assert.DoesNotContain("KernelPluginsManager", mcpManagerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelDependencyEnvironmentScope", mcpManagerSource, StringComparison.Ordinal);
        Assert.Contains("loadPluginMcpServersAsync", mcpManagerSource, StringComparison.Ordinal);
        Assert.Contains("readEnvironmentVariable", mcpManagerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelDependencyEnvironmentScope", transportClientsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelPluginMcpServerDefinition(", pluginManagerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelPluginAndSkillDiscoverySources_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var oldFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelPluginsManager.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelSkillsManager.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelSkillMetadataResolver.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelPathUtilities.cs"),
        };
        var newFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelPluginsManager.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelSkillsManager.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelSkillMetadataResolver.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelPathUtilities.cs"),
        };

        foreach (var file in oldFiles)
        {
            Assert.False(File.Exists(file), $"旧文件不应继续存在: {file}");
        }

        foreach (var file in newFiles)
        {
            Assert.True(File.Exists(file), $"缺少已归位文件: {file}");
        }
    }

    [Fact]
    public void AppHostToolsPluginAndSkillDiscoverySources_ShouldUseAppHostToolsNamespace()
    {
        var repoRoot = FindRepoRoot();
        var pluginsManagerFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelPluginsManager.cs");
        var skillsManagerFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelSkillsManager.cs");
        var skillMetadataResolverFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelSkillMetadataResolver.cs");
        var pathUtilitiesFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelPathUtilities.cs");

        var files = new[]
        {
            pluginsManagerFile,
            skillsManagerFile,
            skillMetadataResolverFile,
            pathUtilitiesFile,
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);

            Assert.Contains("namespace TianShu.AppHost.Tools;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", source, StringComparison.Ordinal);
        }

        var skillsManagerSource = File.ReadAllText(skillsManagerFile);
        Assert.Contains("using TianShu.AppHost.Configuration;", skillsManagerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostToolsProject_ShouldOwnPluginAndSkillDiscoveryDependencies()
    {
        var repoRoot = FindRepoRoot();
        var appHostToolsProjectFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "TianShu.AppHost.Tools.csproj");
        var kernelProjectFile = GetDeletedKernelProjectFilePath(repoRoot);

        var appHostToolsSource = File.ReadAllText(appHostToolsProjectFile);
        var kernelSource = AssertFileDeletedAndReturnEmpty(kernelProjectFile);

        Assert.Contains("<PackageReference Include=\"YamlDotNet\" />", appHostToolsSource, StringComparison.Ordinal);
        Assert.Contains("<ProjectReference Include=\"..\\TianShu.AppHost.Configuration\\TianShu.AppHost.Configuration.csproj\" />", appHostToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("<PackageReference Include=\"YamlDotNet\" />", kernelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostTestsProject_ShouldOwnPluginAndSkillDiscoveryTests()
    {
        var repoRoot = FindRepoRoot();
        var appHostProjectFile = Path.Combine(repoRoot, "tests", "TianShu.AppHost.Tests", "TianShu.AppHost.Tests.csproj");
        var integrationProjectFile = Path.Combine(repoRoot, "tests", "TianShu.Execution.Integration.Tests", "TianShu.Execution.Integration.Tests.csproj");
        var runtimeProjectFile = Path.Combine(repoRoot, "tests", "TianShu.Execution.Runtime.Tests", "TianShu.Execution.Runtime.Tests.csproj");

        var appHostSource = File.ReadAllText(appHostProjectFile);
        var integrationSource = File.ReadAllText(integrationProjectFile);
        var runtimeSource = File.ReadAllText(runtimeProjectFile);

        foreach (var fileName in new[] { "KernelPluginsManagerTests.cs", "KernelSkillMetadataResolverTests.cs" })
        {
            Assert.True(File.Exists(Path.Combine(repoRoot, "tests", "TianShu.AppHost.Tests", "Migrated", fileName)));
            Assert.False(File.Exists(Path.Combine(repoRoot, "tests", "TianShu.Execution.Integration.Tests", "Migrated", fileName)));
            Assert.False(File.Exists(Path.Combine(repoRoot, "tests", "TianShu.Execution.Runtime.Tests", "Migrated", "KernelTests", fileName)));
        }

        Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests", appHostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests", integrationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelInstructionScopeHelpers_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerFile = GetAppHostServerSourcePath(repoRoot);
        var kernelInstructionScopesFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.InstructionScopes.cs");
        var utilityFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelInstructionScopeUtilities.cs");

        var kernelSource = File.ReadAllText(kernelAppServerFile);
        var utilitySource = File.ReadAllText(utilityFile);

        Assert.False(File.Exists(kernelInstructionScopesFile));
        Assert.Contains("KernelInstructionScopeUtilities.BuildScopedDeveloperInstructions", kernelSource, StringComparison.Ordinal);
        Assert.Contains("KernelInstructionScopeUtilities.BuildScopedUserInstructions", kernelSource, StringComparison.Ordinal);
        Assert.Contains("KernelInstructionScopeUtilities.SerializeUserInstructions", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private const string DefaultProjectDocFilename", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelProjectDocScopeOptions", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? BuildHomeInstructionSection", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? BuildScopedInstructionFileSections", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryReadInstructionContent", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static List<string> EnumerateScopedInstructionFilePaths", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static List<string> EnumerateSearchDirectories", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? FindProjectRoot", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelProjectDocScopeOptions ResolveProjectDocScopeOptions", kernelSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools;", utilitySource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelInstructionScopeUtilities", utilitySource, StringComparison.Ordinal);
        Assert.Contains("public static string? BuildScopedDeveloperInstructions", utilitySource, StringComparison.Ordinal);
        Assert.Contains("public static string? BuildScopedUserInstructions", utilitySource, StringComparison.Ordinal);
        Assert.Contains("public static string? SerializeUserInstructions", utilitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", utilitySource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostTestsProject_ShouldOwnKernelInstructionScopeTests()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "tests",
            "TianShu.AppHost.Tests",
            "TianShu.AppHost.Tests.csproj");

        var source = File.ReadAllText(projectFile);

        Assert.True(File.Exists(Path.Combine(
            FindRepoRoot(),
            "tests",
            "TianShu.AppHost.Tests",
            "Migrated",
            "KernelAppServerInstructionScopeTests.cs")));
        Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests\\KernelAppServerInstructionScopeTests.cs", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelInteractionCarrierSources_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var oldMcpElicitationFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelMcpServerElicitationModels.cs");
        var newFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelCollaborationToolModels.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelToolSchemaHelpers.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelSpawnAgentsOnCsvModels.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelRequestUserInputModels.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "McpServerElicitationModels.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelToolSuggestModels.cs"),
        };

        Assert.False(File.Exists(oldMcpElicitationFile));

        foreach (var file in newFiles)
        {
            Assert.True(File.Exists(file), $"缺少已归位文件: {file}");
        }
    }

    [Fact]
    public void AppHostToolsInteractionCarrierSources_ShouldUseAppHostToolsNamespace()
    {
        var repoRoot = FindRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelCollaborationToolModels.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelToolSchemaHelpers.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelSpawnAgentsOnCsvModels.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelRequestUserInputModels.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "McpServerElicitationModels.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "KernelToolSuggestModels.cs"),
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);

            Assert.Contains("namespace TianShu.AppHost.Tools;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KernelLegacyInteractionCarrierDefinitions_ShouldNotRemainInlineInKernelSources()
    {
        var repoRoot = FindRepoRoot();
        var oldCollaborationFile = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelCollaborationToolHandlers.cs");
        var oldSpawnCsvFile = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelSpawnAgentsOnCsvToolHandler.cs");
        var oldRequestUserInputFile = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelRequestUserInputToolHandler.cs");
        var oldToolDiscoverySuggestFile = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "Kernel" + "Tool" + "SuggestHandler.cs");
        var runtimeCollaborationHandlerFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelCollaborationToolHandlers.cs");
        var toolSuggestSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolDiscoveryRuntimeSupport.cs"));

        Assert.False(File.Exists(oldCollaborationFile));
        Assert.False(File.Exists(oldSpawnCsvFile));
        Assert.False(File.Exists(oldRequestUserInputFile));
        Assert.False(File.Exists(oldToolDiscoverySuggestFile));
        Assert.False(File.Exists(runtimeCollaborationHandlerFile));

        Assert.DoesNotContain("internal sealed record KernelToolSuggestConnectorInfo", toolSuggestSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostToolsProject_ShouldOwnMcpRuntimeDependencies()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "TianShu.AppHost.Tools.csproj");

        var source = File.ReadAllText(projectFile);

        Assert.Contains("<PackageReference Include=\"Tomlyn\" />", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TianShu.Execution.Runtime.csproj", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TianShu.AppHost.Tools.Runtime.csproj", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelRuntimeBoundToolHandlerSources_ShouldLiveUnderAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var fileNames = new[]
        {
            "KernelApplyPatchRuntimeSupport.cs",
            "KernelApprovalPolicyHelpers.cs",
            "KernelArtifactsRuntimeSupport.cs",
            "KernelArtifactsRuntimeHelpers.cs",
            "KernelAutoCompactionAppHostRuntime.cs",
            "KernelAutoCompactionRuntimeHelpers.cs",
            "KernelCodeModeProtocolHelpers.cs",
            "KernelCodeModeRuntimeHelpers.cs",
            "KernelCodeModeRuntimeSupport.cs",
            "KernelCommandExecAppHostRuntime.cs",
            "KernelCollaborationRuntimeSupport.cs",
            "KernelCollaborationLifecycleHelpers.cs",
            "KernelCommittedUnifiedExec.cs",
            "KernelJsReplRuntimeHelpers.cs",
            "KernelJsReplRuntimeSupport.cs",
            "McpServerSurfaceHelpers.cs",
            "KernelNativeToolOptionsAppHostRuntime.cs",
            "KernelPendingInteractiveReplayAppHostRuntime.cs",
            "KernelPendingInteractiveReplayHelpers.cs",
            "KernelThreadHistoryAppHostRuntime.cs",
            "KernelThreadLifecycleAppHostRuntime.cs",
            "KernelToolCallAppHostRuntime.cs",
            "KernelToolRuntimeServices.cs",
            "KernelResponsesToolRegistry.cs",
            "KernelSandboxEnforcer.cs",
            "KernelSpawnAgentsOnCsvRuntimeHelpers.cs",
            "KernelToolRuntimeAgentHelpers.cs",
            "KernelToolRuntimeApprovalHelpers.cs",
            "KernelToolRuntimeInteractionHelpers.cs",
            "KernelToolItemLifecycleAppHostRuntime.cs",
            "KernelToolItemLifecycleHelpers.cs",
            "KernelToolRuntimeParsingHelpers.cs",
            "KernelToolExecutionNotificationHook.cs",
            "KernelToolAbstractions.cs",
            "KernelToolSandboxResolver.cs",
            "KernelUnifiedExecAvailability.cs",
            "KernelShellRuntimeSupport.cs",
            "KernelTestSyncRuntimeSupport.cs",
            "KernelViewImageRuntimeSupport.cs",
            "KernelManagedNetworkExecutionRequest.cs",
        };

        foreach (var fileName in fileNames)
        {
            var oldPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", fileName);
            var newPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", fileName);

            Assert.False(File.Exists(oldPath), $"旧文件不应继续存在: {oldPath}");
            Assert.True(File.Exists(newPath), $"缺少已归位文件: {newPath}");
        }
    }

    [Fact]
    public void KernelToolResult_ShouldDelegateProviderPayloadProjectionToContractsTools()
    {
        var repoRoot = FindRepoRoot();
        var runtimeAbstractionsPath = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelToolAbstractions.cs");
        var projectorPath = Path.Combine(
            repoRoot,
            "src",
            "Contracts",
            "TianShu.Contracts.Tools",
            "Models",
            "ToolUseFollowUpItemProjector.cs");

        var runtimeAbstractionsSource = File.ReadAllText(runtimeAbstractionsPath);
        var projectorSource = File.ReadAllText(projectorPath);

        Assert.Contains("ToolUseFollowUpItemProjector.BuildFunctionCallOutputPayload(", runtimeAbstractionsSource, StringComparison.Ordinal);
        Assert.Contains("ToolUseFollowUpItemProjector.BuildTextPreview(", runtimeAbstractionsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, object?> ToWireItem(", runtimeAbstractionsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, object?> BuildInputImageItem(", runtimeAbstractionsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string BuildTextPreview(", runtimeAbstractionsSource, StringComparison.Ordinal);
        Assert.Contains("public static object BuildFunctionCallOutputPayload(", projectorSource, StringComparison.Ordinal);
        Assert.Contains("public static string BuildTextPreview(", projectorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelToolSchemaValidator_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelToolsPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelTools.cs");
        var runtimeValidatorPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolSchemaValidator.cs");

        var kernelToolsSource = AssertFileDeletedAndReturnEmpty(kernelToolsPath);
        var runtimeValidatorSource = File.ReadAllText(runtimeValidatorPath);

        Assert.DoesNotContain("internal static class KernelToolSchemaValidator", kernelToolsSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelToolSchemaValidator", runtimeValidatorSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeValidatorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelResponsesToolRegistry_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelToolsPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelTools.cs");
        var runtimeRegistryPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelResponsesToolRegistry.cs");
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);

        var kernelToolsSource = AssertFileDeletedAndReturnEmpty(kernelToolsPath);
        var runtimeRegistrySource = File.ReadAllText(runtimeRegistryPath);
        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);

        Assert.DoesNotContain("internal sealed record KernelResponsesNativeToolOptions(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed class KernelToolRegistry", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelToolRegistry CreateToolRegistry()", kernelToolsSource, StringComparison.Ordinal);

        Assert.Contains("internal sealed record KernelResponsesNativeToolOptions(", runtimeRegistrySource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelToolRegistry", runtimeRegistrySource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelToolRegistryFactory", runtimeRegistrySource, StringComparison.Ordinal);
        Assert.Contains("public static KernelToolRegistry CreateDefaultRegistry()", runtimeRegistrySource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeRegistrySource, StringComparison.Ordinal);

        Assert.Contains("ToolRuntimeComposition.CreateDefaultToolRegistry(", kernelAppServerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelToolRuntimeInteractionHelpers_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var kernelToolRuntimePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ToolRuntime.cs");
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolRuntimeInteractionHelpers.cs");
        var appHostRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolRuntimeAppHostRuntime.cs");
        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);
        var appHostRuntimeSource = File.ReadAllText(appHostRuntimePath);

        Assert.False(File.Exists(kernelToolRuntimePath));
        Assert.DoesNotContain("private static KernelTurnRecord CloneTurnRecordForResponse(", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("internal static class KernelToolRuntimeInteractionHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelRequestUserInputResponse ParseRequestUserInputResponse(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string BuildCollabPrompt(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<KernelTurnInputItem>? BuildCollabTurnInputItems(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelTurnInputItem? BuildCollabTurnInputItem(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelTurnRecord CloneTurnRecordForResponse(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelTurnRecord? InjectForkedSpawnAgentToolItems(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static JsonElement BuildForkedSpawnAgentFunctionCallItem(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static JsonElement BuildForkedSpawnAgentFunctionCallOutputItem(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("ToolUseFollowUpItemProjector.BuildFunctionCallItem(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("ToolUseFollowUpItemProjector.BuildFunctionCallOutputItem(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"type\"] = \"function_call\"", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"type\"] = \"function_call_output\"", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string SerializeSpawnAgentArguments(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static (string? Model, string? ReasoningEffort) NormalizeSpawnAgentRequestedModelAndReasoning(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static void ValidateSpawnAgentReasoningEffort(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("KernelCatalogSurfaceUtilities.TryGetBuiltInModel(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("KernelCatalogSurfaceUtilities.GetBuiltInModelNames()", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderModelCatalogs.TryGetModel(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderModelCatalogs.ListModels()", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool ContainsRawResponseTurnItem(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string NormalizePlanStatus(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);

        Assert.Contains("KernelToolRuntimeInteractionHelpers.CloneTurnRecordForResponse(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeInteractionHelpers.InjectForkedSpawnAgentToolItems(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeInteractionHelpers.BuildCollabPrompt(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeInteractionHelpers.NormalizePlanStatus(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeInteractionHelpers.ParseRequestUserInputResponse(", appHostRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelCollaborationLifecycleHelpers_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var toolItemLifecyclePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ToolItemLifecycle.cs");
        var collaborationLifecyclePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.CollaborationLifecycle.cs");
        var lifecycleRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolItemLifecycleAppHostRuntime.cs");
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelCollaborationLifecycleHelpers.cs");

        var collaborationLifecycleSource = File.ReadAllText(lifecycleRuntimePath);
        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);

        Assert.False(File.Exists(toolItemLifecyclePath));
        Assert.False(File.Exists(collaborationLifecyclePath));
        Assert.DoesNotContain("private sealed record KernelCollabLifecycleDescriptor(", collaborationLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object CreateCollabToolCallItem(", collaborationLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static (string Status, IReadOnlyList<string> ReceiverThreadIds, IReadOnlyDictionary<string, object?> AgentsStates) BuildCollabCompletedState(", collaborationLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryCreateCollabLifecycleDescriptor(", collaborationLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryReadTopLevelStatusNode(", collaborationLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryReadStatusMap(", collaborationLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? TryReadJsonString(", collaborationLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryCreateCollabAgentState(", collaborationLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object CreateCollabAgentStatePayload(", collaborationLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool ShouldFailCollabCall(", collaborationLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string NormalizeCollabAgentStatus(", collaborationLifecycleSource, StringComparison.Ordinal);

        Assert.Contains("internal sealed record KernelCollabLifecycleDescriptor(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelCollabCompletedState(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelCollaborationLifecycleHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static object CreateCollabToolCallItem(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelCollabCompletedState BuildCollabCompletedState(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryCreateCollabLifecycleDescriptor(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);

        Assert.Contains("KernelCollaborationLifecycleHelpers.TryCreateCollabLifecycleDescriptor(", collaborationLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("KernelCollaborationLifecycleHelpers.BuildCollabCompletedState(", collaborationLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("KernelCollaborationLifecycleHelpers.CreateCollabToolCallItem(", collaborationLifecycleSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelPendingInteractiveReplayHelpers_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var pendingInteractiveReplayPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.PendingInteractiveReplay.cs");
        var pendingInteractiveReplayRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelPendingInteractiveReplayAppHostRuntime.cs");
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelPendingInteractiveReplayHelpers.cs");

        var pendingInteractiveReplayRuntimeSource = File.ReadAllText(pendingInteractiveReplayRuntimePath);
        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);

        Assert.False(File.Exists(pendingInteractiveReplayPath));

        Assert.Contains("internal static class KernelPendingInteractiveReplayHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool IsPendingInteractiveRequestMethod(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string? TryReadPendingInteractiveCallId(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static object BuildPendingInteractiveRequestPayload(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static JsonElement? ReadPendingAvailableDecisionsElement(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string[]? ExtractAvailableDecisionTypes(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string ResolvePendingApprovalToolName(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string BuildPendingUserInputSummary(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static object[] BuildPendingUserInputQuestions(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryGetProperty(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);

        Assert.Contains("KernelPendingInteractiveReplayHelpers.IsPendingInteractiveRequestMethod(", pendingInteractiveReplayRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelPendingInteractiveReplayHelpers.TryReadPendingInteractiveCallId(", pendingInteractiveReplayRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelPendingInteractiveReplayHelpers.BuildPendingInteractiveRequestPayload(", pendingInteractiveReplayRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelPendingInteractiveReplayHelpers.TryGetProperty(", pendingInteractiveReplayRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelPendingInteractiveReplayAppHostRuntime_ShouldLiveUnderAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var pendingInteractiveReplayPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.PendingInteractiveReplay.cs");
        var runtimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelPendingInteractiveReplayAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var runtimeSource = File.ReadAllText(runtimePath);

        Assert.False(File.Exists(pendingInteractiveReplayPath));
        Assert.Contains("new KernelPendingInteractiveReplayAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelPendingInteractiveServerRequest(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed class KernelPendingServerRequestResolvedException(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pendingInteractiveReplayAppHostRuntime.TryTrackPendingInteractiveRequest(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pendingInteractiveReplayAppHostRuntime.CleanupPendingInteractiveRequestMapping(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pendingInteractiveReplayAppHostRuntime.TryResolvePendingInteractiveRequestOnInterruptAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pendingInteractiveReplayAppHostRuntime.BuildPendingInteractiveRequestPayloads(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pendingInteractiveReplayAppHostRuntime.HandleServerRequestRespondAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandleServerRequestRespondAsync(", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelPendingInteractiveServerRequest(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelPendingServerRequestResolvedException", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelPendingInteractiveReplayAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public void TryTrackPendingInteractiveRequest(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public void CleanupPendingInteractiveRequestMapping(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task ResolvePendingInteractiveRequestsForThreadLifecycleAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<bool> TryResolvePendingInteractiveRequestOnInterruptAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public object[] BuildPendingInteractiveRequestPayloads(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task ReplayPendingInteractiveRequestsAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleServerRequestRespondAsync(", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelCodeModeProtocolHelpers_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var codeModeProtocolPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.CodeModeProtocol.cs");
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelCodeModeProtocolHelpers.cs");
        var appHostRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelCodeModeProtocolAppHostRuntime.cs");

        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);
        var appHostRuntimeSource = File.ReadAllText(appHostRuntimePath);

        Assert.False(File.Exists(codeModeProtocolPath));

        Assert.Contains("internal static class KernelCodeModeProtocolHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static object BuildCodeModeProtocolPayload(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string InferCodeModeStatus(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string? TryExtractCodeModeCellId(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);

        Assert.Contains("KernelCodeModeProtocolHelpers.BuildCodeModeProtocolPayload(", appHostRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelCodeModeProtocolAppHostRuntime_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var codeModeProtocolPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.CodeModeProtocol.cs");
        var appHostRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelCodeModeProtocolAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var appHostRuntimeSource = File.ReadAllText(appHostRuntimePath);

        Assert.False(File.Exists(codeModeProtocolPath));

        Assert.Contains("private readonly KernelCodeModeProtocolAppHostRuntime codeModeProtocolAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("new KernelCodeModeProtocolAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await codeModeProtocolAppHostRuntime.HandleCodeModeExecAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await codeModeProtocolAppHostRuntime.HandleCodeModeWaitAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("internal sealed class KernelCodeModeProtocolAppHostRuntime", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleCodeModeExecAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleCodeModeWaitAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelCodeModeRuntimeSupport.ParseExecFreeformInput(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelCodeModeProtocolHelpers.BuildCodeModeProtocolPayload(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("threadManager.GetOrAttachThread(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("runtimeThread.SetActiveTurn(turnId);", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("runtimeThread.ClearActiveTurn(turnId);", appHostRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelCodeModeRuntimeHelpers_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var toolRuntimePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ToolRuntime.cs");
        var codeModeRuntimePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.CodeModeRuntime.cs");
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelCodeModeRuntimeHelpers.cs");
        var appHostRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelCodeModeAppHostRuntime.cs");

        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);
        var appHostRuntimeSource = File.ReadAllText(appHostRuntimePath);

        Assert.False(File.Exists(toolRuntimePath));
        Assert.False(File.Exists(codeModeRuntimePath));

        Assert.Contains("internal sealed record KernelCodeModeToolReference(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelCodeModeRuntimeHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool ShouldIncludeCodeModeNestedTool(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelCodeModeEnabledTool BuildCodeModeEnabledTool(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelCodeModeToolReference ResolveCodeModeToolReference(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TrySplitQualifiedMcpToolName(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static JsonElement ConvertKernelToolResultToCodeModeResult(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static JsonElement ConvertToolResultToCodeModeResult(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryBuildCodeModeFunctionArguments(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryParseJsonElement(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static JsonElement? TryFindDynamicToolSchema(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string BuildCodeModeNestedToolCallItemId(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);

        Assert.Contains("KernelCodeModeRuntimeHelpers.ShouldIncludeCodeModeNestedTool(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelCodeModeRuntimeHelpers.BuildCodeModeEnabledTool(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelCodeModeRuntimeHelpers.ResolveCodeModeToolReference(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelCodeModeRuntimeHelpers.BuildCodeModeNestedToolCallItemId(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelCodeModeRuntimeHelpers.TryBuildCodeModeFunctionArguments(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelCodeModeRuntimeHelpers.ConvertKernelToolResultToCodeModeResult(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelCodeModeRuntimeHelpers.TryFindDynamicToolSchema(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelCodeModeRuntimeHelpers.ConvertToolResultToCodeModeResult(", appHostRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void McpServerSurfaceHelpers_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var mcpServerSurfacePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.McpServerSurface.cs");
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "McpServerSurfaceHelpers.cs");

        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);

        Assert.False(File.Exists(mcpServerSurfacePath));

        Assert.Contains("internal sealed record McpServerToolCallResult(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("internal static class McpServerSurfaceHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public const string McpServerTianShuToolName = \"tianshu\";", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public const string McpServerTianShuReplyToolName = \"tianshu-reply\";", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static object CreateMcpServerToolCallPayload(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static object CreateMcpServerTianShuToolDefinition(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static object CreateMcpServerTianShuReplyToolDefinition(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelApprovalPolicy? TryReadMcpApprovalPolicy(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelSandboxPolicyOverride? TryReadMcpSandboxOverride(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string? ResolveMcpToolCwd(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string? ReadMcpTianShuReplyThreadId(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string NormalizeMcpServerToolContent(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);

    }

    [Fact]
    public void McpServerSurfaceAppHostRuntime_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var mcpServerSurfacePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.McpServerSurface.cs");
        var paritySurfacePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.Surface.cs");
        var runtimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "McpServerSurfaceAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var runtimeSource = File.ReadAllText(runtimePath);

        Assert.Contains("new McpServerSurfaceAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.False(File.Exists(mcpServerSurfacePath));
        Assert.False(File.Exists(paritySurfacePath));
        Assert.Contains("await mcpServerSurfaceAppHostRuntime.HandleMcpServerToolsListAsync(id, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await mcpServerSurfaceAppHostRuntime.HandleMcpServerToolsCallAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await mcpServerSurfaceAppHostRuntime.HandleMcpServerOauthLoginAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await mcpServerSurfaceAppHostRuntime.HandleConfigMcpServerReloadAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await mcpServerSurfaceAppHostRuntime.HandleMcpServerStatusListAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await mcpServerSurfaceAppHostRuntime.WaitForPendingOauthNotificationsAsync(CancellationToken.None).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class McpServerSurfaceAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly ConcurrentDictionary<string, CancellationTokenSource> pendingMcpOauthLogins = new(StringComparer.OrdinalIgnoreCase);", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly ConcurrentDictionary<string, Task> pendingMcpOauthLoginTasks = new(StringComparer.OrdinalIgnoreCase);", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleMcpServerToolsListAsync(JsonElement id, CancellationToken cancellationToken)", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleMcpServerToolsCallAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleMcpServerOauthLoginAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleConfigMcpServerReloadAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleMcpServerStatusListAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<McpServerToolCallResult> ExecuteMcpTianShuToolAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<McpServerToolCallResult> ExecuteMcpTianShuReplyToolAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task UpdateMcpSandboxStateAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task WaitForPendingOauthNotificationsAsync(CancellationToken cancellationToken)", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task<McpServerToolCallResult> ExecuteMcpToolTurnAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private KernelThreadStartRequest BuildMcpTianShuThreadStartRequest(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private static KernelConfigOverridePayload? CreateMcpTianShuConfigOverride(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private static int? ReadInt(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private static long? ReadLong(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task WaitAndEmitMcpOauthCompletionAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("McpServerSurfaceHelpers.ReadMcpTianShuReplyThreadId(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("McpServerSurfaceHelpers.ResolveMcpToolCwd(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("McpServerSurfaceHelpers.TryReadMcpApprovalPolicy(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("McpServerSurfaceHelpers.TryReadMcpSandboxOverride(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("McpServerSurfaceHelpers.NormalizeMcpServerToolContent(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("McpServerAuthUtilities.ListMcpServerNamesAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("McpServerAuthUtilities.ResolveMcpServerAuthorizationUrlAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("McpServerAuthUtilities.ResolveMcpServerAuthStatus(", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelUserShellAppHostRuntime_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var userShellPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.UserShell.cs");
        var runtimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelUserShellAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var runtimeSource = File.ReadAllText(runtimePath);

        Assert.Contains("new KernelUserShellAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.False(File.Exists(userShellPath));
        Assert.Contains("await userShellAppHostRuntime.HandleUserShellRunAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelUserShellAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleUserShellRunAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelUserShellRunResultPayload> ExecuteAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task<KernelUserShellRunResultPayload> RunStandaloneUserShellTurnAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task<KernelUserShellExecutionResult> ExecuteUserShellCommandCoreAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task PersistStandaloneUserShellTurnAsync(", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelAgentJobsAppHostRuntime_ShouldOwnNorthboundAgentJobSurface()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var threadLifecyclePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ThreadLifecycle.cs");
        var runtimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelAgentJobsAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var threadLifecycleSource = ReadKernelThreadLifecycleFacadeSource(repoRoot);
        var runtimeSource = File.ReadAllText(runtimePath);

        Assert.Contains("private readonly KernelAgentJobsAppHostRuntime agentJobsAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("this.agentJobsAppHostRuntime = new KernelAgentJobsAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await agentJobsAppHostRuntime.HandleAgentJobCreateAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await agentJobsAppHostRuntime.HandleAgentJobDispatchAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await agentJobsAppHostRuntime.HandleAgentJobItemReportAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await agentJobsAppHostRuntime.HandleAgentJobReadAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);

        Assert.DoesNotContain("private async Task HandleAgentJobCreateAsync(", threadLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandleAgentJobDispatchAsync(", threadLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandleAgentJobItemReportAsync(", threadLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandleAgentJobReadAsync(", threadLifecycleSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelAgentJobsAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleAgentJobCreateAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleAgentJobDispatchAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleAgentJobItemReportAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleAgentJobReadAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelAgentOrchestrationManager", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelSpawnAgentsOnCsvRuntimeHelpers_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var agentJobsRuntimePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.AgentJobsToolRuntime.cs");
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var toolRuntimePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ToolRuntime.cs");
        var bridgeRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolRuntimeServicesAppHostRuntime.cs");
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelSpawnAgentsOnCsvRuntimeHelpers.cs");
        var runtimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelSpawnAgentsOnCsvAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var bridgeRuntimeSource = File.ReadAllText(bridgeRuntimePath);
        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);
        var runtimeSource = File.ReadAllText(runtimePath);

        Assert.False(File.Exists(agentJobsRuntimePath));
        Assert.False(File.Exists(toolRuntimePath));

        Assert.Contains("internal static class KernelSpawnAgentsOnCsvRuntimeHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelSpawnAgentsOnCsvResponse BuildSpawnAgentsOnCsvResponse(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<string> ParseAgentJobHeaders(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static List<(string? ItemId, string? SourceId, string RowJson)> BuildAgentJobItems(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string BuildAgentJobWorkerPrompt(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string RenderAgentJobInstructionTemplate(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static int NormalizeSpawnAgentsOnCsvConcurrency(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool IsSpawnAgentThreadLimitError(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string RenderAgentJobCsv(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);

        Assert.Contains("private readonly KernelSpawnAgentsOnCsvAppHostRuntime spawnAgentsOnCsvAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("this.spawnAgentsOnCsvAppHostRuntime = new KernelSpawnAgentsOnCsvAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("spawnAgentsOnCsvAppHostRuntime.ExecuteAsync(", bridgeRuntimeSource, StringComparison.Ordinal);

        Assert.Contains("internal sealed class KernelSpawnAgentsOnCsvAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelSpawnAgentsOnCsvResponse> ExecuteAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<bool> RecoverRunningAgentJobWorkersAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelSpawnAgentsOnCsvRuntimeHelpers.ParseAgentJobCsv(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelSpawnAgentsOnCsvRuntimeHelpers.BuildAgentJobItems(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelSpawnAgentsOnCsvRuntimeHelpers.BuildAgentJobWorkerPrompt(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelSpawnAgentsOnCsvRuntimeHelpers.NormalizeSpawnAgentsOnCsvConcurrency(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelSpawnAgentsOnCsvRuntimeHelpers.ResolveConfiguredAgentJobMaxRuntimeSeconds(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelSpawnAgentsOnCsvRuntimeHelpers.RenderAgentJobCsv(", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelAutoCompactionRuntime_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var autoCompactionPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.AutoCompaction.cs");
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var turnExecutionRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnExecutionAppHostRuntime.cs");
        var turnExecutionRuntimeCompositionPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnExecutionRuntimeComposition.cs");
        var responsesFollowUpInputRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelResponsesFollowUpInputRuntime.cs");
        var autoCompactionRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelAutoCompactionAppHostRuntime.cs");
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelAutoCompactionRuntimeHelpers.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var turnExecutionRuntimeSource = File.ReadAllText(turnExecutionRuntimePath);
        var turnExecutionRuntimeCompositionSource = File.ReadAllText(turnExecutionRuntimeCompositionPath);
        var responsesFollowUpInputRuntimeSource = File.ReadAllText(responsesFollowUpInputRuntimePath);
        var autoCompactionRuntimeSource = File.ReadAllText(autoCompactionRuntimePath);
        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);

        Assert.False(File.Exists(autoCompactionPath));
        Assert.DoesNotContain("private static List<object> BuildResponsesFollowUpInput(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelAutoCompactionAppHostRuntime autoCompactionAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("new KernelAutoCompactionAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("autoCompactionAppHostRuntime.MaybeRunPreSamplingAutoCompactAsync", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("autoCompactionAppHostRuntime.MaybeBuildMidTurnAutoCompactedFollowUpInputAsync", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("internal static class KernelAutoCompactionRuntimeHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static long? ResolveConfiguredModelAutoCompactTokenLimit(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static long EstimatePromptTokenCount(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static long EstimateResponsesFollowUpTokenCount(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static List<object> BuildResponsesFollowUpInput(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", autoCompactionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelAutoCompactionAppHostRuntime", autoCompactionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task MaybeRunPreSamplingAutoCompactAsync(", autoCompactionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<List<object>?> MaybeBuildMidTurnAutoCompactedFollowUpInputAsync(", autoCompactionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelAutoCompactionRuntimeHelpers.EstimatePromptTokenCount(", autoCompactionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelAutoCompactionRuntimeHelpers.EstimateResponsesFollowUpTokenCount(", autoCompactionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelAutoCompactionRuntimeHelpers.BuildResponsesFollowUpInput(", autoCompactionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelTurnExecutionRuntimeHelpers.ResolveTurnInstructions(", autoCompactionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelTurnExecutionRuntimeHelpers.ResolveTurnDeveloperMessage(", autoCompactionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelTurnExecutionRuntimeHelpers.BuildProviderMessages(", autoCompactionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelTurnExecutionRuntimeHelpers.BuildResponsesConversationInput(", autoCompactionRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<TurnRequestContext, string> resolveTurnInstructions", autoCompactionRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<TurnRequestContext, bool, string?> resolveTurnDeveloperMessage", autoCompactionRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<KernelThreadRecord?, string, string?, IReadOnlyList<string>?, IReadOnlyList<KernelTurnInputItem>?, List<Dictionary<string, object?>>> buildProviderMessages", autoCompactionRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<KernelThreadRecord?, string, string?, IReadOnlyList<string>?, IReadOnlyList<KernelTurnInputItem>?, List<object>> buildResponsesConversationInput", autoCompactionRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly KernelResponsesFollowUpInputRuntime responsesFollowUpInputRuntime;", turnExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelResponsesFollowUpInputRuntime responsesFollowUpInputRuntime;", turnExecutionRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("KernelTurnExecutionRuntimeHelpers.BuildSlicedResponsesFollowUpInput(", responsesFollowUpInputRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelSubagentNotificationRuntime_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var oldPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.SubagentNotifications.cs");
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var runtimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelSubagentNotificationAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var runtimeSource = File.ReadAllText(runtimePath);

        Assert.False(File.Exists(oldPath));
        Assert.Contains("private readonly KernelSubagentNotificationAppHostRuntime subagentNotificationAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("new KernelSubagentNotificationAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("subagentNotificationAppHostRuntime.FormatEnvironmentContextSubagents(threadId)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("subagentNotificationAppHostRuntime.MaybeStartSubagentCompletionWatcher", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private void MaybeStartSubagentCompletionWatcher(", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelSubagentNotificationAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public string? FormatEnvironmentContextSubagents(string parentThreadId)", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public void MaybeStartSubagentCompletionWatcher(string childThreadId, KernelSessionSource? sessionSource)", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelSubagentNotificationUtilities.Format(childThreadId, status)", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelJsReplRuntimeHelpers_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var toolRuntimePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ToolRuntime.cs");
        var jsReplRuntimePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.JsReplRuntime.cs");
        var codeModeRuntimePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.CodeModeRuntime.cs");
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelJsReplRuntimeHelpers.cs");
        var codeModeAppHostRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelCodeModeAppHostRuntime.cs");
        var jsReplAppHostRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelJsReplAppHostRuntime.cs");

        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);
        var codeModeAppHostRuntimeSource = File.ReadAllText(codeModeAppHostRuntimePath);
        var jsReplAppHostRuntimeSource = File.ReadAllText(jsReplAppHostRuntimePath);

        Assert.False(File.Exists(toolRuntimePath));
        Assert.False(File.Exists(jsReplRuntimePath));
        Assert.False(File.Exists(codeModeRuntimePath));

        Assert.Contains("internal static class KernelJsReplRuntimeHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelJsReplOptions ResolveJsReplOptions(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string BuildJsReplNestedToolCallItemId(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);

        Assert.Contains("KernelJsReplRuntimeHelpers.ResolveJsReplOptions(", codeModeAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelJsReplRuntimeHelpers.ResolveJsReplOptions(", jsReplAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelJsReplRuntimeHelpers.BuildJsReplNestedToolCallItemId(", jsReplAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ToolUseFollowUpItemProjector.BuildFunctionCallOutputItem(", jsReplAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"type\"] = \"function_call_output\"", jsReplAppHostRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelArtifactsRuntimeHelpers_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var toolRuntimePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ToolRuntime.cs");
        var artifactsRuntimePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ArtifactsRuntime.cs");
        var nativeToolsPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.NativeTools.cs");
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelArtifactsRuntimeHelpers.cs");
        var appHostRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelArtifactsAppHostRuntime.cs");
        var nativeToolOptionsRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelNativeToolOptionsAppHostRuntime.cs");

        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);
        var appHostRuntimeSource = File.ReadAllText(appHostRuntimePath);
        var nativeToolOptionsRuntimeSource = File.ReadAllText(nativeToolOptionsRuntimePath);

        Assert.False(File.Exists(toolRuntimePath));
        Assert.False(File.Exists(artifactsRuntimePath));
        Assert.False(File.Exists(nativeToolsPath));

        Assert.DoesNotContain("private static bool ResolveConfiguredArtifactEnabled(", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("internal static class KernelArtifactsRuntimeHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelArtifactsRuntimeOptions ResolveArtifactsRuntimeOptions(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool ResolveConfiguredArtifactEnabled(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);

        Assert.Contains("KernelArtifactsRuntimeHelpers.ResolveArtifactsRuntimeOptions(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelArtifactsRuntimeHelpers.ResolveConfiguredArtifactEnabled(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("KernelArtifactsRuntimeHelpers.ResolveConfiguredArtifactEnabled(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelArtifactsRuntimeHelpers.ResolveConfiguredArtifactEnabled(", nativeToolOptionsRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelArtifactsCodeModeAndJsReplAppHostRuntime_ShouldLiveUnderAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerFile = GetAppHostServerSourcePath(repoRoot);
        var toolRuntimeFile = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ToolRuntime.cs");
        var bridgeRuntimeFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolRuntimeServicesAppHostRuntime.cs");
        var artifactsRuntimeFile = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ArtifactsRuntime.cs");
        var codeModeRuntimeFile = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.CodeModeRuntime.cs");
        var jsReplRuntimeFile = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.JsReplRuntime.cs");
        var artifactsAppHostRuntimeFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelArtifactsAppHostRuntime.cs");
        var codeModeAppHostRuntimeFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelCodeModeAppHostRuntime.cs");
        var jsReplAppHostRuntimeFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelJsReplAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerFile);
        var bridgeRuntimeSource = File.ReadAllText(bridgeRuntimeFile);
        var artifactsAppHostRuntimeSource = File.ReadAllText(artifactsAppHostRuntimeFile);
        var codeModeAppHostRuntimeSource = File.ReadAllText(codeModeAppHostRuntimeFile);
        var jsReplAppHostRuntimeSource = File.ReadAllText(jsReplAppHostRuntimeFile);

        Assert.False(File.Exists(toolRuntimeFile));
        Assert.False(File.Exists(artifactsRuntimeFile));
        Assert.False(File.Exists(codeModeRuntimeFile));
        Assert.False(File.Exists(jsReplRuntimeFile));

        Assert.Contains("new KernelArtifactsAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("new KernelCodeModeAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("new KernelJsReplAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("artifactsRuntime.ExecuteAsync(", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("codeModeRuntime.ExecuteAsync(", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("codeModeRuntime.WaitAsync(", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("jsReplRuntime.ExecuteAsync(", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("jsReplRuntime.ResetAsync(", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("codeModeRuntime!.BuildEnabledTools(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("codeModeRuntime.DeactivateTurn(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("codeModeRuntime.DisposeAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("jsReplRuntime!.DisposeManagerAsync(", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", artifactsAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelArtifactsAppHostContext(", artifactsAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelArtifactsAppHostRuntime", artifactsAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelArtifactsExecutionResult> ExecuteAsync(", artifactsAppHostRuntimeSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", codeModeAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelCodeModeAppHostContext(", codeModeAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelCodeModeAppHostRuntime", codeModeAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelCodeModeOperationResult> ExecuteAsync(", codeModeAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelCodeModeOperationResult> WaitAsync(", codeModeAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyList<KernelCodeModeEnabledTool> BuildEnabledTools(", codeModeAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public void DeactivateTurn(", codeModeAppHostRuntimeSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", jsReplAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelJsReplAppHostContext(", jsReplAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelJsReplAppHostRuntime", jsReplAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelJsReplExecutionResult> ExecuteAsync(", jsReplAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task ResetAsync(", jsReplAppHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async ValueTask DisposeManagerAsync(", jsReplAppHostRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelToolItemLifecycleHelpers_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var toolItemLifecyclePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ToolItemLifecycle.cs");
        var mcpToolLifecyclePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.McpToolLifecycle.cs");
        var webSearchLifecyclePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.WebSearchLifecycle.cs");
        var imageGenerationLifecyclePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ImageGenerationLifecycle.cs");
        var lifecycleRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolItemLifecycleAppHostRuntime.cs");
        var commandExecPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.CommandExec.cs");
        var commandExecRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelCommandExecAppHostRuntime.cs");
        var commandExecSurfaceRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelCommandExecSurfaceAppHostRuntime.cs");
        var userShellPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.UserShell.cs");
        var userShellRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelUserShellAppHostRuntime.cs");
        var parityPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolItemLifecycleHelpers.cs");

        var toolItemLifecycleSource = File.ReadAllText(lifecycleRuntimePath);
        var commandExecRuntimeSource = File.ReadAllText(commandExecRuntimePath);
        var commandExecSurfaceRuntimeSource = File.ReadAllText(commandExecSurfaceRuntimePath);
        var userShellRuntimeSource = File.ReadAllText(userShellRuntimePath);
        var paritySource = AssertFileDeletedAndReturnEmpty(parityPath);
        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);

        Assert.False(File.Exists(toolItemLifecyclePath));
        Assert.False(File.Exists(commandExecPath));
        Assert.False(File.Exists(userShellPath));
        Assert.False(File.Exists(mcpToolLifecyclePath));
        Assert.False(File.Exists(webSearchLifecyclePath));
        Assert.False(File.Exists(imageGenerationLifecyclePath));
        Assert.DoesNotContain("private static object[]? BuildDynamicToolContentItems(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object[] BuildFileChangeChanges(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? ResolveImageViewPath(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string TryGetCommandExecutionStatusFromExitCode(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? BuildCommandExecutionAggregatedOutput(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object BuildCommandExecutionItemPayload(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelMcpToolLifecycleDescriptor(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryCreateMcpToolLifecycleDescriptor(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object CreateMcpToolCallItem(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object CreateMcpToolCallResultPayload(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object[] BuildMcpToolResultContent(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record WebSearchLifecycleObservation(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static IReadOnlyList<WebSearchLifecycleObservation> CaptureWebSearchOutputItems(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? ExtractWebSearchQuery(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object BuildWebSearchNotificationItem(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record ImageGenerationLifecycleObservation(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static async Task<IReadOnlyList<ImageGenerationLifecycleObservation>> CaptureImageGenerationOutputItemsAsync(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static async Task<string> SaveImageGenerationResultToCwdAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string SanitizeGeneratedImageFileName(", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("internal sealed record KernelMcpToolLifecycleDescriptor(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record WebSearchLifecycleObservation(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record ImageGenerationLifecycleObservation(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelToolItemLifecycleHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static object[]? BuildDynamicToolContentItems(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static object[] BuildFileChangeChanges(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string? ResolveImageViewPath(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string TryGetCommandExecutionStatusFromExitCode(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string? BuildCommandExecutionAggregatedOutput(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static object BuildCommandExecutionItemPayload(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryCreateMcpToolLifecycleDescriptor(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static object CreateMcpToolCallItem(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static object CreateMcpToolCallResultPayload(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static object? ConvertJsonElementToObject(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<WebSearchLifecycleObservation> CaptureWebSearchOutputItems(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static object BuildWebSearchNotificationItem(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static async Task<IReadOnlyList<ImageGenerationLifecycleObservation>> CaptureImageGenerationOutputItemsAsync(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static async Task<string> SaveImageGenerationResultToCwdAsync(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelToolItemLifecycleAppHostRuntime", toolItemLifecycleSource, StringComparison.Ordinal);

        Assert.Contains("KernelToolItemLifecycleHelpers.BuildDynamicToolContentItems(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolItemLifecycleHelpers.BuildFileChangeChanges(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolItemLifecycleHelpers.ResolveImageViewPath(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolItemLifecycleHelpers.BuildCommandExecutionItemPayload(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolItemLifecycleHelpers.TryCreateMcpToolLifecycleDescriptor(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolItemLifecycleHelpers.CreateMcpToolCallItem(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolItemLifecycleHelpers.CaptureWebSearchOutputItems(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolItemLifecycleHelpers.BuildWebSearchNotificationItem(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolItemLifecycleHelpers.CaptureImageGenerationOutputItemsAsync(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolItemLifecycleHelpers.CaptureImageGenerationOutputItemsAsync(", toolItemLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolItemLifecycleHelpers.TryGetCommandExecutionStatusFromExitCode(", commandExecRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolItemLifecycleHelpers.TryGetCommandExecutionStatusFromExitCode(", commandExecSurfaceRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolItemLifecycleHelpers.BuildCommandExecutionAggregatedOutput(", commandExecSurfaceRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolItemLifecycleHelpers.BuildCommandExecutionAggregatedOutput(", userShellRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelToolItemLifecycleHelpers.TryGetCommandExecutionStatusFromExitCode(", paritySource, StringComparison.Ordinal);
        Assert.Contains("KernelToolItemLifecycleHelpers.ConvertJsonElementToObject(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolItemLifecycleHelpers.SaveImageGenerationResultToCwdAsync(", kernelAppServerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelToolRuntimeParsingHelpers_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelToolsPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelTools.cs");
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var codeModeRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelCodeModeAppHostRuntime.cs");
        var turnExecutionRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnExecutionAppHostRuntime.cs");
        var turnAssistantExecutionRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnAssistantExecutionRuntime.cs");
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolRuntimeParsingHelpers.cs");

        var kernelToolsSource = AssertFileDeletedAndReturnEmpty(kernelToolsPath);
        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var codeModeRuntimeSource = File.ReadAllText(codeModeRuntimePath);
        var turnExecutionRuntimeSource = File.ReadAllText(turnExecutionRuntimePath);
        var turnAssistantExecutionRuntimeSource = File.ReadAllText(turnAssistantExecutionRuntimePath);
        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);

        Assert.DoesNotContain("private static readonly Regex InlineToolRegex", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryResolveDynamicToolSchema(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryResolveDynamicToolDescriptor(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static IReadOnlyList<KernelToolOutputContentItem>? ReadDynamicToolOutputContentItems(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static IReadOnlyList<JsonElement>? ReadDynamicToolRawContentItems(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static JsonElement? ReadDynamicToolStructuredOutput(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static JsonElement? ReadDynamicToolMetadata(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelToolOutputContentItem? TryConvertDynamicToolContentItem(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ExtractDynamicToolOutput(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryParseInlineToolCall(", kernelToolsSource, StringComparison.Ordinal);

        Assert.Contains("internal static class KernelToolRuntimeParsingHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryResolveDynamicToolSchema(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryResolveDynamicToolDescriptor(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<KernelToolOutputContentItem>? ReadDynamicToolOutputContentItems(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string ExtractDynamicToolOutput(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryParseInlineToolCall(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);

        Assert.DoesNotContain("KernelToolRuntimeParsingHelpers.TryParseInlineToolCall(", turnExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeParsingHelpers.TryParseInlineToolCall(", turnAssistantExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeParsingHelpers.TryResolveDynamicToolSchema(", codeModeRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelToolRuntimeApprovalHelpers_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelToolsPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelTools.cs");
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolRuntimeApprovalHelpers.cs");
        var toolCallRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolCallAppHostRuntime.cs");

        var kernelToolsSource = AssertFileDeletedAndReturnEmpty(kernelToolsPath);
        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);
        var toolCallRuntimeSource = File.ReadAllText(toolCallRuntimePath);

        Assert.DoesNotContain("private bool IsDynamicToolApprovalAcceptedForSession(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool DynamicToolRequiresApproval(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, object?> BuildDynamicToolApprovalMetadata(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool IsFileChangeApprovalTool(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool AreFileChangesApprovedForSession(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool ResolveRequestPermissionsEnabled(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool IsBuiltInToolExecutionEnabled(", kernelToolsSource, StringComparison.Ordinal);

        Assert.Contains("internal static class KernelToolRuntimeApprovalHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool IsDynamicToolApprovalAcceptedForSession(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool DynamicToolRequiresApproval(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, object?> BuildDynamicToolApprovalMetadata(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool IsFileChangeApprovalTool(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool AreFileChangesApprovedForSession(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool ResolveRequestPermissionsEnabled(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool IsBuiltInToolExecutionEnabled(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);

        Assert.DoesNotContain("KernelToolRuntimeApprovalHelpers.IsBuiltInToolExecutionEnabled(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelToolRuntimeApprovalHelpers.ResolveRequestPermissionsEnabled(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelToolRuntimeApprovalHelpers.BuildDynamicToolApprovalMetadata(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelToolRuntimeApprovalHelpers.MarkFileChangesApprovedForSession(", kernelToolsSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeApprovalHelpers.IsBuiltInToolExecutionEnabled(", toolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeApprovalHelpers.ResolveRequestPermissionsEnabled(", toolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeApprovalHelpers.BuildDynamicToolApprovalMetadata(", toolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeApprovalHelpers.MarkFileChangesApprovedForSession(", toolCallRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelToolCallAppHostRuntime_ShouldOwnToolCallExecutionOrchestration()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var kernelToolsPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelTools.cs");
        var toolExecutionRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolExecutionAppHostRuntime.cs");
        var toolCallRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolCallAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var kernelToolsSource = AssertFileDeletedAndReturnEmpty(kernelToolsPath);
        var toolExecutionRuntimeSource = File.ReadAllText(toolExecutionRuntimePath);
        var toolCallRuntimeSource = File.ReadAllText(toolCallRuntimePath);

        Assert.Contains("private readonly KernelToolCallAppHostRuntime toolCallAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelToolExecutionAppHostRuntime toolExecutionAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("this.toolCallAppHostRuntime = new KernelToolCallAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("this.toolExecutionAppHostRuntime = new KernelToolExecutionAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new KernelToolCallAppHostRuntimeContext(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("toolCallAppHostRuntime.ExecuteToolCallAsync(", kernelToolsSource, StringComparison.Ordinal);
        Assert.Contains("toolExecutionAppHostRuntime.ExecuteToolCallAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<KernelToolResult> ExecuteDynamicToolAsync(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task InvokeToolHooksBeforeAsync(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<KernelToolExecutionHookAfterDecision> InvokeToolHooksAfterAsync(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task InvokeToolHooksErrorAsync(", kernelToolsSource, StringComparison.Ordinal);

        Assert.Contains("internal sealed class KernelToolExecutionAppHostRuntime", toolExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelToolResult> ExecuteToolCallAsync(", toolExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("new KernelToolCallAppHostRuntimeContext(", toolExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("toolCallAppHostRuntime.ExecuteToolCallAsync(", toolExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelToolCallAppHostRuntimeContext(", toolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelToolCallAppHostRuntime", toolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelToolResult> ExecuteToolCallAsync(", toolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task<KernelToolResult> ExecuteDynamicToolAsync(", toolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task InvokeToolHooksBeforeAsync(", toolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task<KernelToolExecutionHookAfterDecision> InvokeToolHooksAfterAsync(", toolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task InvokeToolHooksErrorAsync(", toolCallRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutionIntegrationTests_ShouldNotReflectRemovedAppHostRuntimeForwardingBridges()
    {
        var repoRoot = FindRepoRoot();
        var integrationTestPath = Path.Combine(
            repoRoot,
            "tests",
            "TianShu.Execution.Integration.Tests",
            "Migrated",
            "KernelAppServer",
            "KernelAppServerTests.cs");

        var source = File.ReadAllText(integrationTestPath);
        var removedForwardingBridgeNames = new[]
        {
            "RequestManagedNetworkApprovalAsync",
            "EmitManagedNetworkSideEffectAsync",
            "ExecuteModelFunctionCallAsync",
            "MarkFileChangesApprovedForSession",
            "HandleServerRequestRespondAsync",
        };

        foreach (var bridgeName in removedForwardingBridgeNames)
        {
            Assert.DoesNotContain($"GetMethod(\"{bridgeName}\"", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KernelNativeToolOptionsAppHostRuntime_ShouldOwnToolSurfaceAvailabilityOrchestration()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var kernelToolRuntimePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ToolRuntime.cs");
        var kernelToolsPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelTools.cs");
        var nativeToolsPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.NativeTools.cs");
        var toolExecutionRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolExecutionAppHostRuntime.cs");
        var nativeToolOptionsRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelNativeToolOptionsAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var kernelToolsSource = AssertFileDeletedAndReturnEmpty(kernelToolsPath);
        var toolExecutionRuntimeSource = File.ReadAllText(toolExecutionRuntimePath);
        var nativeToolOptionsRuntimeSource = File.ReadAllText(nativeToolOptionsRuntimePath);

        Assert.Contains("private readonly KernelNativeToolOptionsAppHostRuntime nativeToolOptionsAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelToolExecutionAppHostRuntime toolExecutionAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("this.nativeToolOptionsAppHostRuntime = new KernelNativeToolOptionsAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.False(File.Exists(kernelToolRuntimePath));
        Assert.False(File.Exists(nativeToolsPath));
        Assert.DoesNotContain("new KernelNativeToolOptionsAppHostRuntimeContext(", kernelToolsSource, StringComparison.Ordinal);
        Assert.Contains("toolExecutionAppHostRuntime!.ResolveResponsesNativeToolOptionsAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<KernelResponsesNativeToolOptions> ResolveResponsesNativeToolOptionsAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ResolveConfiguredWebSearchMode(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool ResolveConfiguredShellToolEnabled(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool ResolveConfiguredFeatureFlag(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? ReadConfiguredWebSearchValue(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool SupportsImageGenerationModel(", kernelToolsSource, StringComparison.Ordinal);

        Assert.Contains("internal sealed class KernelToolExecutionAppHostRuntime", toolExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelResponsesNativeToolOptions> ResolveResponsesNativeToolOptionsAsync(", toolExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("new KernelNativeToolOptionsAppHostRuntimeContext(", toolExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("nativeToolOptionsAppHostRuntime.ResolveResponsesNativeToolOptionsAsync(", toolExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelNativeToolOptionsAppHostRuntimeContext(", nativeToolOptionsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelNativeToolOptionsAppHostRuntime", nativeToolOptionsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelResponsesNativeToolOptions> ResolveResponsesNativeToolOptionsAsync(", nativeToolOptionsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static string ResolveConfiguredWebSearchMode(", nativeToolOptionsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static bool ResolveConfiguredShellToolEnabled(", nativeToolOptionsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static bool ResolveConfiguredFeatureFlag(", nativeToolOptionsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static string? ReadConfiguredWebSearchValue(", nativeToolOptionsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static bool SupportsImageGenerationModel(", nativeToolOptionsRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelThreadLifecycleAppHostRuntime_ShouldOwnThreadLifecycleOrchestration()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var threadLifecyclePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ThreadLifecycle.cs");
        var resumeForkPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ResumeFork.cs");
        var runtimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelThreadLifecycleAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var threadLifecycleSource = ReadKernelThreadLifecycleFacadeSource(repoRoot);
        var runtimeSource = File.ReadAllText(runtimePath);

        Assert.Contains("private readonly KernelThreadLifecycleAppHostRuntime threadLifecycleAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("this.threadLifecycleAppHostRuntime = new KernelThreadLifecycleAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await threadLifecycleAppHostRuntime.HandleThreadArchiveAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await threadLifecycleAppHostRuntime.HandleThreadDeleteAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await threadLifecycleAppHostRuntime.HandleThreadUnsubscribeAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await threadLifecycleAppHostRuntime.HandleThreadUnarchiveAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await threadLifecycleAppHostRuntime.HandleThreadRollbackAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await threadLifecycleAppHostRuntime.HandleThreadCompactStartAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await threadLifecycleAppHostRuntime.HandleThreadBackgroundTerminalsCleanAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await threadLifecycleAppHostRuntime.HandleThreadLoadedListAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);

        Assert.False(File.Exists(threadLifecyclePath));
        Assert.Contains("threadLifecycleAppHostRuntime.HandleThreadListAsync(", threadLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("threadLifecycleAppHostRuntime.HandleThreadStartAsync(", threadLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("threadLifecycleAppHostRuntime.HandleThreadResumeAsync(", threadLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("threadLifecycleAppHostRuntime.HandleThreadReadAsync(", threadLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("threadLifecycleAppHostRuntime.HandleThreadForkAsync(", threadLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("threadLifecycleAppHostRuntime.HandleThreadIncrementElicitationAsync(", threadLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("threadLifecycleAppHostRuntime.HandleThreadDecrementElicitationAsync(", threadLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("threadLifecycleAppHostRuntime.HandleAgentThreadRegisterAsync(", threadLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("threadLifecycleAppHostRuntime.HandleThreadSetNameAsync(", threadLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateThreadFromHistoryAsync(request, historyOverride", threadLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadThreadFromRolloutPathAsync(requestedPath", threadLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildForkedThreadSession(sourceSession, effectiveForkCwd, request)", threadLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("var configuredDefaults = ResolveConfiguredThreadDefaultsWithThreadError(cwd);", threadLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimeThread.IncrementOutOfBandElicitationCount()", threadLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("threadStore.SetThreadAgentMetadataAsync(", threadLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("threadStore.SetThreadNameAsync(", threadLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteBroadcastNotificationAsync(\"thread/name/updated\"", threadLifecycleSource, StringComparison.Ordinal);

        Assert.False(File.Exists(resumeForkPath));
        Assert.Contains("threadLifecycleAppHostRuntime.EnsureThreadRolloutMaterializedAsync,", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("threadLifecycleAppHostRuntime.LoadThreadRecordPreferringRolloutAsync,", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("threadLifecycleAppHostRuntime.PersistThreadConfigSnapshotAsync,", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelThreadLifecycleAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadListAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadStartAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadResumeAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadReadAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadForkAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadIncrementElicitationAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadDecrementElicitationAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleAgentThreadRegisterAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadSetNameAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadArchiveAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task DrainLoadedThreadAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadDeleteAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadUnarchiveAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadMetadataUpdateAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadPendingInputStateUpdateAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadRollbackAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadUnsubscribeAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadCompactStartAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadBackgroundTerminalsCleanAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadLoadedListAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task PersistThreadConfigSnapshotAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task EnsureThreadRolloutMaterializedAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public bool TryGetRunningThread(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelThreadRecord?> LoadThreadRecordPreferringRolloutAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task WriteRunningThreadResumeResponseAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task<KernelThreadRecord?> LoadThreadFromRolloutPathAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("removeTrackedThreadSubscription", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("forgetThreadSubscription", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("resolvePendingInteractiveRequestsForThreadLifecycleAsync", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("resolvePendingUserInputRequestsForThreadLifecycleAsync", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("closeRealtimeTransportAsync", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("releaseSpawnedAgentThread", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("toThreadPayload", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("tryBeginThreadRollback", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("endThreadRollback", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("cleanBackgroundTerminals", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelThreadHistoryAppHostRuntime_ShouldOwnThreadHistoryStateTracking()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var threadHistoryPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ThreadHistory.cs");
        var runtimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelThreadHistoryAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var runtimeSource = File.ReadAllText(runtimePath);

        Assert.False(File.Exists(threadHistoryPath));
        Assert.Contains("private readonly KernelThreadHistoryAppHostRuntime threadHistoryAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("this.threadHistoryAppHostRuntime = new KernelThreadHistoryAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("threadHistoryAppHostRuntime.TryTrackTurnNotification(method, @params);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("threadHistoryAppHostRuntime.BuildTrackedActiveTurnSnapshot(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("threadHistoryAppHostRuntime.GetTrackedAgentMessageText,", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("threadHistoryAppHostRuntime.RegisterPendingTurnInterrupt,", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("threadHistoryAppHostRuntime.SeedTrackedTurnUserMessage(threadId, turnId, userText, inputItems)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("threadHistoryAppHostRuntime.RegisterPendingTurnInterruptResponse,", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("threadHistoryAppHostRuntime.ClearPendingTurnInterruptResponses,", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool TryBeginThreadRollback(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private void SeedTrackedTurnUserMessage(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private void TryTrackTurnNotification(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<KernelRequestUserInputResponse> RequestUserInputFromToolAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<KernelSpawnAgentResponse> SpawnAgentFromToolCoreAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<KernelSpawnAgentResponse> SpawnAgentFromToolAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task DisposeAllCodeModeManagersAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private void MarkFileChangesApprovedForSession(", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelThreadHistoryAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly ConcurrentDictionary<string, KernelThreadExecutionState> executionStatesByThread", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly ConcurrentDictionary<string, ConcurrentQueue<JsonElement>> pendingTurnInterruptResponseIds", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public void TryTrackTurnNotification(string method, object @params)", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public void TryTrackTurnNotification(string method, JsonElement parameters)", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public void RegisterPendingTurnInterruptResponse(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyList<JsonElement> DrainPendingTurnInterruptResponses(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public bool HasTrackedTurnActivity(", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelTurnExecutionAppHostRuntime_ShouldOwnTurnExecutionAndBackgroundTurnOrchestration()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var threadLifecyclePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ThreadLifecycle.cs");
        var kernelToolRuntimePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ToolRuntime.cs");
        var runtimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnExecutionAppHostRuntime.cs");
        var runtimeCompositionPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnExecutionRuntimeComposition.cs");
        var backgroundSchedulerRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnBackgroundSchedulerRuntime.cs");
        var turnInterruptRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnInterruptRuntime.cs");
        var turnLaunchRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnLaunchRuntime.cs");
        var turnModelStageRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnModelStageRuntime.cs");
        var activeSnapshotRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnActiveSnapshotRuntime.cs");
        var turnStartStateRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnStartStateRuntime.cs");
        var turnTerminalStateRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnTerminalStateRuntime.cs");
        var turnFinalizationRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnFinalizationRuntime.cs");
        var turnStageNotificationRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnStageNotificationRuntime.cs");
        var turnInputResolutionRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnInputResolutionRuntime.cs");
        var turnDependencyResolutionRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnDependencyResolutionRuntime.cs");
        var turnAssistantExecutionRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnAssistantExecutionRuntime.cs");
        var turnProviderAssistantRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnProviderAssistantRuntime.cs");
        var turnAssistantOutputStreamingRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnAssistantOutputStreamingRuntime.cs");
        var turnSteerInputRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnSteerInputRuntime.cs");
        var responsesStreamNotificationRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelResponsesStreamNotificationRuntime.cs");
        var responsesStreamFailureRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelResponsesStreamFailureRuntime.cs");
        var responsesStreamProcessingRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelResponsesStreamProcessingRuntime.cs");
        var responsesAssistantCompletionRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelResponsesAssistantCompletionRuntime.cs");
        var responsesRequestCompositionRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelResponsesRequestCompositionRuntime.cs");
        var responsesToolContinuationRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelResponsesToolContinuationRuntime.cs");
        var modelFunctionToolCallRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelModelFunctionToolCallRuntime.cs");
        var toolUseFollowUpItemProjectorPath = Path.Combine(repoRoot, "src", "Contracts", "TianShu.Contracts.Tools", "Models", "ToolUseFollowUpItemProjector.cs");
        var responsesFollowUpInputRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelResponsesFollowUpInputRuntime.cs");
        var responsesHttpStreamTransportRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelResponsesHttpStreamTransportRuntime.cs");
        var responsesWebSocketStreamTransportRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelResponsesWebSocketStreamTransportRuntime.cs");
        var contextSlicingDiagnosticsRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelContextSlicingDiagnosticsRuntime.cs");
        var providerRequestDiagnosticsRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelProviderRequestDiagnosticsRuntime.cs");
        var terminalTurnProjectionCommitRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTerminalTurnProjectionCommitRuntime.cs");
        var turnRunnerRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnRunnerRuntime.cs");
        var stageExecutorRunRequestFactoryPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelStageExecutorRunRequestFactory.cs");
        var turnOperationChainRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnOperationChainRuntime.cs");
        var coreLoopRoutingRuntimePath = Path.Combine(repoRoot, "src", "Core", "TianShu.RuntimeComposition", "AppHostCoreLoopRoutingRuntime.cs");
        var coreLoopRoutingRuntimeCompositionPath = Path.Combine(repoRoot, "src", "Core", "TianShu.RuntimeComposition", "AppHostCoreLoopRoutingRuntimeComposition.cs");
        var coreLoopRoutingSessionRuntimePath = Path.Combine(repoRoot, "src", "Core", "TianShu.RuntimeComposition", "AppHostCoreLoopRoutingSessionRuntime.cs");
        var coreLoopRoutingEntryPlannerPath = Path.Combine(repoRoot, "src", "Core", "TianShu.RuntimeComposition", "AppHostCoreLoopRoutingEntryPlanner.cs");
        var coreLoopRuntimeConfigReaderPath = Path.Combine(repoRoot, "src", "Core", "TianShu.RuntimeComposition", "AppHostCoreLoopRuntimeConfigReader.cs");
        var coreLoopStateStorePath = Path.Combine(repoRoot, "src", "Core", "TianShu.RuntimeComposition", "AppHostCoreLoopOrchestrationStateStore.cs");
        var coreLoopStateProjectorPath = Path.Combine(repoRoot, "src", "Core", "TianShu.RuntimeComposition", "AppHostCoreLoopOrchestrationStateProjector.cs");
        var coreLoopEntryIntentResolverPath = Path.Combine(repoRoot, "src", "Core", "TianShu.RuntimeComposition", "AppHostCoreLoopEntryIntentResolver.cs");
        var kernelCoreLoopEntryIntentResolverPath = Path.Combine(repoRoot, "src", "Core", "TianShu.Kernel", "CoreLoopEntryIntentResolver.cs");
        var kernelSessionOrchestrationInputFactoryPath = Path.Combine(repoRoot, "src", "Core", "TianShu.Kernel", "SessionOrchestrationInputFactory.cs");
        var kernelSessionObservedStateFactoryPath = Path.Combine(repoRoot, "src", "Core", "TianShu.Kernel", "SessionObservedStateFactory.cs");
        var kernelStageRegistryPlanningContextFactoryPath = Path.Combine(repoRoot, "src", "Core", "TianShu.Kernel", "StageRegistryPlanningContextFactory.cs");
        var kernelThreadOrchestrationInputProjectionFactoryPath = Path.Combine(repoRoot, "src", "Execution", "TianShu.Execution.Runtime", "KernelThreadOrchestrationInputProjectionFactory.cs");
        var kernelThreadObservedStateProjectionFactoryPath = Path.Combine(repoRoot, "src", "Execution", "TianShu.Execution.Runtime", "KernelThreadObservedStateProjectionFactory.cs");
        var coreLoopModelRouteResolverPath = Path.Combine(repoRoot, "src", "Core", "TianShu.RuntimeComposition", "AppHostCoreLoopModelRouteResolver.cs");
        var appHostTurnRequestContextFactoryPath = Path.Combine(repoRoot, "src", "Core", "TianShu.RuntimeComposition", "AppHostTurnRequestContextFactory.cs");
        var kernelThreadProjectionPayloadFactoryPath = Path.Combine(repoRoot, "src", "Execution", "TianShu.Execution.Runtime", "KernelThreadProjectionPayloadFactory.cs");
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnExecutionRuntimeHelpers.cs");
        var runtimeModelsPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnExecutionRuntimeModels.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var threadLifecycleSource = ReadKernelThreadLifecycleFacadeSource(repoRoot);
        var runtimeFacadeSource = File.ReadAllText(runtimePath);
        var runtimeCompositionSource = File.ReadAllText(runtimeCompositionPath);
        var runtimeSource = runtimeFacadeSource + Environment.NewLine + runtimeCompositionSource;
        var backgroundSchedulerRuntimeSource = File.ReadAllText(backgroundSchedulerRuntimePath);
        var turnInterruptRuntimeSource = File.ReadAllText(turnInterruptRuntimePath);
        var turnLaunchRuntimeSource = File.ReadAllText(turnLaunchRuntimePath);
        var turnModelStageRuntimeSource = File.ReadAllText(turnModelStageRuntimePath);
        var activeSnapshotRuntimeSource = File.ReadAllText(activeSnapshotRuntimePath);
        var turnStartStateRuntimeSource = File.ReadAllText(turnStartStateRuntimePath);
        var turnTerminalStateRuntimeSource = File.ReadAllText(turnTerminalStateRuntimePath);
        var turnFinalizationRuntimeSource = File.ReadAllText(turnFinalizationRuntimePath);
        var turnStageNotificationRuntimeSource = File.ReadAllText(turnStageNotificationRuntimePath);
        var turnInputResolutionRuntimeSource = File.ReadAllText(turnInputResolutionRuntimePath);
        var turnDependencyResolutionRuntimeSource = File.ReadAllText(turnDependencyResolutionRuntimePath);
        var turnAssistantExecutionRuntimeSource = File.ReadAllText(turnAssistantExecutionRuntimePath);
        var turnProviderAssistantRuntimeSource = File.ReadAllText(turnProviderAssistantRuntimePath);
        var turnAssistantOutputStreamingRuntimeSource = File.ReadAllText(turnAssistantOutputStreamingRuntimePath);
        var turnSteerInputRuntimeSource = File.ReadAllText(turnSteerInputRuntimePath);
        var responsesStreamNotificationRuntimeSource = File.ReadAllText(responsesStreamNotificationRuntimePath);
        var responsesStreamFailureRuntimeSource = File.ReadAllText(responsesStreamFailureRuntimePath);
        var responsesStreamProcessingRuntimeSource = File.ReadAllText(responsesStreamProcessingRuntimePath);
        var responsesAssistantCompletionRuntimeSource = File.ReadAllText(responsesAssistantCompletionRuntimePath);
        var responsesRequestCompositionRuntimeSource = File.ReadAllText(responsesRequestCompositionRuntimePath);
        var responsesToolContinuationRuntimeSource = File.ReadAllText(responsesToolContinuationRuntimePath);
        var modelFunctionToolCallRuntimeSource = File.ReadAllText(modelFunctionToolCallRuntimePath);
        var toolUseFollowUpItemProjectorSource = File.ReadAllText(toolUseFollowUpItemProjectorPath);
        var responsesFollowUpInputRuntimeSource = File.ReadAllText(responsesFollowUpInputRuntimePath);
        var responsesHttpStreamTransportRuntimeSource = File.ReadAllText(responsesHttpStreamTransportRuntimePath);
        var responsesWebSocketStreamTransportRuntimeSource = File.ReadAllText(responsesWebSocketStreamTransportRuntimePath);
        var contextSlicingDiagnosticsRuntimeSource = File.ReadAllText(contextSlicingDiagnosticsRuntimePath);
        var providerRequestDiagnosticsRuntimeSource = File.ReadAllText(providerRequestDiagnosticsRuntimePath);
        var terminalTurnProjectionCommitRuntimeSource = File.ReadAllText(terminalTurnProjectionCommitRuntimePath);
        var turnRunnerRuntimeSource = File.ReadAllText(turnRunnerRuntimePath);
        Assert.False(File.Exists(stageExecutorRunRequestFactoryPath), "AppHost.Tools.Runtime must not reintroduce a dedicated StageExecutor run request factory.");
        var turnOperationChainRuntimeSource = File.ReadAllText(turnOperationChainRuntimePath);
        var coreLoopRoutingRuntimeSource = File.ReadAllText(coreLoopRoutingRuntimePath);
        var coreLoopRoutingRuntimeCompositionSource = File.ReadAllText(coreLoopRoutingRuntimeCompositionPath);
        var coreLoopRoutingSessionRuntimeSource = File.ReadAllText(coreLoopRoutingSessionRuntimePath);
        var coreLoopRoutingEntryPlannerSource = File.ReadAllText(coreLoopRoutingEntryPlannerPath);
        var coreLoopRuntimeConfigReaderSource = File.ReadAllText(coreLoopRuntimeConfigReaderPath);
        var coreLoopStateStoreSource = File.ReadAllText(coreLoopStateStorePath);
        var coreLoopStateProjectorSource = File.ReadAllText(coreLoopStateProjectorPath);
        var coreLoopEntryIntentResolverSource = File.ReadAllText(coreLoopEntryIntentResolverPath);
        var kernelCoreLoopEntryIntentResolverSource = File.ReadAllText(kernelCoreLoopEntryIntentResolverPath);
        var kernelSessionOrchestrationInputFactorySource = File.ReadAllText(kernelSessionOrchestrationInputFactoryPath);
        var kernelSessionObservedStateFactorySource = File.ReadAllText(kernelSessionObservedStateFactoryPath);
        var kernelStageRegistryPlanningContextFactorySource = File.ReadAllText(kernelStageRegistryPlanningContextFactoryPath);
        var kernelThreadOrchestrationInputProjectionFactorySource = File.ReadAllText(kernelThreadOrchestrationInputProjectionFactoryPath);
        var kernelThreadObservedStateProjectionFactorySource = File.ReadAllText(kernelThreadObservedStateProjectionFactoryPath);
        var coreLoopModelRouteResolverSource = File.ReadAllText(coreLoopModelRouteResolverPath);
        var appHostTurnRequestContextFactorySource = File.ReadAllText(appHostTurnRequestContextFactoryPath);
        var kernelThreadProjectionPayloadFactorySource = File.ReadAllText(kernelThreadProjectionPayloadFactoryPath);
        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);
        var runtimeModelsSource = File.ReadAllText(runtimeModelsPath);

        Assert.Contains("private readonly KernelTurnExecutionAppHostRuntime turnExecutionAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("this.turnExecutionAppHostRuntime = new KernelTurnExecutionAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("turnExecutionAppHostRuntime!.RunTurnAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("turnExecutionAppHostRuntime.TryCommitTerminalTurnProjectionAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("private readonly AppHostCoreLoopRoutingRuntime coreLoopRoutingAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("this.coreLoopRoutingAppHostRuntime = new AppHostCoreLoopRoutingRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("coreLoopRoutingAppHostRuntime.ApplyDefaultOrchestrationAndModelRoute(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("coreLoopRoutingAppHostRuntime.ApplyDefaultOrchestrationAndModelRouteAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("coreLoopRoutingAppHostRuntime.ApplyReviewOrchestrationAndModelRouteAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task TryAppendStageCheckpointAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<TurnRequestContext> ApplyOrchestrationAndModelRouteAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private TurnRequestContext ApplyOrchestrationAndModelRoute(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private TurnRequestContext ApplyDefaultOrchestrationAndModelRoute(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveRequestedStageId(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuiltInStageDefinitions.Review", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuiltInStageDefinitions.Planning", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private SessionCoreLoopRoutingEntry PlanSessionCoreLoopEntry(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private TurnRequestContext ApplyCoreLoopEntryPlan(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new SessionCoreLoopEntryPlanner(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorDispatcher.FromStageDefinitions(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultModelRouter.Instance.Resolve(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorCheckpointBuilder.Instance.Complete(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new StageExecutorRuntimeContext(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new StageExecutorTurnCompletion(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveResponsesTransportSettings,", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CanUseResponsesWebSocketTransport,", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PrewarmResponsesWebSocketSessionAsync,", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StreamResponsesWebSocketRequestWithFallbackAsync,", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateTransportResponseHeaders,", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessResponsesEventStreamAsync,", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record TurnRequestContext(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed class TurnOperationState", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal enum TurnOperationKind", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record ModelFunctionCall(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record ResponsesStreamResult(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed class KernelResponsesStreamException", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string AppendUserInputText(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task WriteCommittedUserMessageItemAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private string NextUserMessageItemId(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<TurnOperationState> ExecuteTurnOperationChainAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<TurnRequestContext> ExecuteTurnOperationAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new TurnRequestContext(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("AppHostTurnRequestContextFactory.CreateFromTransportParams(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("AppHostTurnRequestContextFactory.CreateFromTurnStartRequest(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("AppHostTurnRequestContextFactory.CreateBase(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("KernelThreadProjectionPayloadFactory.ToSessionProjectionPayload(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AppHostThreadProjectionPayloadFactory.ToSessionProjectionPayload(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelThreadSessionProjectionPayload? ToThreadSessionProjectionPayload(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelThreadOrchestrationProjectionPayload? ToThreadOrchestrationProjectionPayload(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ToTurnOperationName(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? TryExtractRequestUserInputText(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<(string assistantText, bool streamed)> ExecuteAssistantFromProviderAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<string> StreamResponsesToolLoopAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<ResponsesStreamResult> StreamResponsesRequestWithRetryAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<ResponsesStreamResult> StreamResponsesRequestAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private Task PersistActiveTurnSnapshotAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<string> MergeSteerInputsAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool TryMergeSteerInputsImmediately(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<List<object>> AppendSteerInputsToResponsesFollowUpAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("await Task.Delay(120, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("threadStore.UpsertActiveTurnAsync(threadId, activeTurn, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("var operations = new[]", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("turn context missing skill injection collection", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void AppendInstructionSection(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryBuildTurnConversationHistoryItem(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static IReadOnlyList<KernelConversationHistoryItem> NormalizeTurnConversationHistoryOrder(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.False(File.Exists(kernelToolRuntimePath));
        Assert.Contains("turnExecutionAppHostRuntime.StartBackgroundTurnAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.False(File.Exists(threadLifecyclePath));
        Assert.Contains("await turnExecutionAppHostRuntime.HandleTurnStartAsync(id, request, cancellationToken).ConfigureAwait(false);", threadLifecycleSource, StringComparison.Ordinal);
        Assert.Contains("await turnExecutionAppHostRuntime.HandleTurnInterruptAsync(id, threadId!, turnId!, cancellationToken).ConfigureAwait(false);", threadLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("var runtimeThread = threadManager.GetOrAttachThread(record, BuildDefaultThreadSession, loaded: true);", threadLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("threadHistoryAppHostRuntime.RegisterPendingTurnInterrupt(normalizedThreadId, normalizedTurnId);", threadLifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("var task = Task.Run(() => RunTurnAsync(", threadLifecycleSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnExecutionAppHostRuntime", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnExecutionRuntimeComposition composition;", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.Contains("composition = new KernelTurnExecutionRuntimeComposition(", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleTurnStartAsync(", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleTurnInterruptAsync(", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.Contains("composition.TurnLaunchRuntime.HandleTurnStartAsync(", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.Contains("composition.TurnInterruptRuntime.HandleTurnInterruptAsync(", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.Contains("composition.TurnRunnerRuntime", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.Contains("composition.TerminalTurnProjectionCommitRuntime.TryCommitTerminalTurnProjectionAsync(", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("composition.StageExecutorRunnerRuntime", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("composition.StageCheckpointCommitRuntime", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly KernelTurnLaunchRuntime turnLaunchRuntime;", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly KernelTurnInterruptRuntime turnInterruptRuntime;", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly KernelStageExecutorRunnerRuntime stageExecutorRunnerRuntime;", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly KernelStageCheckpointCommitRuntime stageCheckpointCommitRuntime;", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new KernelTurnLaunchRuntime(", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new KernelTurnInterruptRuntime(", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new KernelTurnModelStageRuntime(", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnExecutionRuntimeComposition", runtimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("public KernelTurnLaunchRuntime TurnLaunchRuntime", runtimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("public KernelTurnInterruptRuntime TurnInterruptRuntime", runtimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("public KernelTurnRunnerRuntime TurnRunnerRuntime", runtimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("public KernelTerminalTurnProjectionCommitRuntime TerminalTurnProjectionCommitRuntime", runtimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("new KernelTurnLaunchRuntime(", runtimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("new KernelTurnInterruptRuntime(", runtimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("new KernelTurnModelStageRuntime(", runtimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnInterruptRuntime turnInterruptRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("turnInterruptRuntime = new KernelTurnInterruptRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("threadStore.GetThreadAsync(threadId", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("registerPendingTurnInterrupt(normalizedThreadId", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("backgroundTurnSchedulerRuntime.TryCancel(normalizedTurnId)", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("registerPendingTurnInterruptResponse(normalizedThreadId", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("clearPendingTurnInterrupt(normalizedThreadId", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("clearPendingTurnInterruptResponses(normalizedThreadId", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnInterruptRuntime", turnInterruptRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleTurnInterruptAsync(", turnInterruptRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("threadStore.GetThreadAsync(threadId", turnInterruptRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("registerPendingTurnInterrupt(normalizedThreadId", turnInterruptRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("backgroundTurnSchedulerRuntime.TryCancel(normalizedTurnId)", turnInterruptRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("registerPendingTurnInterruptResponse(normalizedThreadId", turnInterruptRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("clearPendingTurnInterrupt(normalizedThreadId", turnInterruptRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("clearPendingTurnInterruptResponses(normalizedThreadId", turnInterruptRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<string> StartBackgroundTurnAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task RunTurnAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnLaunchRuntime turnLaunchRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("turnLaunchRuntime = new KernelTurnLaunchRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains(".StartBackgroundTurnAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private Task RunTurnAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task RunTurnAsync(", runtimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnBackgroundSchedulerRuntime backgroundTurnSchedulerRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("backgroundTurnSchedulerRuntime.Register(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("backgroundTurnSchedulerRuntime.Schedule(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("backgroundTurnSchedulerRuntime.TryCancel(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("backgroundTurnSchedulerRuntime.Complete(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("threadManager.GetOrAttachThread(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("applyTurnOverrides(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("buildTurnRequestContext(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimeThread.SetActiveTurn(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("seedTrackedTurnUserMessage(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("turnStartStateRuntime.PersistAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("turnStartStateRuntime.PublishStartedAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnLaunchRuntime", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleTurnStartAsync(", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<string> StartBackgroundTurnAsync(", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("threadManager.GetOrAttachThread(", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("applyTurnOverrides(", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("buildTurnRequestContext(", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("runtimeThread.SetActiveTurn(", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("seedTrackedTurnUserMessage(", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("var activeRecord = await turnStartStateRuntime", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.Contains(".PersistAsync(", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("await turnStartStateRuntime", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.Contains(".PublishStartedAsync(", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("backgroundTurnSchedulerRuntime.Register(", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("backgroundTurnSchedulerRuntime.Schedule(", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly ConcurrentDictionary<string, CancellationTokenSource> runningTurns;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly ConcurrentDictionary<string, Task> runningTurnTasks;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CancellationTokenSource.CreateLinkedTokenSource(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("runningTurns[", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("runningTurns.Try", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("runningTurnTasks[", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("runningTurnTasks.Try", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnBackgroundSchedulerRuntime", backgroundSchedulerRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly ConcurrentDictionary<string, CancellationTokenSource> runningTurns;", backgroundSchedulerRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly ConcurrentDictionary<string, Task> runningTurnTasks;", backgroundSchedulerRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource(", backgroundSchedulerRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("Task.Run(", backgroundSchedulerRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("runningTurns[turnId] = cts;", backgroundSchedulerRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("runningTurns.TryGetValue(", backgroundSchedulerRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("runningTurns.TryRemove(", backgroundSchedulerRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("runningTurnTasks[turnId] =", backgroundSchedulerRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("runningTurnTasks.TryRemove(", backgroundSchedulerRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task TryCommitTerminalTurnProjectionAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTerminalTurnProjectionCommitRuntime terminalTurnProjectionCommitRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("composition.TerminalTurnProjectionCommitRuntime.TryCommitTerminalTurnProjectionAsync(", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TryAppendStageCheckpointAsync(", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorCheckpointBuilder.Instance.Complete(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new StageExecutorTurnCompletion(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTerminalTurnProjectionCommitRuntime", terminalTurnProjectionCommitRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task TryCommitTerminalTurnProjectionAsync(", terminalTurnProjectionCommitRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("TurnTerminalProjectionCheckpointBuilder.Instance.CompleteTerminalTurn(", terminalTurnProjectionCommitRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorCheckpointBuilder", terminalTurnProjectionCommitRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new StageExecutorTurnCompletion(", terminalTurnProjectionCommitRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("threadStore.AppendTerminalTurnProjectionAsync(", terminalTurnProjectionCommitRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("threadStore.AppendStageCheckpointAsync(", terminalTurnProjectionCommitRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnRunnerRuntime turnRunnerRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnModelStageRuntime modelStageRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("modelStageRuntime = new KernelTurnModelStageRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("new KernelTurnRunnerRuntime(modelStageRuntime.ExecuteAsync)", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("composition.TurnRunnerRuntime", runtimeFacadeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly StageExecutorRunner<TurnRequestContext> stageExecutorRunner;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly KernelStageExecutorRunRequestFactory stageExecutorRunRequestFactory;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new StageExecutorImplementation<TurnRequestContext>(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorDispatcher.DefaultModelTurnImplementationId", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("stageExecutorRunRequestFactory.Create(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new StageExecutorRunRequest<TurnRequestContext>(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static StageExecutorRuntimeContext CreateStageExecutorRuntimeContext(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("stageExecutorRunner.RunAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new StageExecutorRuntimeContext(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("?? $\"execution-", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private Task ExecuteModelTurnStageAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task RunModelTurnStageAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new KernelTurnTerminalStateCommit(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("maybeExtractMemoryFromCompletedTurnAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("finalizationRuntime.FinalizeAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("activeSnapshotRuntime.PersistAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnModelStageRuntime", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public Task ExecuteAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task ExecuteAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("new TurnOperationState(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("operationChainRuntime.ExecuteAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("activeSnapshotRuntime.PersistAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("terminalStateRuntime.PersistAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("maybeExtractMemoryFromCompletedTurnAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("terminalStateRuntime.PublishAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("finalizationRuntime.FinalizeAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnRunnerRuntime", turnRunnerRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly TurnExecutionRunner<TurnRequestContext> turnExecutionRunner;", turnRunnerRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly KernelStageExecutorRunRequestFactory stageExecutorRunRequestFactory;", turnRunnerRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("TurnExecutionRunnerFactory.CreateDefaultModelTurnRunner(runModelTurnAsync)", turnRunnerRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorRunnerFactory", turnRunnerRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new StageExecutorImplementation<TurnRequestContext>(", turnRunnerRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorDispatcher.DefaultModelTurnImplementationId", turnRunnerRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("TurnExecutionRunRequestFactory.Create(", turnRunnerRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorRunRequestFactory", turnRunnerRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("await turnExecutionRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);", turnRunnerRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new StageExecutorRunRequest<TurnRequestContext>(", turnRunnerRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutor", turnRunnerRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("turnContext.ExecutionDispatchContext", turnRunnerRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorRunRequest", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("TurnExecutionRunRequest<TurnRequestContext>", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelStageCheckpointCommitRuntime.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelStageExecutorRunnerRuntime.cs")));
        Assert.Contains("private readonly KernelTurnOperationChainRuntime operationChainRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("operationChainRuntime = new KernelTurnOperationChainRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("operationChainRuntime.ExecuteAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("operationChainRuntime.ExecuteAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<TurnOperationState> ExecuteTurnOperationChainAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<TurnRequestContext> ExecuteTurnOperationAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ToTurnOperationName(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("var operations = new[]", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("const int maxPasses = 3;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnOperationChainRuntime", turnOperationChainRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<TurnOperationState> ExecuteAsync(", turnOperationChainRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<TurnRequestContext> ExecuteOperationAsync(", turnOperationChainRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("TurnOperationKind.ResolveInput", turnOperationChainRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("TurnOperationKind.ResolveDependencies", turnOperationChainRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("TurnOperationKind.ExecuteAssistant", turnOperationChainRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("TurnOperationKind.StreamAssistantOutput", turnOperationChainRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("const int maxPasses = 3;", turnOperationChainRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("steerInputRuntime.TryMergeSteerInputsImmediately(", turnOperationChainRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("PublishLateSteerAcceptedAsync(", turnOperationChainRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<(string assistantText, bool streamed)> ExecuteAssistantFromProviderAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<string> StreamResponsesToolLoopAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnProviderAssistantRuntime providerAssistantRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("providerAssistantRuntime = new KernelTurnProviderAssistantRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("providerAssistantRuntime.ExecuteAsync", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnProviderAssistantRuntime", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<(string AssistantText, bool Streamed)> ExecuteAsync(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<string> StreamResponsesToolLoopAsync(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderWireApi.NormalizeOrThrow(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("Environment.GetEnvironmentVariable(apiKeyEnv)", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("缺少模型访问凭据：环境变量", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("var requestSequence = 0;", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelTurnExecutionRuntimeHelpers.BuildSlicedResponsesConversationInput(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelTurnExecutionRuntimeHelpers.RefreshLoopTurnContext(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("completionDecision.Kind == KernelResponsesAssistantCompletionDecisionKind.Repair", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesFollowUpInputRuntime.BuildAsync(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderWireApi.NormalizeOrThrow(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.GetEnvironmentVariable(apiKeyEnv)", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("缺少模型访问凭据：环境变量", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("var requestSequence = 0;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelTurnExecutionRuntimeHelpers.BuildSlicedResponsesConversationInput(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelTurnExecutionRuntimeHelpers.RefreshLoopTurnContext(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("completionDecision.Kind == KernelResponsesAssistantCompletionDecisionKind.Repair", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<ResponsesStreamResult> StreamResponsesRequestWithRetryAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<ResponsesStreamResult> StreamResponsesRequestAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnActiveSnapshotRuntime activeSnapshotRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public Task PersistActiveTurnSnapshotAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("=> activeSnapshotRuntime.PersistAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("activeSnapshotRuntime.PersistAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Func<string?, string?, KernelTurnRecord?> buildTrackedActiveTurnSnapshot;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Func<string, CancellationToken, Task<bool>> isEphemeralThreadAsync;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("var activeTurn = buildTrackedActiveTurnSnapshot(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("threadStore.UpsertActiveTurnAsync(threadId, activeTurn, cancellationToken)", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("threadStore.RolloutRecorder.AppendTurnResultAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnActiveSnapshotRuntime", activeSnapshotRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("var activeTurn = buildTrackedActiveTurnSnapshot(threadId, turnId);", activeSnapshotRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("threadStore.UpsertActiveTurnAsync(threadId, activeTurn, cancellationToken)", activeSnapshotRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("runtimeThread.Update(updatedRecord, loaded: true);", activeSnapshotRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("isEphemeralThreadAsync(threadId, cancellationToken)", activeSnapshotRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("threadStore.RolloutRecorder.AppendTurnResultAsync(", activeSnapshotRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnStartStateRuntime turnStartStateRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("turnStartStateRuntime = new KernelTurnStartStateRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("turnStartStateRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("turnStartStateRuntime.PersistAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("turnStartStateRuntime.PublishStartedAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("var activeRecord = await turnStartStateRuntime", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.Contains(".PersistAsync(", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("await turnStartStateRuntime", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.Contains(".PublishStartedAsync(", turnLaunchRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UpsertActiveTurnAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"turn.started\"", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureSessionMetaAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnStartStateRuntime", turnStartStateRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("UpsertActiveTurnAsync(", turnStartStateRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"turn.started\"", turnStartStateRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("writeThreadStatusChangedAsync(activeRecord, cancellationToken)", turnStartStateRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("threadStore.RolloutRecorder", turnStartStateRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("EnsureSessionMetaAsync(", turnStartStateRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnTerminalStateRuntime terminalStateRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("terminalStateRuntime = new KernelTurnTerminalStateRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("terminalStateRuntime.PersistAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("terminalStateRuntime.PublishAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("terminalStateRuntime.PersistAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("terminalStateRuntime.PublishAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("terminalStateRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".AppendCompletedTurnAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".SetThreadStatusAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"turn.completed\"", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"turn/completed\"", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RolloutRecorder.GetRolloutPath(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("await persistTurnSessionBeforeTerminalAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnTerminalStateRuntime", turnTerminalStateRuntimeSource, StringComparison.Ordinal);
        Assert.Contains(".AppendCompletedTurnAsync(", turnTerminalStateRuntimeSource, StringComparison.Ordinal);
        Assert.Contains(".SetThreadStatusAsync(", turnTerminalStateRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"turn.completed\"", turnTerminalStateRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"turn/completed\"", turnTerminalStateRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("RolloutRecorder.GetRolloutPath(", turnTerminalStateRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("PersistTurnSessionBeforeTerminalAsync(", turnTerminalStateRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnFinalizationRuntime finalizationRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("finalizationRuntime = new KernelTurnFinalizationRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("finalizationRuntime.FinalizeAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("finalizationRuntime.FinalizeAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("captureThreadGitDiffAsync(threadId", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"turn/diff/updated\"", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("backgroundTurnSchedulerRuntime.Complete(turnId)", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("deactivateCodeModeTurn(turnId)", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("disposeJsReplManagerAsync(turnId)", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("grantedPermissionTurnByTurn.TryRemove(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("steerInputsByTurn.TryRemove(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearActiveTurn(turnId)", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnFinalizationRuntime", turnFinalizationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("captureThreadGitDiffAsync(threadId", turnFinalizationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"turn/diff/updated\"", turnFinalizationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("backgroundTurnSchedulerRuntime.Complete(turnId)", turnFinalizationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("deactivateCodeModeTurn(turnId)", turnFinalizationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("disposeJsReplManagerAsync(turnId)", turnFinalizationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("grantedPermissionTurnByTurn.TryRemove(", turnFinalizationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("steerInputsByTurn.TryRemove(", turnFinalizationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ClearActiveTurn(turnId)", turnFinalizationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnStageNotificationRuntime stageNotificationRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("stageNotificationRuntime = new KernelTurnStageNotificationRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("stageNotificationRuntime.PublishTurnStartedAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("stageNotificationRuntime.PublishCompletedAgentMessageAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishTokenUsageUpdatedAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("stageNotificationRuntime.CompletePlanItemAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("stageNotificationRuntime.PublishTurnStartedAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("stageNotificationRuntime.PublishCompletedAgentMessageAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("PublishTokenUsageUpdatedAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("stageNotificationRuntime.CompletePlanItemAsync(", turnModelStageRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"enteredReviewMode\"", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"rawResponseItem/completed\"", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"thread/tokenUsage/updated\"", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("willRetry = false", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnStageNotificationRuntime", turnStageNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"enteredReviewMode\"", turnStageNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"rawResponseItem/completed\"", turnStageNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"thread/tokenUsage/updated\"", turnStageNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static object BuildTokenUsagePayload(", turnStageNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildTokenUsagePayload,", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object BuildTokenUsagePayload(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<string, string, object> buildTokenUsagePayload", runtimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("willRetry = false", turnStageNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelContextSlicingDiagnosticsRuntime contextSlicingDiagnosticsRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("contextSlicingDiagnosticsRuntime = new KernelContextSlicingDiagnosticsRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("contextSlicingDiagnosticsRuntime.EmitReportAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("contextSlicingDiagnosticsRuntime.EmitReportAsync(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task EmitContextSlicingReportAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ContextSlicingRuntimeHelpers.DiagnosticsNotificationMethod", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelContextSlicingDiagnosticsRuntime", contextSlicingDiagnosticsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ContextSlicingRuntimeHelpers.WithRuntimeIdentity(", contextSlicingDiagnosticsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ContextSlicingRuntimeHelpers.BuildDiagnosticPayload(", contextSlicingDiagnosticsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ContextSlicingRuntimeHelpers.DiagnosticsNotificationMethod", contextSlicingDiagnosticsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("diagnosticOperationScopeFactory.BeginOperation(", contextSlicingDiagnosticsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("diagnosticEventSink.EmitAsync(", contextSlicingDiagnosticsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelProviderRequestDiagnosticsRuntime providerRequestDiagnosticsRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("providerRequestDiagnosticsRuntime = new KernelProviderRequestDiagnosticsRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("providerRequestDiagnosticsRuntime.CaptureAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderRequestDiagnosticsCapture.", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelProviderRequestDiagnosticsRuntime", providerRequestDiagnosticsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestDiagnosticsCapture.BuildOperationStart(", providerRequestDiagnosticsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestDiagnosticsCapture.WritePayloadArtifactAsync(", providerRequestDiagnosticsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestDiagnosticsCapture.BuildContextStats(", providerRequestDiagnosticsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestDiagnosticsCapture.EmitContextStatsAsync(", providerRequestDiagnosticsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnSteerInputRuntime steerInputRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("steerInputRuntime = new KernelTurnSteerInputRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("steerInputRuntime.MergeSteerInputsAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("steerInputRuntime.TryMergeSteerInputsImmediately(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("steerInputRuntime.TryMergeSteerInputsImmediately(", turnOperationChainRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("steerInputRuntime.DrainSteerInputs(state.TurnId)", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("steerInputRuntime.AppendSteerInputsToResponsesFollowUpAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"item/tool/requestUserInput\"", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TryExtractRequestUserInputText(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<string> MergeSteerInputsAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public bool TryMergeSteerInputsImmediately(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<List<object>> AppendSteerInputsToResponsesFollowUpAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task WriteCommittedUserMessageItemAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public string NextUserMessageItemId(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string AppendUserInputText(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnSteerInputRuntime", turnSteerInputRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<string> MergeSteerInputsAsync(", turnSteerInputRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public bool TryMergeSteerInputsImmediately(", turnSteerInputRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<List<object>> AppendSteerInputsToResponsesFollowUpAsync(", turnSteerInputRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task WriteCommittedUserMessageItemAsync(", turnSteerInputRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public string NextUserMessageItemId(", turnSteerInputRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static string AppendUserInputText(", turnSteerInputRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"turn/steered\"", turnSteerInputRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"userMessage\"", turnSteerInputRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnInputResolutionRuntime inputResolutionRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("inputResolutionRuntime = new KernelTurnInputResolutionRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("inputResolutionRuntime.ResolveAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("inputResolutionRuntime.ResolveAsync(", turnOperationChainRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnInputResolutionRuntime", turnInputResolutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task ResolveAsync(", turnInputResolutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("steerInputRuntime.MergeSteerInputsAsync(", turnInputResolutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"item/tool/requestUserInput\"", turnInputResolutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMinutes(2)", turnInputResolutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private string? TryExtractRequestUserInputText(", turnInputResolutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("用户输入请求已返回，但未提供 answers。", turnInputResolutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnAssistantExecutionRuntime assistantExecutionRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("assistantExecutionRuntime = new KernelTurnAssistantExecutionRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("assistantExecutionRuntime.ExecuteAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("assistantExecutionRuntime.ExecuteAsync(", turnOperationChainRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("providerAssistantRuntime.ExecuteAsync", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Func<string, string, KernelReadinessFlag, string, JsonElement, TurnRequestContext, CancellationToken, Task<string>> executeInlineToolCallAsync;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Func<string?, KernelProposedPlanExtraction> extractProposedPlanText;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelToolRuntimeParsingHelpers.TryParseInlineToolCall(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("state.AssistantText = await ExecuteInlineToolCallAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("var extractedPlan = extractProposedPlanText(state.AssistantText);", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private Task<string> ExecuteInlineToolCallAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<string> ExecuteInlineToolCallAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnAssistantExecutionRuntime", turnAssistantExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task ExecuteAsync(", turnAssistantExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeParsingHelpers.TryParseInlineToolCall(", turnAssistantExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("executeInlineToolCallAsync(", turnAssistantExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("executeAssistantFromProviderAsync(", turnAssistantExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("extractProposedPlanText(state.AssistantText)", turnAssistantExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("state.AssistantTextStreamed = false;", turnAssistantExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("state.PlanText = extractedPlan.PlanText;", turnAssistantExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelResponsesFollowUpInputRuntime responsesFollowUpInputRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesFollowUpInputRuntime = new KernelResponsesFollowUpInputRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesFollowUpInputRuntime.BuildAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesFollowUpInputRuntime.BuildAsync(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("var compactedFollowUpInput = await maybeBuildMidTurnAutoCompactedFollowUpInputAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelTurnExecutionRuntimeHelpers.BuildSlicedResponsesFollowUpInput(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelResponsesFollowUpInputRuntime", responsesFollowUpInputRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelResponsesFollowUpInputResult(", responsesFollowUpInputRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelResponsesFollowUpInputResult> BuildAsync(", responsesFollowUpInputRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("maybeBuildMidTurnAutoCompactedFollowUpInputAsync(", responsesFollowUpInputRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelTurnExecutionRuntimeHelpers.BuildSlicedResponsesFollowUpInput(", responsesFollowUpInputRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("steerInputRuntime.DrainSteerInputs(state.TurnId)", responsesFollowUpInputRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("steerInputRuntime.AppendSteerInputsToResponsesFollowUpAsync(", responsesFollowUpInputRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelResponsesStreamNotificationRuntime responsesStreamNotificationRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesStreamNotificationRuntime = new KernelResponsesStreamNotificationRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnAssistantOutputStreamingRuntime assistantOutputStreamingRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("assistantOutputStreamingRuntime = new KernelTurnAssistantOutputStreamingRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("assistantOutputStreamingRuntime.StreamAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("assistantOutputStreamingRuntime.StreamAsync(", turnOperationChainRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesStreamNotificationRuntime.EmitPlanDeltaAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesStreamNotificationRuntime.EmitAssistantDeltaAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesStreamNotificationRuntime.EmitProviderReasoningDeltaAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesStreamNotificationRuntime.EmitPresentableOutputItemNotificationAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesStreamNotificationRuntime.EmitReasoningSummaryPartAddedAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesStreamNotificationRuntime.EmitReasoningSummaryTextDeltaAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesStreamNotificationRuntime.EmitReasoningTextDeltaAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static IEnumerable<string> SplitIntoChunks(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay(35, cancellationToken)", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnAssistantOutputStreamingRuntime", turnAssistantOutputStreamingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task StreamAsync(", turnAssistantOutputStreamingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("state.AssistantTextStreamed", turnAssistantOutputStreamingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static IEnumerable<string> SplitIntoChunks(", turnAssistantOutputStreamingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(35, cancellationToken)", turnAssistantOutputStreamingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesStreamNotificationRuntime.EmitPlanDeltaAsync(", turnAssistantOutputStreamingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesStreamNotificationRuntime.EmitAssistantDeltaAsync(", turnAssistantOutputStreamingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesStreamNotificationRuntime.EmitReasoningSummaryPartAddedAsync(", turnAssistantOutputStreamingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesStreamNotificationRuntime.EmitReasoningSummaryTextDeltaAsync(", turnAssistantOutputStreamingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesStreamNotificationRuntime.EmitReasoningTextDeltaAsync(", turnAssistantOutputStreamingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? NormalizeVisibleAssistantDelta(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task EmitPlanDeltaAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task EmitAssistantDeltaAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task EmitProviderReasoningDeltaAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task EmitPresentableOutputItemNotificationAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"item/agentMessage/delta\"", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"item/plan/delta\"", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"item/reasoning/textDelta\"", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"item/reasoning/summaryPartAdded\"", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"item/reasoning/summaryTextDelta\"", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelResponsesStreamNotificationRuntime", responsesStreamNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task EmitPlanDeltaAsync(", responsesStreamNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<string?> EmitAssistantDeltaAsync(", responsesStreamNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task EmitProviderReasoningDeltaAsync(", responsesStreamNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task EmitPresentableOutputItemNotificationAsync(", responsesStreamNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public Task EmitReasoningSummaryPartAddedAsync(", responsesStreamNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public Task EmitReasoningSummaryTextDeltaAsync(", responsesStreamNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public Task EmitReasoningTextDeltaAsync(", responsesStreamNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"item/agentMessage/delta\"", responsesStreamNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"item/plan/delta\"", responsesStreamNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"item/reasoning/textDelta\"", responsesStreamNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"item/reasoning/summaryPartAdded\"", responsesStreamNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"item/reasoning/summaryTextDelta\"", responsesStreamNotificationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelResponsesStreamFailureRuntime responsesStreamFailureRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesStreamFailureRuntime = new KernelResponsesStreamFailureRuntime();", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesStreamFailureRuntime.ClassifyHttpStreamFailure(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesStreamFailureRuntime.ClassifyWebSocketFailure(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesStreamFailureRuntime.CreateStreamException(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static ProviderResponsesTransportFailure ClassifyResponsesHttpStreamFailure(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static ProviderResponsesTransportFailure ClassifyResponsesWebSocketFailure(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private Exception CreateResponsesStreamException(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildResponsesStreamFailureMessage(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IsRetryableResponsesStreamFailure(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetResponsesStreamFailureError(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TryBuildResponsesStreamFailureMessageFromObject(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed class KernelResponsesWebSocketUpgradeRequiredException : Exception", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static ProviderResponsesTransportFailure ClassifyResponsesHttpStreamFailure(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelResponsesStreamFailureRuntime", responsesStreamFailureRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public ProviderResponsesTransportFailure ClassifyHttpStreamFailure(", responsesStreamFailureRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public ProviderResponsesTransportFailure ClassifyWebSocketFailure(", responsesStreamFailureRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public Exception CreateStreamException(", responsesStreamFailureRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static string BuildStreamFailureMessage(", responsesStreamFailureRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static bool IsRetryableStreamFailure(", responsesStreamFailureRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static bool TryGetStreamFailureError(", responsesStreamFailureRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static bool TryBuildStreamFailureMessageFromObject(", responsesStreamFailureRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelResponsesWebSocketUpgradeRequiredException : Exception", responsesStreamFailureRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelResponsesStreamProcessingRuntime responsesStreamProcessingRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesStreamProcessingRuntime = new KernelResponsesStreamProcessingRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesStreamProcessingRuntime.ProcessAsync", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<ResponsesStreamResult> ProcessResponsesEventStreamAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryReadChatCompletionsDelta(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static JsonDocument ParseResponsesStreamEvent(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed class ChatCompletionsToolCallAccumulator", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderResponsesToolUseBlockAccumulator", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelResponsesStreamProcessingRuntime", responsesStreamProcessingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<ResponsesStreamResult> ProcessAsync(", responsesStreamProcessingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static bool TryReadChatCompletionsDelta(", responsesStreamProcessingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static JsonDocument ParseEvent(", responsesStreamProcessingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private sealed class ChatCompletionsToolCallAccumulator", responsesStreamProcessingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderResponsesToolUseBlockAccumulator", responsesStreamProcessingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderResponsesThinkingBlockAccumulator", responsesStreamProcessingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesStreamFailureRuntime.CreateStreamException(", responsesStreamProcessingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelResponsesAssistantCompletionRuntime responsesAssistantCompletionRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesAssistantCompletionRuntime = new KernelResponsesAssistantCompletionRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesAssistantCompletionRuntime.EvaluateNoToolCallAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesAssistantCompletionRuntime.EvaluateNoToolCallAsync(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private string? ExtractAssistantTextFromOutputItems(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? ExtractAssistantTextFromOutputItems(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool ContainsImageGenerationCall(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private List<object> BuildEmptyAssistantRepairInput(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"turn.responses.empty_assistant_repair\"", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("上一轮模型响应已经成功结束", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelResponsesAssistantCompletionRuntime", responsesAssistantCompletionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal enum KernelResponsesAssistantCompletionDecisionKind", responsesAssistantCompletionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelResponsesAssistantCompletionDecision(", responsesAssistantCompletionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelResponsesAssistantCompletionDecision> EvaluateNoToolCallAsync(", responsesAssistantCompletionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private string? ExtractAssistantTextFromOutputItems(", responsesAssistantCompletionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private bool ContainsImageGenerationCall(", responsesAssistantCompletionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private List<object> BuildEmptyAssistantRepairInput(", responsesAssistantCompletionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("extractProposedPlanText(assistant)", responsesAssistantCompletionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"turn.responses.empty_assistant_repair\"", responsesAssistantCompletionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("上一轮模型响应已经成功结束", responsesAssistantCompletionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"image_generation_call\"", responsesAssistantCompletionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelResponsesRequestCompositionRuntime responsesRequestCompositionRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesRequestCompositionRuntime = new KernelResponsesRequestCompositionRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesRequestCompositionRuntime.ComposeAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesRequestCompositionRuntime.PersistRequestLogAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesRequestCompositionRuntime.ComposeAsync(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesRequestCompositionRuntime.PersistRequestLogAsync(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderResponsesTransportProtocolBindings.Resolve(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderResponsesTransportRetryStrategies.Resolve(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderResponsesRequestComposers.Resolve(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProviderResponsesRequestComposerContext(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("toolRegistry.BuildProviderResponsesToolList(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderModelCatalogs.SupportsParallelToolCalls(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private ResponsesTransportSettings ResolveResponsesTransportSettings(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static IReadOnlyList<JsonElement> SerializeToJsonElements(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private string ResolveTurnInstructions(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<IReadOnlyList<object>, IReadOnlyList<string>> describeResponsesToolNames", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DescribeResponsesToolNames,", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static IReadOnlyList<string> DescribeResponsesToolNames(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? ReadResponsesToolName(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveResponsesReasoningEffort", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveResponsesReasoningSummary", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveResponsesTextVerbosity", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildResponsesTurnMetadataHeader", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveResponsesParallelToolCalls", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("resolveResponsesReasoningEffort", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("resolveResponsesReasoningSummary", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("resolveResponsesTextVerbosity", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("buildResponsesTurnMetadataHeader", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelResponsesRequestCompositionRuntime", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelResponsesProviderRequest(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelResponsesProviderRequest> ComposeAsync(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public Task PersistRequestLogAsync(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderResponsesTransportProtocolBindings.Resolve(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderResponsesTransportRetryStrategies.Resolve(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderResponsesRequestComposers.Resolve(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("new ProviderResponsesRequestComposerContext(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelTurnExecutionRuntimeHelpers.ResolveTurnInstructions(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("toolRegistry.BuildProviderResponsesToolList(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderModelCatalogs.SupportsParallelToolCalls(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private ResponsesTransportSettings ResolveTransportSettings(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private string? BuildTurnMetadataHeader(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static string? ResolveReasoningEffort(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static string? ResolveReasoningSummary(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static string? ResolveTextVerbosity(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderModelCatalogs.GetDefaultReasoningEffort(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderModelCatalogs.GetDefaultReasoningSummary(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderModelCatalogs.GetDefaultVerbosity(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static IReadOnlyList<JsonElement> SerializeToJsonElements(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static IReadOnlyList<string> DescribeResponsesToolNames(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static string? ReadResponsesToolName(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"turn.responses.request\"", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("request.NativeToolOptions.WebSearchMode", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelResponsesToolContinuationRuntime responsesToolContinuationRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesToolContinuationRuntime = new KernelResponsesToolContinuationRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesToolContinuationRuntime.BuildFollowUpResponseItems(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesToolContinuationRuntime.ExtractFunctionCalls(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesToolContinuationRuntime.ExecuteFunctionCallsAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesToolContinuationRuntime.BuildFollowUpResponseItems(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesToolContinuationRuntime.ExtractFunctionCalls(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesToolContinuationRuntime.ExecuteFunctionCallsAsync(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private List<ModelFunctionCall> ExtractFunctionCalls(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static List<ModelFunctionCall> ExtractFunctionCalls(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static List<object> BuildResponsesFollowUpResponseItems(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool ContainsImageGenerationCall(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static IReadOnlyList<object> BuildResponsesFollowUpResponseItems(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static int? ReadInt(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private Task<object> ExecuteModelFunctionCallWithParallelLockAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<object> ExecuteModelFunctionCallAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<object> ExecuteModelFunctionCallWithParallelLockAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("toolRegistry.ToolSupportsParallelToolCalls(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelResponsesToolContinuationRuntime", responsesToolContinuationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyList<object> BuildFollowUpResponseItems(", responsesToolContinuationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public List<ModelFunctionCall> ExtractFunctionCalls(", responsesToolContinuationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<List<object>> ExecuteFunctionCallsAsync(", responsesToolContinuationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"function_call\"", responsesToolContinuationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"custom_tool_call\"", responsesToolContinuationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("\"tool_search_call\"", responsesToolContinuationRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"image_generation_call\"", responsesToolContinuationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolDiscoveryToolNames.Search", responsesToolContinuationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("toolRegistry.ToolSupportsParallelToolCalls(", responsesToolContinuationRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelModelFunctionToolCallRuntime modelFunctionToolCallRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("this.modelFunctionToolCallRuntime = new KernelModelFunctionToolCallRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("modelFunctionToolCallRuntime!.ExecuteWithParallelLockAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, object?> BuildToolSearchOutputItem(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static IReadOnlyList<JsonElement> ExtractToolSearchOutputTools(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string BuildToolAbortMessage(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string BuildModelToolCallItemId(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string BuildSafeToolIdentifierSegment(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ResolveModelFunctionToolName(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelModelFunctionToolCallRuntime", modelFunctionToolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<object> ExecuteAsync(", modelFunctionToolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<object> ExecuteWithParallelLockAsync(", modelFunctionToolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public static string ResolveModelFunctionToolName(", modelFunctionToolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ToolUseFollowUpItemProjector.BuildToolSearchOutputItem(", modelFunctionToolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ToolUseFollowUpItemProjector.BuildFunctionCallOutputItem(", modelFunctionToolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ToolUseFollowUpItemProjector.BuildCancelledFunctionCallOutputItem(", modelFunctionToolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ToolUseFollowUpItemProjector.ExtractToolSearchOutputTools(", modelFunctionToolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ToolUseFollowUpItemProjector.BuildModelToolCallItemId(", modelFunctionToolCallRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, object?> BuildToolSearchOutputItem(", modelFunctionToolCallRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string BuildToolAbortMessage(", modelFunctionToolCallRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static IReadOnlyList<JsonElement> ExtractToolSearchOutputTools(", modelFunctionToolCallRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string BuildModelToolCallItemId(", modelFunctionToolCallRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string BuildSafeToolIdentifierSegment(", modelFunctionToolCallRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<JsonElement> ExtractToolSearchOutputTools(", toolUseFollowUpItemProjectorSource, StringComparison.Ordinal);
        Assert.Contains("public static string BuildModelToolCallItemId(", toolUseFollowUpItemProjectorSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelResponsesHttpStreamTransportRuntime responsesHttpStreamTransportRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesHttpStreamTransportRuntime = new KernelResponsesHttpStreamTransportRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesHttpStreamTransportRuntime.StreamWithRetryAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesHttpStreamTransportRuntime.StreamWithRetryAsync(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly HttpClient providerHttpClient;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Action<HttpRequestMessage> applyW3cTraceContext;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new HttpRequestMessage(HttpMethod.Post", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SendAsync(request, HttpCompletionOption.ResponseHeadersRead", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpCompletionOption.ResponseHeadersRead", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static async IAsyncEnumerable<string> EnumerateSseDataEventsAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildResponsesStreamIdleTimeoutMessage(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static async IAsyncEnumerable<string> EnumerateSseDataEventsAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildResponsesStreamIdleTimeoutMessage(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelResponsesHttpStreamTransportRuntime", responsesHttpStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<ResponsesStreamResult> StreamWithRetryAsync(", responsesHttpStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<ResponsesStreamResult> StreamAsync(", responsesHttpStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("providerRequestDiagnosticsRuntime.CaptureAsync(", responsesHttpStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesStreamFailureRuntime.ClassifyHttpStreamFailure(", responsesHttpStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("new HttpRequestMessage(HttpMethod.Post", responsesHttpStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("SendAsync(request, HttpCompletionOption.ResponseHeadersRead", responsesHttpStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("HttpCompletionOption.ResponseHeadersRead", responsesHttpStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static async IAsyncEnumerable<string> EnumerateSseDataEventsAsync(", responsesHttpStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static string BuildStreamIdleTimeoutMessage(", responsesHttpStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelResponsesWebSocketStreamTransportRuntime responsesWebSocketStreamTransportRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesWebSocketStreamTransportRuntime = new KernelResponsesWebSocketStreamTransportRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelResponsesWebSocketStreamTransportRuntime.CanUseTransport(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesWebSocketStreamTransportRuntime.PrewarmSessionAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("responsesWebSocketStreamTransportRuntime.StreamWithFallbackAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelResponsesWebSocketStreamTransportRuntime.CanUseTransport(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesWebSocketStreamTransportRuntime.PrewarmSessionAsync(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesWebSocketStreamTransportRuntime.StreamWithFallbackAsync(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool CanUseResponsesWebSocketTransport(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task PrewarmResponsesWebSocketSessionAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<ResponsesStreamResult> StreamResponsesWebSocketRequestWithFallbackAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<ResponsesStreamResult> StreamResponsesWebSocketRequestAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task PrewarmResponsesWebSocketSessionCoreAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<ClientWebSocket> EnsureResponsesWebSocketConnectedAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static async Task SendResponsesWebSocketMessageAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async IAsyncEnumerable<string> EnumerateResponsesWebSocketDataEventsAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static async Task<string?> ReceiveResponsesWebSocketMessageAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void CaptureResponsesWebSocketTurnState(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelResponsesWebSocketStreamTransportRuntime", responsesWebSocketStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public static bool CanUseTransport(", responsesWebSocketStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task PrewarmSessionAsync(", responsesWebSocketStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<ResponsesStreamResult> StreamWithFallbackAsync(", responsesWebSocketStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task<ResponsesStreamResult> StreamAsync(", responsesWebSocketStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task PrewarmSessionCoreAsync(", responsesWebSocketStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("providerRequestDiagnosticsRuntime.CaptureAsync(", responsesWebSocketStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task<ClientWebSocket> EnsureConnectedAsync(", responsesWebSocketStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static async Task SendMessageAsync(", responsesWebSocketStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private async IAsyncEnumerable<string> EnumerateDataEventsAsync(", responsesWebSocketStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static async Task<string?> ReceiveMessageAsync(", responsesWebSocketStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static void CaptureTurnState(", responsesWebSocketStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesStreamFailureRuntime.ClassifyWebSocketFailure(", responsesWebSocketStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("responsesHttpStreamTransportRuntime.StreamWithRetryAsync(", responsesWebSocketStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelResponsesTransportRuntimeHelpers.ApplyTransportHeaders(", responsesWebSocketStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelResponsesTransportRuntimeHelpers.CreateTransportResponseHeaders(", responsesWebSocketStreamTransportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelTurnDependencyResolutionRuntime dependencyResolutionRuntime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("dependencyResolutionRuntime = new KernelTurnDependencyResolutionRuntime(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("dependencyResolutionRuntime.ResolveAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("dependencyResolutionRuntime.ResolveAsync(", turnOperationChainRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Func<IReadOnlyList<KernelTurnInputItem>?, string, CancellationToken, Task<string?>> buildExplicitPluginInstructionsAsync;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Func<TurnRequestContext, string, CancellationToken, Task<List<KernelSkillDescriptor>>> resolveMentionedSkillsAsync;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Func<IReadOnlyList<KernelSkillDescriptor>, List<string>> buildSkillInjectionMessages;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelTurnExecutionRuntimeHelpers.ResolveTurnDependenciesAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnDependencyResolutionRuntime", turnDependencyResolutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public Task<TurnRequestContext> ResolveAsync(", turnDependencyResolutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelTurnExecutionRuntimeHelpers.ResolveTurnDependenciesAsync(", turnDependencyResolutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("buildExplicitPluginInstructionsAsync,", turnDependencyResolutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("resolveMentionedSkillsAsync,", turnDependencyResolutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("buildSkillInjectionMessages,", turnDependencyResolutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("resolveSkillEnvironmentDependenciesAsync,", turnDependencyResolutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("resolveSkillMcpDependenciesAsync,", turnDependencyResolutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelTurnExecutionRuntimeHelpers.ResolveTurnDeveloperMessage(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelTurnExecutionRuntimeHelpers.BuildSlicedResponsesConversationInput(", turnProviderAssistantRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelTurnExecutionRuntimeHelpers.ResolveTurnDeveloperMessage(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelTurnExecutionRuntimeHelpers.BuildSlicedResponsesConversationInput(", runtimeSource, StringComparison.Ordinal);

        Assert.Contains("internal sealed class AppHostCoreLoopRoutingRuntime", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<TurnRequestContext> ApplyOrchestrationAndModelRouteAsync(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public Task<TurnRequestContext> ApplyDefaultOrchestrationAndModelRouteAsync(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public Task<TurnRequestContext> ApplyReviewOrchestrationAndModelRouteAsync(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly AppHostCoreLoopRoutingRuntimeComposition composition;", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("composition = new AppHostCoreLoopRoutingRuntimeComposition(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("composition.EntryIntentResolver.ResolveRequestedStageId(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("composition.RoutingSessionRuntime", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains(".ApplyAsync(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("composition.RoutingSessionRuntime.ApplyTransient(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".ReadAsync(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".CommitStepAsync(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("composition.RoutingEntryPlanner.Plan(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("composition.TurnContextProjector.Project(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".GetThreadAsync(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".ApplySessionOrchestrationStepAsync(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly AppHostCoreLoopOrchestrationStateStore orchestrationStateStore;", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly AppHostCoreLoopEntryIntentResolver entryIntentResolver;", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly AppHostCoreLoopRoutingEntryPlanner routingEntryPlanner;", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly AppHostCoreLoopTurnContextProjector turnContextProjector;", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new AppHostCoreLoopOrchestrationStateStore(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new AppHostCoreLoopEntryIntentResolver(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new AppHostCoreLoopRoutingEntryPlanner(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new AppHostCoreLoopTurnContextProjector(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private TurnRequestContext ApplyCoreLoopEntryPlan(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private string? ResolveRequestedStageId(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuiltInStageDefinitions.Review", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuiltInStageDefinitions.Planning", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelCollaborationModeState.PlanMode", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public TurnRequestContext ApplyOrchestrationAndModelRoute(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PlanSessionCoreLoopEntry(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("stageRegistryRuntime.CreateContext(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("stageRegistryContext.EntryPlanner.PlanEntry(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("var rawConfig = readRuntimeConfig(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageRegistryRuntimeComposition.CreateRegistryFromConfig(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new SessionCoreLoopEntryPlanner(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("orchestrationStateProjector.ProjectInput(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("modelRouteResolver.Resolve(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("stageExecutorDispatchBinder.Bind(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorRuntimeContext = entry.DispatchPlan.RuntimeContext", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private IReadOnlyList<StageContextSegment> ToStageContextSegments(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private StageCheckpoint? ToStageCheckpoint(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorDispatcher.FromStageDefinitions(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultModelRouter.Instance.Resolve(", coreLoopRoutingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class AppHostCoreLoopRoutingRuntimeComposition", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("var orchestrationStateStore = new AppHostCoreLoopOrchestrationStateStore(threadStore);", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new AppHostCoreLoopTurnContextProjector(", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("new AppHostCoreLoopRuntimeConfigReader(readRuntimeConfig)", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("EntryIntentResolver = new AppHostCoreLoopEntryIntentResolver(normalize);", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("var routingEntryPlanner = new AppHostCoreLoopRoutingEntryPlanner(", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("new AppHostCoreLoopOrchestrationStateProjector()", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new StageRegistryPlanningRuntimeComposition()", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("new AppHostCoreLoopModelRouteResolver(normalize)", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new AppHostCoreLoopRouteDispatchPlanner(", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new AppHostStageExecutorDispatchBinder()", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("RoutingSessionRuntime = new AppHostCoreLoopRoutingSessionRuntime(", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public AppHostCoreLoopOrchestrationStateStore OrchestrationStateStore", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("public AppHostCoreLoopEntryIntentResolver EntryIntentResolver", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public AppHostCoreLoopRoutingEntryPlanner RoutingEntryPlanner", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public AppHostCoreLoopTurnContextProjector TurnContextProjector", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("public AppHostCoreLoopRoutingSessionRuntime RoutingSessionRuntime", coreLoopRoutingRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class AppHostCoreLoopRoutingSessionRuntime", coreLoopRoutingSessionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly AppHostCoreLoopOrchestrationStateStore orchestrationStateStore;", coreLoopRoutingSessionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly AppHostCoreLoopRoutingEntryPlanner routingEntryPlanner;", coreLoopRoutingSessionRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly AppHostCoreLoopTurnContextProjector turnContextProjector;", coreLoopRoutingSessionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<TurnRequestContext> ApplyAsync(", coreLoopRoutingSessionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains(".ReadAsync(", coreLoopRoutingSessionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("routingEntryPlanner.Plan(", coreLoopRoutingSessionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains(".CommitStepAsync(", coreLoopRoutingSessionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public TurnRequestContext ApplyTransient(", coreLoopRoutingSessionRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private TurnRequestContext Project(AppHostCoreLoopRoutingEntry entry)", coreLoopRoutingSessionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("TurnRequestContextExecutionDispatchProjection.Project(", coreLoopRoutingSessionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("entry.DispatchContext", coreLoopRoutingSessionRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TurnExecutionDispatchContext.FromDispatchPlan(entry.DispatchPlan)", coreLoopRoutingSessionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class AppHostCoreLoopRoutingEntryPlanner", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.Contains("public AppHostCoreLoopRoutingEntry Plan(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.Contains("private readonly AppHostCoreLoopRuntimeConfigReader runtimeConfigReader;", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.Contains("var rawConfig = runtimeConfigReader.Read(session.Cwd);", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Func<string?, Dictionary<string, object?>> readRuntimeConfig;", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("readRuntimeConfig(session.Cwd)", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageRegistryPlanningContextFactory.CreateContext(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("stageRegistryRuntime.CreateContext(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.Contains("orchestrationStateProjector.ProjectInput(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("routeDispatchPlanner.Plan(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("stageRegistryContext.EntryPlanner.PlanEntry(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.Contains("SessionCoreLoopRoutingPlanFactory.Plan(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.Contains("modelRouteResolver.Resolve(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorDispatchPlanFactory.Bind(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.Contains("TurnExecutionDispatchContextFactory.FromExecutionEntry(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorDispatcher.FromStageDefinitions(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("stageExecutorDispatchBinder.Bind(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageRegistryRuntimeComposition.CreateRegistryFromConfig(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new SessionCoreLoopEntryPlanner(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionObservedStateFactory.Create(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.Contains("KernelThreadObservedStateProjectionFactory.ProjectArtifactRefs(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private IReadOnlyList<ArtifactRef> BuildArtifactRefs(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new StageContextSegment(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new ArtifactRef(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record AppHostCoreLoopRoutingEntry(", coreLoopRoutingEntryPlannerSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class AppHostCoreLoopRuntimeConfigReader", coreLoopRuntimeConfigReaderSource, StringComparison.Ordinal);
        Assert.Contains("private readonly Func<string?, Dictionary<string, object?>> readRuntimeConfig;", coreLoopRuntimeConfigReaderSource, StringComparison.Ordinal);
        Assert.Contains("public Dictionary<string, object?> Read(string? cwd)", coreLoopRuntimeConfigReaderSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class AppHostCoreLoopOrchestrationStateStore", coreLoopStateStoreSource, StringComparison.Ordinal);
        Assert.Contains(".GetThreadAsync(", coreLoopStateStoreSource, StringComparison.Ordinal);
        Assert.Contains(".ApplySessionOrchestrationStepAsync(", coreLoopStateStoreSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record AppHostCoreLoopStoredOrchestrationState", coreLoopStateStoreSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class AppHostCoreLoopOrchestrationStateProjector", coreLoopStateProjectorSource, StringComparison.Ordinal);
        Assert.Contains("public SessionOrchestrationInput ProjectInput(", coreLoopStateProjectorSource, StringComparison.Ordinal);
        Assert.Contains("SessionOrchestrationInputFactory.Create(", coreLoopStateProjectorSource, StringComparison.Ordinal);
        Assert.Contains("KernelThreadOrchestrationInputProjectionFactory.Project(", coreLoopStateProjectorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuiltInStageDefinitions.Default", coreLoopStateProjectorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new SessionOrchestrationInput(", coreLoopStateProjectorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private IReadOnlyList<StageContextSegment> ToStageContextSegments(", coreLoopStateProjectorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private StageCheckpoint? ToStageCheckpoint(", coreLoopStateProjectorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new StageContextSegment(", coreLoopStateProjectorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new StageCheckpoint(", coreLoopStateProjectorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new ArtifactRef(", coreLoopStateProjectorSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelThreadOrchestrationInputProjectionFactory", kernelThreadOrchestrationInputProjectionFactorySource, StringComparison.Ordinal);
        Assert.Contains("new StageContextSegment(", kernelThreadOrchestrationInputProjectionFactorySource, StringComparison.Ordinal);
        Assert.Contains("new StageCheckpoint(", kernelThreadOrchestrationInputProjectionFactorySource, StringComparison.Ordinal);
        Assert.Contains("new ArtifactRef(", kernelThreadOrchestrationInputProjectionFactorySource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelThreadObservedStateProjectionFactory", kernelThreadObservedStateProjectionFactorySource, StringComparison.Ordinal);
        Assert.Contains("new ArtifactRef(", kernelThreadObservedStateProjectionFactorySource, StringComparison.Ordinal);
        Assert.Contains("public static class SessionOrchestrationInputFactory", kernelSessionOrchestrationInputFactorySource, StringComparison.Ordinal);
        Assert.Contains("BuiltInStageDefinitions.Default", kernelSessionOrchestrationInputFactorySource, StringComparison.Ordinal);
        Assert.Contains("new SessionOrchestrationInput(", kernelSessionOrchestrationInputFactorySource, StringComparison.Ordinal);
        Assert.Contains("public static class SessionObservedStateFactory", kernelSessionObservedStateFactorySource, StringComparison.Ordinal);
        Assert.Contains("new SessionObservedState(", kernelSessionObservedStateFactorySource, StringComparison.Ordinal);
        Assert.Contains("new StageContextSegment(", kernelSessionObservedStateFactorySource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class AppHostCoreLoopEntryIntentResolver", coreLoopEntryIntentResolverSource, StringComparison.Ordinal);
        Assert.Contains("public string? ResolveRequestedStageId(", coreLoopEntryIntentResolverSource, StringComparison.Ordinal);
        Assert.Contains("CoreLoopEntryIntentResolver kernelResolver", coreLoopEntryIntentResolverSource, StringComparison.Ordinal);
        Assert.Contains("kernelResolver.ResolveRequestedStageId(", coreLoopEntryIntentResolverSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuiltInStageDefinitions.Review", coreLoopEntryIntentResolverSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuiltInStageDefinitions.Planning", coreLoopEntryIntentResolverSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelCollaborationModeState.PlanMode", coreLoopEntryIntentResolverSource, StringComparison.Ordinal);
        Assert.Contains("public sealed class CoreLoopEntryIntentResolver", kernelCoreLoopEntryIntentResolverSource, StringComparison.Ordinal);
        Assert.Contains("public enum CoreLoopEntryIntent", kernelCoreLoopEntryIntentResolverSource, StringComparison.Ordinal);
        Assert.Contains("public static class KernelBuiltInStageIds", kernelCoreLoopEntryIntentResolverSource, StringComparison.Ordinal);
        Assert.False(
            File.Exists(Path.Combine(repoRoot, "src", "Core", "TianShu.RuntimeComposition", "StageRegistryPlanningRuntimeComposition.cs")),
            "RuntimeComposition must not reintroduce a dedicated Stage Registry planning pure-forwarding bridge.");
        Assert.Contains("public static class StageRegistryPlanningContextFactory", kernelStageRegistryPlanningContextFactorySource, StringComparison.Ordinal);
        Assert.Contains("StageRegistryRuntimeComposition.CreateRegistryFromConfig(", kernelStageRegistryPlanningContextFactorySource, StringComparison.Ordinal);
        Assert.Contains("new SessionCoreLoopEntryPlanner(", kernelStageRegistryPlanningContextFactorySource, StringComparison.Ordinal);
        Assert.Contains("public sealed record StageRegistryPlanningContext", kernelStageRegistryPlanningContextFactorySource, StringComparison.Ordinal);
        Assert.False(
            File.Exists(Path.Combine(repoRoot, "src", "Core", "TianShu.RuntimeComposition", "AppHostCoreLoopRouteDispatchPlanner.cs")),
            "RuntimeComposition must not reintroduce a dedicated route dispatch planner bridge.");
        Assert.Contains("internal sealed class AppHostCoreLoopModelRouteResolver", coreLoopModelRouteResolverSource, StringComparison.Ordinal);
        Assert.Contains("DefaultModelRouter.Instance.Resolve(", coreLoopModelRouteResolverSource, StringComparison.Ordinal);
        Assert.Contains("ProviderBaseUrl = result.BaseUrl", coreLoopModelRouteResolverSource, StringComparison.Ordinal);
        Assert.Contains("ProviderApiKeyEnvironmentVariable = result.ApiKeyEnvironmentVariable", coreLoopModelRouteResolverSource, StringComparison.Ordinal);
        Assert.Contains("ProviderWireApi = result.Protocol", coreLoopModelRouteResolverSource, StringComparison.Ordinal);
        Assert.DoesNotContain("result.BaseUrl ?? context.ProviderBaseUrl", coreLoopModelRouteResolverSource, StringComparison.Ordinal);
        Assert.DoesNotContain("result.ApiKeyEnvironmentVariable ?? context.ProviderApiKeyEnvironmentVariable", coreLoopModelRouteResolverSource, StringComparison.Ordinal);
        Assert.DoesNotContain("result.Protocol ?? context.ProviderWireApi", coreLoopModelRouteResolverSource, StringComparison.Ordinal);
        Assert.False(
            File.Exists(Path.Combine(repoRoot, "src", "Core", "TianShu.RuntimeComposition", "StageExecutorDispatchRuntimeComposition.cs")),
            "RuntimeComposition must not reintroduce a dedicated StageExecutor dispatch pure-forwarding bridge.");
        Assert.Contains("internal static class TurnRequestContextExecutionDispatchProjection", runtimeModelsSource, StringComparison.Ordinal);
        Assert.Contains("ExecutionDispatchContext = dispatchContext", runtimeModelsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StageExecutorRuntimeContext", runtimeModelsSource, StringComparison.Ordinal);
        Assert.False(
            File.Exists(Path.Combine(repoRoot, "src", "Core", "TianShu.RuntimeComposition", "AppHostCoreLoopTurnContextProjector.cs")),
            "RuntimeComposition must not reintroduce the StageExecutor dispatch-plan to TurnRequestContext projection bridge.");
        Assert.Contains("internal static class AppHostTurnRequestContextFactory", appHostTurnRequestContextFactorySource, StringComparison.Ordinal);
        Assert.Contains("public static TurnRequestContext CreateFromTransportParams(", appHostTurnRequestContextFactorySource, StringComparison.Ordinal);
        Assert.Contains("public static TurnRequestContext CreateFromTurnStartRequest(", appHostTurnRequestContextFactorySource, StringComparison.Ordinal);
        Assert.Contains("public static TurnRequestContext CreateBase(", appHostTurnRequestContextFactorySource, StringComparison.Ordinal);
        Assert.Contains("KernelThreadTransportParsers.ParseTurnInputItems(", appHostTurnRequestContextFactorySource, StringComparison.Ordinal);
        Assert.Contains("KernelJsonSchemaPayload.FromElement(", appHostTurnRequestContextFactorySource, StringComparison.Ordinal);
        Assert.Contains("KernelCollaborationModeState.NormalizeOrDefault(", appHostTurnRequestContextFactorySource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelThreadProjectionPayloadFactory", kernelThreadProjectionPayloadFactorySource, StringComparison.Ordinal);
        Assert.Contains("public static KernelThreadSessionProjectionPayload? ToSessionProjectionPayload(", kernelThreadProjectionPayloadFactorySource, StringComparison.Ordinal);
        Assert.Contains("public static KernelThreadOrchestrationProjectionPayload? ToOrchestrationProjectionPayload(", kernelThreadProjectionPayloadFactorySource, StringComparison.Ordinal);
        Assert.Contains("KernelThreadOrchestrationStateNormalizer.Clone(", kernelThreadProjectionPayloadFactorySource, StringComparison.Ordinal);
        Assert.Contains("new KernelThreadOrchestratorDecisionProjectionPayload(", kernelThreadProjectionPayloadFactorySource, StringComparison.Ordinal);
        Assert.Contains("new KernelThreadStageContextPackageProjectionPayload(", kernelThreadProjectionPayloadFactorySource, StringComparison.Ordinal);
        Assert.False(
            File.Exists(Path.Combine(repoRoot, "src", "Core", "TianShu.RuntimeComposition", "AppHostThreadProjectionPayloadFactory.cs")),
            "RuntimeComposition must not reintroduce AppHost response projection payload mapping.");

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelTurnExecutionRuntimeHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static async Task<TurnRequestContext> ResolveTurnDependenciesAsync(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static TurnRequestContext RefreshLoopTurnContext(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string ResolveTurnInstructions(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<string>? ResolveContextualUserMessages(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string? ResolveTurnDeveloperMessage(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static List<Dictionary<string, object?>> BuildProviderMessages(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static List<object> BuildResponsesConversationInput(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<KernelConversationHistoryItem> EnumerateTurnConversationHistoryItems(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("=> KernelTurnExecutionRuntimeHelpers.BuildProviderMessages(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("=> KernelTurnExecutionRuntimeHelpers.BuildResponsesConversationInput(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("=> KernelTurnExecutionRuntimeHelpers.ResolveTurnInstructions(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("=> KernelTurnExecutionRuntimeHelpers.ResolveTurnDeveloperMessage(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConversationHistoryUtilities.ParseInputItems(currentInputItems)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConversationHistoryUtilities.BuildProviderContentItems(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? ExtractTurnItemConversationText(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? ExtractTurnItemContentArrayText(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? ExtractTurnItemContentObjectText(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("KernelConversationHistoryUtilities.ParseHistoryItem(item.Payload)", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("KernelConversationHistoryUtilities.BuildDisplayText(historyItem)", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("KernelConversationHistoryUtilities.BuildProviderContentItems(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string ExtractInputText(IEnumerable<JsonElement> inputItems)", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string ExtractInputText(IEnumerable<KernelTurnInputItem> inputItems)", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static int CountInputTextChars(IReadOnlyList<JsonElement> inputItems)", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static int CountInputTextChars(IReadOnlyList<KernelTurnInputItem> inputItems)", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static int CountTextChars(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ExtractUserTextFromInputItems(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static int CountInputTextChars(", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record TurnRequestContext(", runtimeModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal enum TurnOperationKind", runtimeModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class TurnOperationState", runtimeModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record ModelFunctionCall(", runtimeModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record ResponsesStreamResult(", runtimeModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelResponsesStreamException", runtimeModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record ResponsesTransportSettings(", runtimeModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ResponsesWebSocketTurnSession", runtimeModelsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelTurnReviewSurfaceAppHostRuntime_ShouldOwnTurnSteerAndReviewStartSurfaces()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var paritySurfacePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.Surface.cs");
        var runtimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelTurnReviewSurfaceAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var runtimeSource = File.ReadAllText(runtimePath);

        Assert.Contains("private readonly KernelTurnReviewSurfaceAppHostRuntime turnReviewSurfaceAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("this.turnReviewSurfaceAppHostRuntime = new KernelTurnReviewSurfaceAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.False(File.Exists(paritySurfacePath));
        Assert.Contains("await turnReviewSurfaceAppHostRuntime.HandleTurnSteerAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await turnReviewSurfaceAppHostRuntime.HandleReviewStartAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("case \"mock/experimentalMethod\":", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mock/experimentalMethod\" => \"mock/experimentalMethod\"", kernelAppServerSource, StringComparison.Ordinal);

        Assert.False(File.Exists(paritySurfacePath));

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelTurnReviewSurfaceAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleTurnSteerAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleReviewStartAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("BuildReviewTurnItems(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("EnrichReviewPromptWithTargetContextAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("ResolveDetachedReviewModelAsync(", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelResponsesTransportRuntimeHelpers_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelTransportPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ResponsesTransport.cs");
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelResponsesTransportRuntimeHelpers.cs");
        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);

        Assert.False(File.Exists(kernelTransportPath));

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelResponsesTransportRuntimeHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static (IReadOnlyList<JsonElement> Input, string? PreviousResponseId) BuildResponsesWebSocketRequestInput(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string BuildResponsesWebSocketRequestSignature(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<JsonElement> CloneJsonElements(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static ProviderResponsesTransportResponseHeaders CreateTransportResponseHeaders(HttpHeaders headers)", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static ProviderResponsesTransportResponseHeaders CreateTransportResponseHeaders(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static void ApplyTransportHeaders(HttpHeaders headers, IReadOnlyDictionary<string, string> values)", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static void ApplyTransportHeaders(ClientWebSocketOptions options, IReadOnlyDictionary<string, string> values)", runtimeHelpersSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelRealtimeAppHostRuntime_ShouldOwnRealtimeTransportAndSurfaceOrchestration()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var kernelThreadLifecyclePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ThreadLifecycle.cs");
        var kernelRealtimeSurfacePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.Realtime.Surface.cs");
        var kernelRealtimeTransportPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.RealtimeTransport.cs");
        var realtimeRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelRealtimeAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var kernelThreadLifecycleSource = ReadKernelThreadLifecycleFacadeSource(repoRoot);
        var realtimeRuntimeSource = File.ReadAllText(realtimeRuntimePath);

        Assert.False(File.Exists(kernelRealtimeSurfacePath));
        Assert.False(File.Exists(kernelRealtimeTransportPath));

        Assert.Contains("private readonly KernelRealtimeAppHostRuntime realtimeAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("this.realtimeAppHostRuntime = new KernelRealtimeAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await realtimeAppHostRuntime.ShutdownRealtimeSessionsAsync().ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await realtimeAppHostRuntime.HandleThreadRealtimeStartAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await realtimeAppHostRuntime.HandleThreadRealtimeAppendAudioAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await realtimeAppHostRuntime.HandleThreadRealtimeAppendTextAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await realtimeAppHostRuntime.HandleThreadRealtimeHandoffOutputAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await realtimeAppHostRuntime.HandleThreadRealtimeStopAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("(realtimeSession, closeReason) => realtimeAppHostRuntime!.CloseRealtimeTransportAsync(realtimeSession, closeReason),", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", realtimeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelRealtimeAppHostRuntime", realtimeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadRealtimeStartAsync(", realtimeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadRealtimeAppendAudioAsync(", realtimeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadRealtimeAppendTextAsync(", realtimeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadRealtimeHandoffOutputAsync(", realtimeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleThreadRealtimeStopAsync(", realtimeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task ShutdownRealtimeSessionsAsync()", realtimeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public Task CloseRealtimeTransportAsync(KernelRealtimeSessionState session, string closeReason)", realtimeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task<bool> TryStartRealtimeWebSocketAsync(", realtimeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task DispatchRealtimeTransportEventAsync(", realtimeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static object BuildRealtimeSessionUpdatePayload(", realtimeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ToolUseFollowUpItemProjector.BuildFunctionCallOutputItem(", realtimeRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("type = \"function_call_output\"", realtimeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private static Uri BuildRealtimeWebSocketUri(", realtimeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task CloseRealtimeTransportCoreAsync(", realtimeRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelRealtimeContextRuntimeHelpers_ShouldOwnRealtimeContextAndStartupInstructionHelpers()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var kernelRealtimeContextPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.Realtime.Context.cs");
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelRealtimeContextRuntimeHelpers.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);

        Assert.False(File.Exists(kernelRealtimeContextPath));

        Assert.Contains("KernelRealtimeContextRuntimeHelpers.BuildConfiguredRealtimeSessionState(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("KernelRealtimeContextRuntimeHelpers.BuildRealtimeStartDeveloperInstruction(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("KernelRealtimeContextRuntimeHelpers.ResolveRealtimeDeveloperInstructions(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private const string RealtimeStartupContextHeader", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private string BuildRealtimeStartupContext(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private string BuildRealtimeStartDeveloperInstruction(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string BuildRealtimeEndDeveloperInstruction(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private KernelRealtimeSessionState BuildConfiguredRealtimeSessionState(", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("internal static class KernelRealtimeContextRuntimeHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelRealtimeSessionState BuildConfiguredRealtimeSessionState(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string BuildRealtimeStartDeveloperInstruction(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string? ResolveRealtimeDeveloperInstructions(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static string BuildRealtimeEndDeveloperInstruction(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("private static string BuildRealtimeStartupContext(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("private static string? BuildRealtimeRecentWorkSection(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("private static string? BuildRealtimeWorkspaceSection(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelToolRuntimeAgentHelpers_ShouldLiveInAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelToolRuntimePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ToolRuntime.cs");
        var runtimeHelpersPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolRuntimeAgentHelpers.cs");
        var appHostRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolRuntimeAppHostRuntime.cs");

        var runtimeHelpersSource = File.ReadAllText(runtimeHelpersPath);
        var appHostRuntimeSource = File.ReadAllText(appHostRuntimePath);

        Assert.False(File.Exists(kernelToolRuntimePath));

        Assert.Contains("internal static class KernelToolRuntimeAgentHelpers", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static readonly TimeSpan WaitPollInterval", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static int NormalizeWaitTimeoutMs(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelSessionSource BuildSpawnedAgentSource(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelThreadSessionState BuildSpawnedAgentSession(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static JsonNode? BuildAgentStatusNode(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("public static bool IsFinalAgentStatus(", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeHelpersSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeAgentHelpers.BuildSpawnedAgentSource(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeAgentHelpers.BuildSpawnedAgentSession(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeAgentHelpers.BuildAgentStatusNode(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeAgentHelpers.NormalizeWaitTimeoutMs(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeAgentHelpers.WaitPollInterval", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeAgentHelpers.IsFinalAgentStatus(", appHostRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelToolRuntimeAppHostRuntime_ShouldLiveUnderAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var kernelToolsPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelTools.cs");
        var kernelToolRuntimePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ToolRuntime.cs");
        var toolExecutionRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolExecutionAppHostRuntime.cs");
        var bridgeRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolRuntimeServicesAppHostRuntime.cs");
        var appHostRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolRuntimeAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var kernelToolsSource = AssertFileDeletedAndReturnEmpty(kernelToolsPath);
        var toolExecutionRuntimeSource = File.ReadAllText(toolExecutionRuntimePath);
        var bridgeRuntimeSource = File.ReadAllText(bridgeRuntimePath);
        var appHostRuntimeSource = File.ReadAllText(appHostRuntimePath);

        Assert.Contains("new KernelToolRuntimeAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("new KernelToolExecutionAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("new KernelToolRuntimeServicesAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.False(File.Exists(kernelToolRuntimePath));
        Assert.Contains("toolExecutionAppHostRuntime!.TryPersistDynamicToolApprovalAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<bool> TryPersistDynamicToolApprovalAsync(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("toolRuntimeServicesAppHostRuntime.CreateToolRuntimeServices(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("toolRuntimeAppHostRuntime.RequestUserInputAsync(", kernelToolsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("toolRuntimeAppHostRuntime.RequestPermissionsAsync(", kernelToolsSource, StringComparison.Ordinal);
        Assert.Contains("toolRuntimeServicesAppHostRuntime.CreateToolRuntimeServices(", toolExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("toolRuntimeAppHostRuntime.RequestUserInputAsync(", toolExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("toolRuntimeAppHostRuntime.RequestPermissionsAsync(", toolExecutionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("toolRuntimeAppHostRuntime.UpdatePlanAsync(", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("toolRuntimeAppHostRuntime.SpawnAgentAsync(", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("toolRuntimeAppHostRuntime.SendInputToAgentAsync(", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("toolRuntimeAppHostRuntime.ResumeAgentAsync(", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("toolRuntimeAppHostRuntime.WaitOnAgentsAsync(", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("toolRuntimeAppHostRuntime.ReportAgentJobResultAsync(", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("toolRuntimeAppHostRuntime.CloseAgentAsync(", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelToolRuntimeRequestContext.FromTurnRequestContext(turnContext)", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelToolRuntimeRequestContext CreateToolRuntimeRequestContext(", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelToolRuntimeRequestContext CreateToolRuntimeRequestContext(", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelToolRuntimeRequestContext(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelToolRuntimeRequestContext FromTurnRequestContext(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelToolRuntimeAppHostRuntime", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task UpdatePlanAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelRequestUserInputResponse> RequestUserInputAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelRequestPermissionsResponse> RequestPermissionsAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelSpawnAgentResponse> SpawnAgentAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelSendInputResponse> SendInputToAgentAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<JsonNode?> ResumeAgentAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelWaitAgentsResponse> WaitOnAgentsAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<bool> ReportAgentJobResultAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<JsonNode?> CloseAgentAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<JsonNode?> GetAgentStatusNodeAsync(", appHostRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelPluginsAppHostRuntime_ShouldLiveUnderAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        var kernelPluginsPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.Plugins.cs");
        var kernelParityPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var kernelToolRuntimePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ToolRuntime.cs");
        var bridgeRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolRuntimeServicesAppHostRuntime.cs");
        var appHostRuntimePath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelPluginsAppHostRuntime.cs");
        var appHostRuntimeProjectPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "TianShu.AppHost.Tools.Runtime.csproj");
        var providerAdapterPath = Path.Combine(repoRoot, "src", "Provider", "TianShu.Provider.Abstractions", "OpenAiAppCatalogCompatibilityAdapter.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerPath);
        var kernelParitySource = AssertFileDeletedAndReturnEmpty(kernelParityPath);
        var bridgeRuntimeSource = File.ReadAllText(bridgeRuntimePath);
        var appHostRuntimeSource = File.ReadAllText(appHostRuntimePath);
        var appHostRuntimeProjectSource = File.ReadAllText(appHostRuntimeProjectPath);
        var providerAdapterSource = File.ReadAllText(providerAdapterPath);

        Assert.False(File.Exists(kernelPluginsPath));
        Assert.False(File.Exists(kernelToolRuntimePath));
        Assert.Contains("new KernelPluginsAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("cancellationToken => pluginsAppHostRuntime!.SyncRemotePluginStatesAsync(cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("cancellationToken => pluginsAppHostRuntime.LoadToolSuggestDiscoverableConnectorsAsync(cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pluginsAppHostRuntime.HandleSkillsListAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pluginsAppHostRuntime.HandleSkillsRemoteListAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pluginsAppHostRuntime.HandleSkillsRemoteExportAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pluginsAppHostRuntime.HandleSkillsConfigWriteAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pluginsAppHostRuntime.HandlePluginListAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pluginsAppHostRuntime.HandlePluginReadAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pluginsAppHostRuntime.HandlePluginInstallAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pluginsAppHostRuntime.HandlePluginUninstallAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pluginsAppHostRuntime.HandleAppListAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pluginsAppHostRuntime.LoadAppsAsync(forceRefetch: false, cwd, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pluginsAppHostRuntime.ClearAppListCache();", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("cachedAppDirectoryConnectors", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("cachedAccessibleAppConnectors", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("appListStateGate", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("pluginsAppHostRuntime.LoadToolSuggestDiscoverableConnectorsAsync(cancellationToken)", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("pluginsAppHostRuntime.RefreshOpenAiAppsToolSnapshotAsync(cancellationToken)", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("runtimeThread.UpdateSession(runtimeThread.Session with", bridgeRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("pluginsAppHostRuntime.ReadChatGptAuthContextAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("pluginsAppHostRuntime.FetchAllConnectorsAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("pluginsAppHostRuntime.TryFetchAccessibleConnectorsAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelPluginsAppHostRuntime.ReadConfiguredChatGptBaseUrl(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadAppsAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleSkillsListAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleSkillsRemoteListAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleSkillsRemoteExportAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleSkillsConfigWriteAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleAppListAsync(", kernelParitySource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelChatGptAuthContext(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelAccessibleConnectorsResult(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelAccessibleToolsSnapshot(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelPluginsAppHostRuntime", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public Task<string?> ReadTianShuConfigTextAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public static bool AreConnectorsEnabled(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public static string? ReadConfiguredChatGptBaseUrl(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelChatGptAuthContext?> ReadChatGptAuthContextAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandlePluginListAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandlePluginReadAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandlePluginInstallAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandlePluginUninstallAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleAppListAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleSkillsListAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleSkillsRemoteListAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleSkillsRemoteExportAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleSkillsConfigWriteAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<object[]> LoadPluginAppsNeedingAuthAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<IReadOnlyList<KernelRemotePluginState>> SyncRemotePluginStatesAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<IReadOnlyList<KernelToolSuggestConnectorInfo>> LoadToolSuggestDiscoverableConnectorsAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelOpenAiAppsToolSnapshot> RefreshOpenAiAppsToolSnapshotAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public void ClearAppListCache()", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<List<ControlPlaneAppDescriptor>> LoadAppsAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<List<KernelPluginConnectorInfo>> FetchAllConnectorsAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelAccessibleConnectorsResult> TryFetchAccessibleConnectorsAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private readonly object appListStateGate = new();", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("cachedAppDirectoryConnectors", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("cachedAccessibleAppConnectors", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("private async Task<KernelAccessibleToolsSnapshot> TryFetchAccessibleToolsSnapshotAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("OpenAiAppCatalogCompatibilityAdapter.TryReadConfiguredBaseUrl(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("TryReadAuthContextAsync(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("OpenAiAppCatalogCompatibilityAdapter.IsToolSuggestDiscoverableConnector(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("OpenAiAppCatalogCompatibilityAdapter.BuildCatalogUri(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("OpenAiAppCatalogCompatibilityAdapter.ApplyAuthHeaders(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string BuildConnectorInstallUrl(", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public const string DefaultBaseUrl =", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private const string ChatGptAuthFileName =", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static readonly string[] ToolSuggestDiscoverableConnectorIds =", appHostRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static readonly string[] DisallowedConnectorIds =", appHostRuntimeSource, StringComparison.Ordinal);

        Assert.Contains("<ProjectReference Include=\"..\\..\\Provider\\TianShu.Provider.Abstractions\\TianShu.Provider.Abstractions.csproj\" />", appHostRuntimeProjectSource, StringComparison.Ordinal);
        Assert.DoesNotContain("<ProjectReference Include=\"..\\..\\Provider\\TianShu.Provider.OpenAI\\TianShu.Provider.OpenAI.csproj\"", appHostRuntimeProjectSource, StringComparison.Ordinal);
        Assert.Contains("public static class OpenAiAppCatalogCompatibilityAdapter", providerAdapterSource, StringComparison.Ordinal);
        Assert.Contains("public const string DefaultBaseUrl = OpenAiAppCatalogCompatibilityKeys.DefaultBaseUrl;", providerAdapterSource, StringComparison.Ordinal);
        Assert.Contains("public const string CodexAppsMcpServerName = OpenAiAppCatalogCompatibilityKeys.CodexAppsMcpServerName;", providerAdapterSource, StringComparison.Ordinal);
        Assert.Contains("public static string? BuildConnectorApprovalSessionKey(", providerAdapterSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostToolsRuntimeSources_ShouldUseAppHostToolsRuntimeNamespace()
    {
        var repoRoot = FindRepoRoot();
        var files = new[]
        {
            "KernelApplyPatchRuntimeSupport.cs",
            "KernelApprovalPolicyHelpers.cs",
            "KernelArtifactsAppHostRuntime.cs",
            "KernelArtifactsRuntimeSupport.cs",
            "KernelArtifactsRuntimeHelpers.cs",
            "KernelAutoCompactionRuntimeHelpers.cs",
            "KernelCollaborationModePrompts.cs",
            "KernelCodeModeAppHostRuntime.cs",
            "KernelCodeModeProtocolAppHostRuntime.cs",
            "KernelCodeModeProtocolHelpers.cs",
            "KernelCodeModeRuntimeHelpers.cs",
            "KernelCodeModeRuntimeSupport.cs",
            "KernelCollaborationRuntimeSupport.cs",
            "KernelCollaborationLifecycleHelpers.cs",
            "KernelCommittedUnifiedExec.cs",
            "KernelJsReplAppHostRuntime.cs",
            "KernelJsReplRuntimeHelpers.cs",
            "KernelJsReplRuntimeSupport.cs",
            "KernelExecPolicyManager.cs",
            "KernelManagedNetworkAppHostRuntime.cs",
            "KernelManagedNetworkExecutionLeaseDefaults.cs",
            "KernelManagedNetworkExecutionRequest.cs",
            "KernelManagedNetwork.cs",
            "McpServerSurfaceAppHostRuntime.cs",
            "McpServerSurfaceHelpers.cs",
            "KernelNativeToolOptionsAppHostRuntime.cs",
            "KernelPendingInteractiveReplayAppHostRuntime.cs",
            "KernelPendingInteractiveReplayHelpers.cs",
            "KernelRealtimeAppHostRuntime.cs",
            "KernelThreadHistoryAppHostRuntime.cs",
            "KernelThreadLifecycleAppHostRuntime.cs",
            "KernelTurnExecutionAppHostRuntime.cs",
            "KernelTurnExecutionRuntimeHelpers.cs",
            "KernelTurnExecutionRuntimeModels.cs",
            "KernelToolRuntimeServices.cs",
            "KernelPluginsAppHostRuntime.cs",
            "KernelProposedPlanRuntimeHelpers.cs",
            "KernelResponsesTransportRuntimeHelpers.cs",
            "KernelResponsesToolRegistry.cs",
            "KernelReviewOutputParity.cs",
            "KernelSandboxEnforcer.cs",
            "KernelSpawnAgentsOnCsvRuntimeHelpers.cs",
            "KernelSubagentNotificationAppHostRuntime.cs",
            "KernelSubagentNotificationUtilities.cs",
            "KernelToolAbstractions.cs",
            "KernelToolSchemaValidator.cs",
            "KernelToolRuntimeAgentHelpers.cs",
            "KernelToolRuntimeAppHostRuntime.cs",
            "KernelToolRuntimeApprovalHelpers.cs",
            "KernelToolRuntimeInteractionHelpers.cs",
            "KernelToolItemLifecycleAppHostRuntime.cs",
            "KernelToolItemLifecycleHelpers.cs",
            "KernelToolRuntimeParsingHelpers.cs",
            "KernelToolExecutionNotificationHook.cs",
            "KernelToolSandboxResolver.cs",
            "KernelUserShellAppHostRuntime.cs",
            "KernelUnifiedExecAvailability.cs",
            "KernelShellRuntimeSupport.cs",
            "KernelTestSyncRuntimeSupport.cs",
            "KernelViewImageRuntimeSupport.cs",
        };

        foreach (var fileName in files)
        {
            var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", fileName));
            Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KernelProposedPlanRuntimeHelpers_ShouldLiveUnderAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerFile = GetAppHostServerSourcePath(repoRoot);
        var kernelSourceFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.ProposedPlan.cs");
        var runtimeHelpersFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelProposedPlanRuntimeHelpers.cs");

        Assert.True(File.Exists(kernelAppServerFile));
        Assert.False(File.Exists(kernelSourceFile));
        Assert.True(File.Exists(runtimeHelpersFile));

        var kernelSource = File.ReadAllText(kernelAppServerFile);
        var runtimeSource = File.ReadAllText(runtimeHelpersFile);

        Assert.DoesNotContain("private sealed record KernelProposedPlanSegment", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelProposedPlanExtraction", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed class KernelProposedPlanStreamParser", kernelSource, StringComparison.Ordinal);
        Assert.Contains("private static KernelProposedPlanExtraction ExtractProposedPlanText(", kernelSource, StringComparison.Ordinal);
        Assert.Contains("new KernelProposedPlanStreamParser()", kernelSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelProposedPlanSegment", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelProposedPlanExtraction", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelProposedPlanStreamParser", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelCollaborationAndReviewRuntimeHelpers_ShouldLiveUnderAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var oldFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelCollaborationModePrompts.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelReviewOutputParity.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelSubagentNotificationUtilities.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelThreadSessionSource.cs"),
        };
        var newFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelCollaborationModePrompts.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelReviewOutputParity.cs"),
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelSubagentNotificationUtilities.cs"),
        };

        foreach (var file in oldFiles)
        {
            Assert.False(File.Exists(file), $"旧文件不应继续存在: {file}");
        }

        foreach (var file in newFiles)
        {
            Assert.True(File.Exists(file), $"缺少已归位文件: {file}");
        }
    }

    [Fact]
    public void AppHostToolsRuntimeProject_ShouldOwnCollaborationModeEmbeddedResources()
    {
        var repoRoot = FindRepoRoot();
        var runtimeProjectFile = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "TianShu.AppHost.Tools.Runtime.csproj");
        var kernelProjectFile = GetDeletedKernelProjectFilePath(repoRoot);

        var runtimeSource = File.ReadAllText(runtimeProjectFile);
        var kernelSource = AssertFileDeletedAndReturnEmpty(kernelProjectFile);

        Assert.Contains("<EmbeddedResource Include=\"Resources\\collaboration_mode\\default.md\" LogicalName=\"TianShu.AppHost.Tools.Runtime.Resources.collaboration-mode.default.md\" />", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("<EmbeddedResource Include=\"Resources\\collaboration_mode\\plan.md\" LogicalName=\"TianShu.AppHost.Tools.Runtime.Resources.collaboration-mode.plan.md\" />", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Resources\\Codex\\collaboration_mode\\default.md", kernelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Resources\\Codex\\collaboration_mode\\plan.md", kernelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutionIntegrationTestsProject_ShouldReferenceAppHostToolsRuntimeForDirectHelperCoverage()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "tests",
            "TianShu.Execution.Integration.Tests",
            "TianShu.Execution.Integration.Tests.csproj");

        var source = File.ReadAllText(projectFile);

        Assert.Contains(
            "<ProjectReference Include=\"..\\..\\src\\Hosting\\TianShu.AppHost.Tools.Runtime\\TianShu.AppHost.Tools.Runtime.csproj\" />",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void KernelProject_ShouldReferenceAppHostToolsRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var projectFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "TianShu.AppHost.csproj");
        var deletedKernelProjectFile = GetDeletedKernelProjectFilePath(repoRoot);

        var source = File.ReadAllText(projectFile);
        _ = AssertFileDeletedAndReturnEmpty(deletedKernelProjectFile);

        Assert.Contains(
            "<ProjectReference Include=\"..\\TianShu.AppHost.Tools.Runtime\\TianShu.AppHost.Tools.Runtime.csproj\" />",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostToolsRuntimeProject_ShouldReferenceExpectedSouthboundProjects()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "TianShu.AppHost.Tools.Runtime.csproj");

        var source = File.ReadAllText(projectFile);

        Assert.Contains("<ProjectReference Include=\"..\\TianShu.AppHost.State\\TianShu.AppHost.State.csproj\" />", source, StringComparison.Ordinal);
        Assert.Contains("<ProjectReference Include=\"..\\TianShu.AppHost.Tools\\TianShu.AppHost.Tools.csproj\" />", source, StringComparison.Ordinal);
        Assert.Contains("<ProjectReference Include=\"..\\..\\Execution\\TianShu.Execution.Runtime\\TianShu.Execution.Runtime.csproj\" />", source, StringComparison.Ordinal);
        Assert.Contains("<ProjectReference Include=\"..\\..\\Provider\\TianShu.Provider.Abstractions\\TianShu.Provider.Abstractions.csproj\" />", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostToolsAndExecutionRuntimeProjects_ShouldExposeInternalsToRuntimeSibling()
    {
        var repoRoot = FindRepoRoot();
        var appHostToolsProject = File.ReadAllText(Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", "TianShu.AppHost.Tools.csproj"));
        var executionRuntimeProject = File.ReadAllText(Path.Combine(repoRoot, "src", "Execution", "TianShu.Execution.Runtime", "TianShu.Execution.Runtime.csproj"));

        Assert.Contains("<InternalsVisibleTo Include=\"TianShu.AppHost.Tools.Runtime\" />", appHostToolsProject, StringComparison.Ordinal);
        Assert.Contains("<InternalsVisibleTo Include=\"TianShu.AppHost.Tools.Runtime\" />", executionRuntimeProject, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostTestsProject_ShouldOwnKernelMcpManagerResourceTests()
    {
        var repoRoot = FindRepoRoot();
        var appHostProjectFile = Path.Combine(
            repoRoot,
            "tests",
            "TianShu.AppHost.Tests",
            "TianShu.AppHost.Tests.csproj");
        var integrationProjectFile = Path.Combine(
            repoRoot,
            "tests",
            "TianShu.Execution.Integration.Tests",
            "TianShu.Execution.Integration.Tests.csproj");

        var appHostSource = File.ReadAllText(appHostProjectFile);
        var integrationSource = File.ReadAllText(integrationProjectFile);
        Assert.True(File.Exists(Path.Combine(repoRoot, "tests", "TianShu.AppHost.Tests", "Migrated", "KernelMcpManagerResourceTests.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "tests", "TianShu.Execution.Integration.Tests", "Migrated", "KernelMcpManagerResourceTests.cs")));
        Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests\\KernelMcpManagerResourceTests.cs", appHostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests\\KernelMcpManagerResourceTests.cs", integrationSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostConfigurationSources_ShouldUseExpectedConfigurationNamespaces()
    {
        var files = new[]
        {
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Configuration", "TianShuSkillRootPaths.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Configuration", "TianShuProjectRootResolver.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Configuration", "TianShuConfigTomlPathResolver.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Configuration", "KernelConfigReadModels.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Configuration", "KernelConfigRequirementModels.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Configuration", "KernelConfigObjectUtilities.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Configuration", "KernelConfigPersistenceUtilities.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Configuration", "KernelSpawnAgentRoleModels.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Configuration", "KernelInstructionConfigUtilities.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Configuration", "KernelModelProviderConfigUtilities.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Configuration", "KernelPermissionProfileModels.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Configuration", "KernelPermissionProfileResolver.cs"),
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);

            Assert.Contains("namespace TianShu.AppHost.Configuration;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", source, StringComparison.Ordinal);
        }

        var runtimeCompositionFiles = new[]
        {
            Path.Combine(FindRepoRoot(), "src", "Core", "TianShu.RuntimeComposition", "TianShuTomlConfigurationLoader.cs"),
        };

        foreach (var file in runtimeCompositionFiles)
        {
            var source = File.ReadAllText(file);

            Assert.Contains("namespace TianShu.RuntimeComposition;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.AppHost.Configuration;", source, StringComparison.Ordinal);
        }

        var neutralConfigurationFiles = new[]
        {
            Path.Combine(FindRepoRoot(), "src", "Core", "TianShu.Configuration", "ResolvedTianShuConfig.cs"),
            Path.Combine(FindRepoRoot(), "src", "Core", "TianShu.Configuration", "ResolvedTianShuConfigLayer.cs"),
            Path.Combine(FindRepoRoot(), "src", "Core", "TianShu.Configuration", "KernelModelProtocolResolver.cs"),
            Path.Combine(FindRepoRoot(), "src", "Core", "TianShu.Configuration", "TianShuPromptConfigUtilities.cs"),
            Path.Combine(FindRepoRoot(), "src", "Core", "TianShu.Configuration", "TianShuConfigObjectUtilities.cs"),
        };

        foreach (var file in neutralConfigurationFiles)
        {
            var source = File.ReadAllText(file);

            Assert.Contains("namespace TianShu.Configuration;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.AppHost.Configuration;", source, StringComparison.Ordinal);
        }

        var catalogSurfaceFiles = new[]
        {
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Catalog", "KernelCatalogSurfaceUtilities.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Catalog", "KernelCatalogSurfaceAppHostRuntime.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Catalog", "ProviderModelConnectivityProbe.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Catalog", "ProviderSmokeTestPlan.cs"),
        };

        foreach (var file in catalogSurfaceFiles)
        {
            var source = File.ReadAllText(file);

            Assert.Contains("namespace TianShu.AppHost.Catalog;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.AppHost.Configuration;", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KernelConfigAndRequirementsCarrierHelpers_ShouldLiveUnderAppHostConfigurationProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelConfigReadFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.ConfigReadLayers.cs");
        var kernelAppServerFile = GetAppHostServerSourcePath(repoRoot);
        var kernelRequirementsFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Requirements.cs");
        var kernelManagedNetworkFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.ManagedNetwork.cs");
        var configReadModelsFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelConfigReadModels.cs");
        var requirementModelsFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelConfigRequirementModels.cs");
        var configReadHelperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelConfigReadLayerUtilities.cs");
        var configSnapshotHelperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelConfigSnapshotUtilities.cs");
        var tomlTextParsingHelperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelTomlTextParsingUtilities.cs");

        Assert.False(File.Exists(kernelConfigReadFile));
        Assert.True(File.Exists(kernelAppServerFile));
        Assert.False(File.Exists(kernelRequirementsFile));
        Assert.False(File.Exists(kernelManagedNetworkFile));
        Assert.True(File.Exists(configReadModelsFile));
        Assert.True(File.Exists(requirementModelsFile));
        Assert.True(File.Exists(configReadHelperFile));
        Assert.True(File.Exists(configSnapshotHelperFile));
        Assert.True(File.Exists(tomlTextParsingHelperFile));

        var kernelAppServerSource = File.ReadAllText(kernelAppServerFile);
        var kernelRequirementsSource = kernelAppServerSource;
        var kernelManagedNetworkSource = kernelAppServerSource;
        var configReadModelsSource = File.ReadAllText(configReadModelsFile);
        var requirementModelsSource = File.ReadAllText(requirementModelsFile);
        var configReadHelperSource = File.ReadAllText(configReadHelperFile);
        var configSnapshotHelperSource = File.ReadAllText(configSnapshotHelperFile);
        var tomlTextParsingHelperSource = File.ReadAllText(tomlTextParsingHelperFile);

        Assert.Contains("using TianShu.AppHost.Configuration;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, string> MergeConfigValueLayers(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void MergeConfigObjects(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, object?> BuildProjectDocScopedConfig(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool IsProjectConfigReadLayer(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void AssignConfigOrigins(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, object?> BuildConfigObjectFromOverridePayload(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, object?> BuildConfigObjectFromOverrideElement(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, object?> ConvertJsonObjectToConfigDictionary(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object? ConvertJsonElementToConfigValue(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("var orderedLayers = new List<KernelConfigReadLayer>()", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelConfigReadLayer", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelConfigReadSnapshot", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("using TianShu.AppHost.Configuration;", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelMergedConfigRequirements", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelParsedRequirements", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed class KernelRequirementsMergeState", kernelRequirementsSource, StringComparison.Ordinal);

        Assert.Contains("BuildConfigReadSnapshotForRuntime(", kernelManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelManagedNetworkRequirements", kernelManagedNetworkSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", configReadModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelConfigReadLayer", configReadModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelConfigReadSnapshot", configReadModelsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", configReadModelsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", configReadHelperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelConfigReadLayerUtilities", configReadHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, string> MergeConfigValueLayers(", configReadHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static void MergeConfigObjects(", configReadHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, object?> BuildProjectDocScopedConfig(", configReadHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static void AssignConfigOrigins(", configReadHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, object?> BuildConfigObjectFromOverrideElement(", configReadHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, object?> ConvertJsonObjectToConfigDictionary(", configReadHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static object? ConvertJsonElementToConfigValue(", configReadHelperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", configReadHelperSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", configSnapshotHelperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelConfigSnapshotUtilities", configSnapshotHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelConfigReadSnapshot BuildConfigReadSnapshot(", configSnapshotHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelConfigReadSnapshot ApplyRequestConfigOverrides(", configSnapshotHelperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", configSnapshotHelperSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", tomlTextParsingHelperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelTomlTextParsingUtilities", tomlTextParsingHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryParseTopLevelTomlScalar(", tomlTextParsingHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryParseTomlStringArray(", tomlTextParsingHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, bool> ParseTomlBooleanSection(", tomlTextParsingHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, string> ParseTomlSectionRawValues(", tomlTextParsingHelperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", tomlTextParsingHelperSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", requirementModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelManagedNetworkRequirements", requirementModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelMergedConfigRequirements", requirementModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelParsedRequirements", requirementModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelRequirementsMergeState", requirementModelsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", requirementModelsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelRequirementsResolverHelpers_ShouldLiveUnderAppHostConfigurationProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerFile = GetAppHostServerSourcePath(repoRoot);
        var kernelRequirementsFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Requirements.cs");
        var kernelParityFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var pathResolverFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "TianShuConfigTomlPathResolver.cs");
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelConfigRequirementsUtilities.cs");

        Assert.True(File.Exists(kernelAppServerFile));
        Assert.False(File.Exists(kernelRequirementsFile));
        Assert.False(File.Exists(kernelParityFile));
        Assert.True(File.Exists(pathResolverFile));
        Assert.True(File.Exists(helperFile));

        var kernelRequirementsSource = File.ReadAllText(kernelAppServerFile);
        var kernelParitySource = AssertFileDeletedAndReturnEmpty(kernelParityFile);
        var pathResolverSource = File.ReadAllText(pathResolverFile);
        var helperSource = File.ReadAllText(helperFile);

        Assert.Contains("using TianShu.AppHost.Configuration;", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private const string CloudRequirementsTomlEnvironmentVariable", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private const string AdminRequirementsTomlEnvironmentVariable", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelParsedRequirements? ReadRequirementsFromEnvironment", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelParsedRequirements? ReadRequirementsFromFile", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelParsedRequirements? ReadLegacyManagedRequirementsFromFile", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelParsedRequirements ParseRequirementsToml", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, object?> ParseTomlRoot", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static List<string>? ReadOptionalStringArray", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? TryReadOptionalString", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, bool>? ReadOptionalBooleanSection", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelManagedNetworkRequirements? ReadOptionalManagedNetworkRequirements", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool? ReadOptionalBoolean", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static int? ReadOptionalInteger", kernelRequirementsSource, StringComparison.Ordinal);

        Assert.DoesNotContain("private static string ResolveRequirementsPath()", kernelParitySource, StringComparison.Ordinal);

        Assert.Contains("public static string ResolveUserRequirementsTomlPath()", pathResolverSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelConfigRequirementsUtilities", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelMergedConfigRequirements LoadMergedConfigRequirements()", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static object? BuildConfigRequirementsPayload(KernelMergedConfigRequirements requirements)", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static (HashSet<string> AllowedApprovalPolicies, HashSet<string> AllowedSandboxModes) BuildConfigValidationRules(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, bool> BuildRequiredFeatureFlags(KernelMergedConfigRequirements requirements)", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostConfigurationProject_ShouldExposeInternalsToKernelAndToolsForCarrierReuse()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "TianShu.AppHost.Configuration.csproj");

        var source = File.ReadAllText(projectFile);

        Assert.Contains(
            "<InternalsVisibleTo Include=\"TianShu.Execution.Runtime\" />",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "<InternalsVisibleTo Include=\"TianShu.AppHost\" />",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "<InternalsVisibleTo Include=\"TianShu.AppHost.Tools\" />",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "<InternalsVisibleTo Include=\"TianShu.AppHost.Tools.Runtime\" />",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void KernelTomlAndConfigObjectHelpers_ShouldLiveUnderAppHostConfigurationProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelConfigReadFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.ConfigReadLayers.cs");
        var kernelAppServerFile = GetAppHostServerSourcePath(repoRoot);
        var kernelRequirementsFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Requirements.cs");
        var kernelRequirementsHelperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelConfigRequirementsUtilities.cs");
        var kernelAgentRolesFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.AgentRoles.cs");
        var kernelConfigPersistenceFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.ConfigPersistence.cs");
        var roleConfigurationHelperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelSpawnAgentRoleConfigurationUtilities.cs");
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelConfigObjectUtilities.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerFile);
        var kernelRequirementsSource = kernelAppServerSource;
        var kernelRequirementsHelperSource = File.ReadAllText(kernelRequirementsHelperFile);
        var kernelAgentRolesSource = kernelAppServerSource;
        var kernelConfigPersistenceSource = kernelAppServerSource;
        var roleConfigurationHelperSource = File.ReadAllText(roleConfigurationHelperFile);
        var helperSource = File.ReadAllText(helperFile);

        Assert.False(File.Exists(kernelRequirementsFile));
        Assert.False(File.Exists(kernelConfigReadFile));
        Assert.False(File.Exists(kernelAgentRolesFile));
        Assert.False(File.Exists(kernelConfigPersistenceFile));

        Assert.Contains("using TianShu.AppHost.Configuration;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, object?> ReadTomlConfigObject", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, object?> ConvertTomlTableToDictionary", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object? ConvertTomlValue", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, object?> CloneConfigDictionary", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object? CloneConfigValue", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ComputeConfigObjectVersion", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelConfigReadLayer CreateConfigReadLayer", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("using TianShu.AppHost.Configuration;", kernelRequirementsSource, StringComparison.Ordinal);
        Assert.Contains("KernelConfigObjectUtilities.ConvertTomlTableToDictionary", kernelRequirementsHelperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigObjectUtilities.ConvertTomlTableToDictionary", kernelAgentRolesSource, StringComparison.Ordinal);
        Assert.Contains("KernelConfigObjectUtilities.ConvertTomlTableToDictionary", roleConfigurationHelperSource, StringComparison.Ordinal);
        Assert.Contains("using TianShu.AppHost.Configuration;", kernelConfigPersistenceSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelConfigObjectUtilities", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, object?> ReadTomlConfigObject", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, object?> ConvertTomlTableToDictionary", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static object? ConvertTomlValue", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, object?> CloneConfigDictionary", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static object? CloneConfigValue", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string ComputeConfigObjectVersion", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelConfigReadLayer CreateConfigReadLayer", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helperSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", roleConfigurationHelperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelSpawnAgentRoleConfigurationUtilities", roleConfigurationHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyDictionary<string, KernelSpawnAgentRoleDefinition> ResolveSpawnAgentRoleDefinitions(", roleConfigurationHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static async Task<string> BuildSpawnAgentTypeDescriptionAsync(", roleConfigurationHelperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", roleConfigurationHelperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelSpawnAgentRoleCarriers_ShouldLiveUnderAppHostConfigurationProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAgentRolesFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.AgentRoles.cs");
        var kernelAppServerFile = GetAppHostServerSourcePath(repoRoot);
        var roleModelsFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelSpawnAgentRoleModels.cs");

        var kernelAgentRolesSource = File.ReadAllText(kernelAppServerFile);
        var roleModelsSource = File.ReadAllText(roleModelsFile);

        Assert.False(File.Exists(kernelAgentRolesFile));
        Assert.Contains("using TianShu.AppHost.Configuration;", kernelAgentRolesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelSpawnAgentRoleDefinition", kernelAgentRolesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelSpawnAgentRoleOverrides", kernelAgentRolesSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", roleModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelSpawnAgentRoleDefinition", roleModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelSpawnAgentRoleOverrides", roleModelsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", roleModelsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelPermissionProfileLocalPolicyHelpers_ShouldLiveUnderAppHostConfigurationProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelPermissionProfilesFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelPermissionProfiles.cs");
        var kernelAppServerFile = GetAppHostServerSourcePath(repoRoot);
        var kernelManagedNetworkFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.ManagedNetwork.cs");
        var managedNetworkSettingsHelperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelManagedNetworkSettingsUtilities.cs");
        var managedNetworkRuntimeFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelManagedNetworkAppHostRuntime.cs");
        var managedNetworkAppHostHelperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelManagedNetworkAppHostUtilities.cs");
        var jsReplRuntimeHelperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelJsReplRuntimeHelpers.cs");
        var kernelGlobalUsingsFile = GetHostGlobalUsingsPath(repoRoot);
        var permissionProfileModelsFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelPermissionProfileModels.cs");
        var permissionProfileResolverFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelPermissionProfileResolver.cs");

        var kernelPermissionProfilesSource = File.ReadAllText(kernelAppServerFile);
        var kernelManagedNetworkSource = kernelPermissionProfilesSource;
        var managedNetworkSettingsHelperSource = File.ReadAllText(managedNetworkSettingsHelperFile);
        var managedNetworkRuntimeSource = File.ReadAllText(managedNetworkRuntimeFile);
        var managedNetworkAppHostHelperSource = File.ReadAllText(managedNetworkAppHostHelperFile);
        var jsReplRuntimeHelperSource = File.ReadAllText(jsReplRuntimeHelperFile);
        var permissionRuntimeAdapterFile = Path.Combine(
            repoRoot,
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "KernelPermissionRuntimeAdapter.cs");
        Assert.False(File.Exists(kernelPermissionProfilesFile));
        var kernelGlobalUsingsSource = File.ReadAllText(kernelGlobalUsingsFile);
        var permissionProfileModelsSource = File.ReadAllText(permissionProfileModelsFile);
        var permissionProfileResolverSource = File.ReadAllText(permissionProfileResolverFile);
        var permissionRuntimeAdapterSource = File.ReadAllText(permissionRuntimeAdapterFile);

        Assert.False(File.Exists(kernelManagedNetworkFile));

        Assert.Contains("KernelPermissionProfileResolver.ResolveConfiguredPermissionConfiguration", kernelPermissionProfilesSource, StringComparison.Ordinal);
        Assert.Contains("KernelPermissionRuntimeAdapter.CreateResolvedPermissionSettings", kernelPermissionProfilesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly record struct KernelResolvedPermissionSettings", kernelPermissionProfilesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private enum KernelPermissionConfigSyntax", kernelPermissionProfilesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed class KernelCompiledPermissionState", kernelPermissionProfilesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelShellEnvironmentPolicy CreateShellEnvironmentPolicy", kernelPermissionProfilesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelApprovalPolicy? ReadConfiguredApprovalPolicy", kernelPermissionProfilesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryAsDictionary", kernelPermissionProfilesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, object?> ConvertJsonObject", kernelPermissionProfilesSource, StringComparison.Ordinal);

        Assert.DoesNotContain("private static KernelManagedNetworkExecutionLeaseSnapshot CreateManagedNetworkLeaseSnapshot(", kernelManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool IsSandboxPolicyNetworkEnabled(", kernelManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ExtractApprovalDecision(", kernelManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryReadNetworkPolicyAmendment(", kernelManagedNetworkSource, StringComparison.Ordinal);
        Assert.Contains("managedNetworkRuntime.BeginExecutionAsync", kernelManagedNetworkSource, StringComparison.Ordinal);
        Assert.Contains("KernelManagedNetworkSettingsUtilities.ResolveManagedNetworkSettingsWithSkillOverride(", kernelManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private Task<KernelManagedNetworkExecutionLease> BeginManagedNetworkExecutionAsync(", kernelManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private Task<KernelManagedNetworkApprovalResponse> RequestManagedNetworkApprovalAsync(", kernelManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private Task EmitManagedNetworkSideEffectAsync(", kernelManagedNetworkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task ShutdownManagedNetworkSessionsAsync(", kernelManagedNetworkSource, StringComparison.Ordinal);
        Assert.Contains("KernelPermissionProfileResolver.ResolvePermissionConfigSyntax", managedNetworkSettingsHelperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityReaders", kernelGlobalUsingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", kernelGlobalUsingsSource, StringComparison.Ordinal);
        Assert.Contains("global using static TianShu.AppHost.Configuration.KernelConfigReadLayerUtilities;", kernelGlobalUsingsSource, StringComparison.Ordinal);
        Assert.Contains("global using static TianShu.AppHost.Configuration.KernelTomlTextParsingUtilities;", kernelGlobalUsingsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", permissionProfileModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal enum KernelPermissionConfigSyntax", permissionProfileModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelConfiguredShellEnvironmentPolicySettings", permissionProfileModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelResolvedPermissionConfiguration", permissionProfileModelsSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelCompiledPermissionState", permissionProfileModelsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", permissionProfileModelsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", permissionProfileResolverSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelPermissionProfileResolver", permissionProfileResolverSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelResolvedPermissionConfiguration ResolveConfiguredPermissionConfiguration", permissionProfileResolverSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelPermissionConfigSyntax? ResolvePermissionConfigSyntax", permissionProfileResolverSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", permissionProfileResolverSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.Execution.Runtime;", permissionRuntimeAdapterSource, StringComparison.Ordinal);
        Assert.Contains("internal readonly record struct KernelResolvedPermissionRuntimeSettings(", permissionRuntimeAdapterSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelPermissionRuntimeAdapter", permissionRuntimeAdapterSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelResolvedPermissionRuntimeSettings CreateResolvedPermissionSettings(", permissionRuntimeAdapterSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelShellEnvironmentPolicy CreateShellEnvironmentPolicy(", permissionRuntimeAdapterSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", permissionRuntimeAdapterSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", managedNetworkRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelManagedNetworkAppHostRuntime", managedNetworkRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task<KernelManagedNetworkExecutionLease> BeginExecutionAsync(", managedNetworkRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal async Task<KernelManagedNetworkApprovalResponse> RequestManagedNetworkApprovalAsync(", managedNetworkRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal async Task EmitManagedNetworkSideEffectAsync(", managedNetworkRuntimeSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools;", managedNetworkAppHostHelperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelManagedNetworkAppHostUtilities", managedNetworkAppHostHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelManagedNetworkExecutionLeaseSnapshot CreateManagedNetworkLeaseSnapshot(", managedNetworkAppHostHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool IsSandboxPolicyNetworkEnabled(", managedNetworkAppHostHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static string ExtractApprovalDecision(", managedNetworkAppHostHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryReadNetworkPolicyAmendment(", managedNetworkAppHostHelperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", managedNetworkAppHostHelperSource, StringComparison.Ordinal);

        Assert.Contains("using static TianShu.AppHost.Configuration.KernelTomlTextParsingUtilities;", jsReplRuntimeHelperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryParseTopLevelTomlScalar(", jsReplRuntimeHelperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryParseTomlStringArray(", jsReplRuntimeHelperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeCompositionPhysicalMigration_ShouldOwnWorkspaceAndPolicyStrategyComposition()
    {
        var repoRoot = FindRepoRoot();
        var appHostServerFile = GetAppHostServerSourcePath(repoRoot);
        var permissionProfileResolverFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelPermissionProfileResolver.cs");
        var execPolicyManagerFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelExecPolicyManager.cs");
        var tomlConfigurationLoaderFile = Path.Combine(
            repoRoot,
            "src",
            "Core",
            "TianShu.RuntimeComposition",
            "TianShuTomlConfigurationLoader.cs");
        var policyStrategyRuntimeCompositionFile = Path.Combine(
            repoRoot,
            "src",
            "Core",
            "TianShu.RuntimeComposition",
            "PolicyStrategyRuntimeComposition.cs");
        var workspaceResolverRuntimeCompositionFile = Path.Combine(
            repoRoot,
            "src",
            "Core",
            "TianShu.RuntimeComposition",
            "WorkspaceResolverRuntimeComposition.cs");

        var appHostServerSource = File.ReadAllText(appHostServerFile);
        var permissionProfileResolverSource = File.ReadAllText(permissionProfileResolverFile);
        var execPolicyManagerSource = File.ReadAllText(execPolicyManagerFile);
        var tomlConfigurationLoaderSource = File.ReadAllText(tomlConfigurationLoaderFile);
        var policyStrategyRuntimeCompositionSource = File.ReadAllText(policyStrategyRuntimeCompositionFile);
        var workspaceResolverRuntimeCompositionSource = File.ReadAllText(workspaceResolverRuntimeCompositionFile);

        Assert.Contains("PolicyStrategyRuntimeComposition.ResolveEffectivePackage", appHostServerSource, StringComparison.Ordinal);
        Assert.Contains("PolicyStrategyRuntimeComposition.CreateExecPolicyManager", appHostServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PolicyStrategyRuntimeComposition.ResolveEffectiveDefaults", appHostServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PolicyStrategyRuntimeComposition.ResolveEffectiveRules", appHostServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PolicyStrategyRuntimeRules policyStrategyRules", appHostServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PolicyStrategyEffectiveDefaults policyStrategyDefaults", appHostServerSource, StringComparison.Ordinal);
        Assert.Contains("PolicyStrategyCommandRuleValue", execPolicyManagerSource, StringComparison.Ordinal);
        Assert.Contains("PolicyStrategyNetworkRuleValue", execPolicyManagerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TianShuPolicyStrategyManifestConfiguration.ResolveEffectiveDefaults", permissionProfileResolverSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TianShuPolicyStrategyManifestConfiguration.ResolveEffectiveCommandRules", execPolicyManagerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TianShuPolicyStrategyManifestConfiguration.ResolveEffectiveNetworkRules", execPolicyManagerSource, StringComparison.Ordinal);

        Assert.Contains("WorkspaceResolverRuntimeComposition.ResolveEffectivePolicy", tomlConfigurationLoaderSource, StringComparison.Ordinal);
        Assert.Contains("TianShuWorkspaceResolverManifestConfiguration.ResolveEffectivePolicy", workspaceResolverRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("TianShuPolicyStrategyManifestConfiguration.ResolveEffectiveDefaults", policyStrategyRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("TianShuPolicyStrategyManifestConfiguration.ResolveEffectiveCommandRules", policyStrategyRuntimeCompositionSource, StringComparison.Ordinal);
        Assert.Contains("TianShuPolicyStrategyManifestConfiguration.ResolveEffectiveNetworkRules", policyStrategyRuntimeCompositionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelPersistedConfigTomlHelpers_ShouldLiveUnderAppHostConfigurationProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelConfigPersistenceFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.ConfigPersistence.cs");
        var kernelAppServerFile = GetAppHostServerSourcePath(repoRoot);
        var kernelParityFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var kernelGlobalUsingsFile = GetHostGlobalUsingsPath(repoRoot);
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelConfigPersistenceUtilities.cs");
        var orchestrationHelperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelConfigPersistenceOrchestrationUtilities.cs");

        var kernelConfigPersistenceSource = File.ReadAllText(kernelAppServerFile);
        var kernelParitySource = AssertFileDeletedAndReturnEmpty(kernelParityFile);
        var kernelGlobalUsingsSource = File.ReadAllText(kernelGlobalUsingsFile);
        var helperSource = File.ReadAllText(helperFile);
        var orchestrationHelperSource = File.ReadAllText(orchestrationHelperFile);

        Assert.False(File.Exists(kernelConfigPersistenceFile));
        Assert.Contains("using TianShu.AppHost.Configuration;", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void MergePersistedConfigTable", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static TomlTable ReadPersistedConfigTable", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void FlattenPersistedConfigValue", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ConvertTomlScalarToJson", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static List<object?> ConvertTomlArrayToList", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object? ConvertTomlValueToClr", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static JsonElement ParsePersistedJsonValue", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object ConvertJsonElementToTomlValue", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static TomlArray ConvertJsonArrayToTomlArray", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static TomlTable ConvertJsonObjectToTomlTable", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void SetTomlPathValue", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void RemoveTomlPathValue", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static TomlTable GetOrCreateTomlTable", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static List<string> SplitConfigKeyPath", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string CanonicalizePersistedConfigKeyPath", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ResolvePersistedConfigTomlPath", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TianShuConfigTomlPathResolver.EnumerateProjectConfigPaths(cwd)", kernelConfigPersistenceSource, StringComparison.Ordinal);

        Assert.DoesNotContain("using TianShu.AppHost.Configuration;", kernelParitySource, StringComparison.Ordinal);

        Assert.Contains("global using static TianShu.AppHost.Configuration.KernelConfigPersistenceUtilities;", kernelGlobalUsingsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelConfigPersistenceUtilities", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static void MergePersistedConfigTable", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static TomlTable ReadPersistedConfigTable", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static void FlattenPersistedConfigValue", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string ConvertTomlScalarToJson", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static object ConvertJsonElementToTomlValue", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static void SetTomlPathValue", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static List<string> SplitConfigKeyPath", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string CanonicalizePersistedConfigKeyPath", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string ResolvePersistedConfigTomlPath", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helperSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", orchestrationHelperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelConfigPersistenceOrchestrationUtilities", orchestrationHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, string> ReadMergedPersistedConfigValues(", orchestrationHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static TomlTable ReadMergedPersistedConfigTable(", orchestrationHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static string? ReadMergedPersistedConfigText(", orchestrationHelperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", orchestrationHelperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelConfigWriteHelpers_ShouldLiveUnderAppHostConfigurationProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelParityFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var kernelGlobalUsingsFile = GetHostGlobalUsingsPath(repoRoot);
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelConfigWriteUtilities.cs");
        var requirementsHelperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelConfigRequirementsUtilities.cs");
        var runtimeFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelConfigSurfaceAppHostRuntime.cs");
        var tianShuPathHelperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "TianShuHomePathUtilities.cs");

        var kernelParitySource = AssertFileDeletedAndReturnEmpty(kernelParityFile);
        var kernelGlobalUsingsSource = File.ReadAllText(kernelGlobalUsingsFile);
        var helperSource = File.ReadAllText(helperFile);
        var requirementsHelperSource = File.ReadAllText(requirementsHelperFile);
        var runtimeSource = File.ReadAllText(runtimeFile);
        var tianShuPathHelperSource = File.ReadAllText(tianShuPathHelperFile);

        Assert.DoesNotContain("MutatePersistedConfigTableAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryValidateConfigEdit(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void ValidateMutatedConfigTable(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryParseConfigJsonValue(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, object?> BuildConfigObject(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? TryGetRawProperty(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static List<KernelConfigWriteItem> ExtractBatchConfigItems(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static async Task WriteTextAtomicallyAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryApplyConfigWriteValue(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? NormalizeConfigMergeStrategy(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private object? ComputeConfigWriteOverriddenMetadata(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool ConfigValuesEqual(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelConfigWriteItem(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private string ResolveCliConfigOverrideBaseDirectory(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<(HashSet<string> AllowedApprovalPolicies, HashSet<string> AllowedSandboxModes)> LoadConfigValidationRulesAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, bool> LoadRequiredFeatureFlagsSynchronously(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ResolveTianShuHomePath(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ResolveConfigWritePath(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ResolveConfigWriteTargetPath(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<KernelConfigWriteMutationResult> MutatePersistedConfigTableAsync(", kernelParitySource, StringComparison.Ordinal);

        Assert.Contains("global using static TianShu.AppHost.Configuration.TianShuHomePathUtilities;", kernelGlobalUsingsSource, StringComparison.Ordinal);
        Assert.Contains("global using static TianShu.AppHost.Configuration.KernelConfigWriteUtilities;", kernelGlobalUsingsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelConfigWriteUtilities", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string ResolveCliConfigOverrideBaseDirectory", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string ResolveConfigWritePath", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string ResolveConfigWriteTargetPath", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryValidateConfigEdit", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, object?> BuildConfigObject", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static List<KernelConfigWriteItem> ExtractBatchConfigItems", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static async Task<KernelConfigWriteMutationResult> MutatePersistedConfigTableAsync", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryApplyConfigWriteValue", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static object? ComputeConfigWriteOverriddenMetadata", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelConfigWriteItem", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelConfigWriteMutationResult", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelConfigWriteException", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helperSource, StringComparison.Ordinal);

        Assert.Contains("public static (HashSet<string> AllowedApprovalPolicies, HashSet<string> AllowedSandboxModes) LoadConfigValidationRules", requirementsHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, bool> LoadRequiredFeatureFlags", requirementsHelperSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelConfigSurfaceAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelConfigWriteUtilities.MutatePersistedConfigTableAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelConfigWriteUtilities.ResolveConfigWritePath(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelConfigRequirementsUtilities.LoadConfigValidationRules(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", runtimeSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", tianShuPathHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static class TianShuHomePathUtilities", tianShuPathHelperSource, StringComparison.Ordinal);
        Assert.Contains("public static string ResolveTianShuHomePath()", tianShuPathHelperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", tianShuPathHelperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelConfigOverrideUtilities_ShouldLiveUnderAppHostConfigurationProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelParityFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var kernelGlobalUsingsFile = GetHostGlobalUsingsPath(repoRoot);
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelConfigOverrideUtilities.cs");

        var kernelParitySource = AssertFileDeletedAndReturnEmpty(kernelParityFile);
        var kernelGlobalUsingsSource = File.ReadAllText(kernelGlobalUsingsFile);
        var helperSource = File.ReadAllText(helperFile);

        Assert.DoesNotContain("private static string ConvertRawOverrideToJson(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string RebaseCliConfigOverrideRawValue(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object? RebaseCliConfigOverrideJsonElement(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool ShouldRebaseCliConfigOverridePath(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string RebaseCliRelativePath(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", kernelGlobalUsingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityReaders", kernelGlobalUsingsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelConfigOverrideUtilities", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string ConvertRawOverrideToJson", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string RebaseCliConfigOverrideRawValue", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static object? RebaseCliConfigOverrideJsonElement", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool ShouldRebaseCliConfigOverridePath", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string RebaseCliRelativePath", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadTomlSectionNames", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadCollaborationModesFromToml", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TryParseScopedConfigKey", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelCollaborationModeMask", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelStoragePaths_ShouldUseSharedHomePathHelper()
    {
        var repoRoot = FindRepoRoot();
        var storagePathsFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.State",
            "KernelStoragePaths.cs");
        var storagePathsSource = File.ReadAllText(storagePathsFile);

        Assert.Contains("using TianShu.Configuration;", storagePathsSource, StringComparison.Ordinal);
        Assert.Contains("TianShuHomePathUtilities.ResolveTianShuStateRootPath()", storagePathsSource, StringComparison.Ordinal);
        Assert.Contains("TianShuHomePathUtilities.ResolveTianShuSessionsRootPath()", storagePathsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.GetEnvironmentVariable(\"TIANSHU_KERNEL_HOME\")", storagePathsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.GetEnvironmentVariable(\"TIANSHU_HOME\")", storagePathsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelInstructionAndModelProviderConfigHelpers_ShouldLiveUnderAppHostConfigurationProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerFile = GetAppHostServerSourcePath(repoRoot);
        var kernelAgentRolesFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.AgentRoles.cs");
        var kernelGlobalUsingsFile = GetHostGlobalUsingsPath(repoRoot);
        var instructionUtilitiesFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelInstructionConfigUtilities.cs");
        var modelProviderUtilitiesFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelModelProviderConfigUtilities.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerFile);
        var kernelAgentRolesSource = kernelAppServerSource;
        var kernelGlobalUsingsSource = File.ReadAllText(kernelGlobalUsingsFile);
        var instructionUtilitiesSource = File.ReadAllText(instructionUtilitiesFile);
        var modelProviderUtilitiesSource = File.ReadAllText(modelProviderUtilitiesFile);

        Assert.False(File.Exists(kernelAgentRolesFile));
        Assert.DoesNotContain("private static bool HasExperimentalInstructionsFile", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool ContainsExperimentalInstructionsFile", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? LoadInstructionFileIfConfigured", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ResolveConfiguredInstructionFilePath", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ResolveConfiguredInstructionBaseDirectory", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? ReadConfiguredStringWithActiveProfile", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? ReadConfiguredNestedStringWithActiveProfile", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool? ReadConfiguredNestedBooleanWithActiveProfile", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? ReadConfiguredModelProviderSetting", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static int? ReadConfiguredModelProviderInt", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static long? ReadConfiguredModelProviderLong", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool? ReadConfiguredModelProviderBoolean", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("private static KernelServiceTier? ReadConfiguredServiceTier", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("ReadConfiguredServiceTierValue(config, propertyNames)", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("using TianShu.AppHost.Configuration;", kernelAgentRolesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveConfiguredInstructionFilePath(", kernelAgentRolesSource, StringComparison.Ordinal);

        Assert.Contains("global using static TianShu.AppHost.Configuration.KernelInstructionConfigUtilities;", kernelGlobalUsingsSource, StringComparison.Ordinal);
        Assert.Contains("global using static TianShu.AppHost.Configuration.KernelModelProviderConfigUtilities;", kernelGlobalUsingsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", instructionUtilitiesSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelInstructionConfigUtilities", instructionUtilitiesSource, StringComparison.Ordinal);
        Assert.Contains("public static bool HasExperimentalInstructionsFile", instructionUtilitiesSource, StringComparison.Ordinal);
        Assert.Contains("public static string? LoadInstructionFileIfConfigured", instructionUtilitiesSource, StringComparison.Ordinal);
        Assert.Contains("public static string ResolveConfiguredInstructionFilePath", instructionUtilitiesSource, StringComparison.Ordinal);
        Assert.Contains("public static string? ReadConfiguredStringWithActiveProfile", instructionUtilitiesSource, StringComparison.Ordinal);
        Assert.Contains("public static string? ReadConfiguredNestedStringWithActiveProfile", instructionUtilitiesSource, StringComparison.Ordinal);
        Assert.Contains("public static bool? ReadConfiguredNestedBooleanWithActiveProfile", instructionUtilitiesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", instructionUtilitiesSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", modelProviderUtilitiesSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelModelProviderConfigUtilities", modelProviderUtilitiesSource, StringComparison.Ordinal);
        Assert.Contains("public static string? ReadConfiguredServiceTierValue", modelProviderUtilitiesSource, StringComparison.Ordinal);
        Assert.Contains("public static string? ReadConfiguredModelProviderSetting", modelProviderUtilitiesSource, StringComparison.Ordinal);
        Assert.Contains("public static int? ReadConfiguredModelProviderInt", modelProviderUtilitiesSource, StringComparison.Ordinal);
        Assert.Contains("public static long? ReadConfiguredModelProviderLong", modelProviderUtilitiesSource, StringComparison.Ordinal);
        Assert.Contains("public static bool? ReadConfiguredModelProviderBoolean", modelProviderUtilitiesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", modelProviderUtilitiesSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostServerTransport_ShouldLiveUnderAppHostProject()
    {
        var repoRoot = FindRepoRoot();
        var oldTransportFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "KernelAppServerTransport.cs");
        var newTransportFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "AppHostServerTransport.cs");

        Assert.False(File.Exists(oldTransportFile));
        Assert.True(File.Exists(newTransportFile));
    }

    [Fact]
    public void AppHostTransportSource_ShouldUseAppHostNamespace()
    {
        var transportFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost",
            "AppHostServerTransport.cs");

        var source = File.ReadAllText(transportFile);

        Assert.Contains("namespace TianShu.AppHost;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostMcpServer_ShouldLiveUnderAppHostProject()
    {
        var repoRoot = FindRepoRoot();
        var oldMcpServerFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelMcpServer.cs");
        var newMcpServerFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "AppHostMcpServer.cs");

        Assert.False(File.Exists(oldMcpServerFile));
        Assert.True(File.Exists(newMcpServerFile));
    }

    [Fact]
    public void AppHostMcpServerSource_ShouldUseAppHostNamespace()
    {
        var sourceFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost",
            "AppHostMcpServer.cs");

        var source = File.ReadAllText(sourceFile);

        Assert.Contains("namespace TianShu.AppHost;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", source, StringComparison.Ordinal);
        Assert.Contains("using TianShu.AppHost.State;", source, StringComparison.Ordinal);
        Assert.Contains("using TianShu.AppHost.Tools.Runtime;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostTestsProject_ShouldOwnAppHostMcpServerTests()
    {
        var repoRoot = FindRepoRoot();
        var appHostProjectFile = Path.Combine(
            repoRoot,
            "tests",
            "TianShu.AppHost.Tests",
            "TianShu.AppHost.Tests.csproj");
        var integrationProjectFile = Path.Combine(
            repoRoot,
            "tests",
            "TianShu.Execution.Integration.Tests",
            "TianShu.Execution.Integration.Tests.csproj");

        var appHostSource = File.ReadAllText(appHostProjectFile);
        var integrationSource = File.ReadAllText(integrationProjectFile);
        Assert.True(File.Exists(Path.Combine(repoRoot, "tests", "TianShu.AppHost.Tests", "Migrated", "KernelMcpServerTests.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "tests", "TianShu.Execution.Integration.Tests", "Migrated", "KernelMcpServerTests.cs")));
        Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests\\KernelMcpServerTests.cs", appHostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests\\KernelMcpServerTests.cs", integrationSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostTestsProject_ShouldOwnAppHostMcpServerSurfaceTests()
    {
        var repoRoot = FindRepoRoot();
        var appHostProjectFile = Path.Combine(
            repoRoot,
            "tests",
            "TianShu.AppHost.Tests",
            "TianShu.AppHost.Tests.csproj");
        var integrationProjectFile = Path.Combine(
            repoRoot,
            "tests",
            "TianShu.Execution.Integration.Tests",
            "TianShu.Execution.Integration.Tests.csproj");

        var appHostSource = File.ReadAllText(appHostProjectFile);
        var integrationSource = File.ReadAllText(integrationProjectFile);
        Assert.True(File.Exists(Path.Combine(repoRoot, "tests", "TianShu.AppHost.Tests", "Migrated", "KernelAppServerMcpServerSurfaceTests.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "tests", "TianShu.Execution.Integration.Tests", "Migrated", "KernelAppServerMcpServerSurfaceTests.cs")));
        Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests\\KernelAppServerMcpServerSurfaceTests.cs", appHostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests\\KernelAppServerMcpServerSurfaceTests.cs", integrationSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelHostSideUtilityPrimitives_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var fileNames = new[]
        {
            "KernelAppCatalogUtilities.cs",
            "KernelAsyncReadWriteLock.cs",
            "KernelGlobalNotificationHub.cs",
            "KernelInstructionScopeUtilities.cs",
            "McpServerAuthUtilities.cs",
            "KernelPersistedSkillConfigUtilities.cs",
            "KernelQueuePair.cs",
            "KernelReadinessFlag.cs",
            "KernelReviewAppHostUtilities.cs",
        };

        foreach (var fileName in fileNames)
        {
            var oldPath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", fileName);
            var newPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", fileName);

            Assert.False(File.Exists(oldPath), $"旧文件不应继续存在: {oldPath}");
            Assert.True(File.Exists(newPath), $"缺少已归位文件: {newPath}");
        }
    }

    [Fact]
    public void AppHostToolsUtilitySources_ShouldUseAppHostToolsNamespace()
    {
        var repoRoot = FindRepoRoot();
        var fileNames = new[]
        {
            "KernelAppCatalogUtilities.cs",
            "KernelAsyncReadWriteLock.cs",
            "KernelGlobalNotificationHub.cs",
            "McpServerAuthUtilities.cs",
            "KernelPersistedSkillConfigUtilities.cs",
            "KernelQueuePair.cs",
            "KernelReadinessFlag.cs",
            "KernelReviewAppHostUtilities.cs",
        };

        foreach (var fileName in fileNames)
        {
            var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools", fileName));
            Assert.Contains("namespace TianShu.AppHost.Tools;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KernelPersistedSkillConfigCompatibilityHelpers_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelConfigPersistenceFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.ConfigPersistence.cs");
        var kernelAppServerFile = GetAppHostServerSourcePath(repoRoot);
        var kernelParityFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var skillsManagerFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelSkillsManager.cs");
        var kernelGlobalUsingsFile = GetHostGlobalUsingsPath(repoRoot);
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelPersistedSkillConfigUtilities.cs");

        var kernelConfigPersistenceSource = File.ReadAllText(kernelAppServerFile);
        var kernelParitySource = AssertFileDeletedAndReturnEmpty(kernelParityFile);
        var skillsManagerSource = File.ReadAllText(skillsManagerFile);
        var kernelGlobalUsingsSource = File.ReadAllText(kernelGlobalUsingsFile);
        var helperSource = File.ReadAllText(helperFile);

        Assert.False(File.Exists(kernelConfigPersistenceFile));
        Assert.DoesNotContain("private static Dictionary<string, string> ReadPersistedConfigValues(", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void MergePersistedConfigValues(", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, string> FlattenPersistedConfigTable(", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void FlattenSkillsConfig(", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void FlattenSkillConfigEntry(", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? ResolvePersistedSkillConfigPath(", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void ApplyPersistedConfigValues(", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void ApplyPersistedConfigValue(", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryApplySpecialPersistedConfigValue(", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static TomlTableArray GetOrCreateTomlTableArray(", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void CleanupSkillsConfig(", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void RemoveLegacySkillState(", kernelConfigPersistenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void CleanupEmptyTianShuState(", kernelConfigPersistenceSource, StringComparison.Ordinal);

        Assert.DoesNotContain("private static string ToSkillEnabledConfigKey(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ToSkillEnabledConfigKey(", skillsManagerSource, StringComparison.Ordinal);
        Assert.Contains("KernelPersistedSkillConfigUtilities.ToSkillEnabledConfigKey(", skillsManagerSource, StringComparison.Ordinal);

        Assert.Contains("global using static TianShu.AppHost.Tools.KernelPersistedSkillConfigUtilities;", kernelGlobalUsingsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools;", helperSource, StringComparison.Ordinal);
        Assert.Contains("using static TianShu.AppHost.Configuration.KernelConfigPersistenceUtilities;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelPersistedSkillConfigUtilities", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, string> ReadPersistedConfigValues(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static void MergePersistedConfigValues(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, string> FlattenPersistedConfigTable(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static void ApplyPersistedConfigValues(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static void ApplyPersistedConfigValue(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string ToSkillEnabledConfigKey(", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelReviewHelpers_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelParityFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var kernelParitySurfaceFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.Surface.cs");
        var runtimeFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelTurnReviewSurfaceAppHostRuntime.cs");
        var kernelGlobalUsingsFile = GetHostGlobalUsingsPath(repoRoot);
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelReviewAppHostUtilities.cs");

        var kernelParitySource = AssertFileDeletedAndReturnEmpty(kernelParityFile);
        var runtimeSource = File.ReadAllText(runtimeFile);
        var kernelGlobalUsingsSource = File.ReadAllText(kernelGlobalUsingsFile);
        var helperSource = File.ReadAllText(helperFile);

        Assert.DoesNotContain("private async Task<string?> ResolveDetachedReviewModelAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object[] BuildReviewTurnItems(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<string> EnrichReviewPromptWithTargetContextAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<string?> CaptureReviewTargetContextAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<string?> CaptureUncommittedReviewContextAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<string?> RunGitCommandForReviewAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string TrimReviewContext(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryBuildReviewPrompt(", kernelParitySource, StringComparison.Ordinal);

        Assert.False(File.Exists(kernelParitySurfaceFile));

        Assert.Contains("EnrichReviewPromptWithTargetContextAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("ResolveDetachedReviewModelAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("BuildReviewTurnItems(", runtimeSource, StringComparison.Ordinal);

        Assert.Contains("global using static TianShu.AppHost.Tools.KernelReviewAppHostUtilities;", kernelGlobalUsingsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelReviewCommandResult", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelReviewAppHostUtilities", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static async Task<string?> ResolveDetachedReviewModelAsync(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static object[] BuildReviewTurnItems(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static async Task<string> EnrichReviewPromptWithTargetContextAsync(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static async Task<string?> CaptureReviewTargetContextAsync(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static async Task<string?> CaptureUncommittedReviewContextAsync(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static async Task<string?> RunGitCommandForReviewAsync(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string TrimReviewContext(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryBuildReviewPrompt(", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostMcpServerAuthHelpers_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelParityFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var kernelParitySurfaceFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.Surface.cs");
        var runtimeFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "McpServerSurfaceAppHostRuntime.cs");
        var kernelGlobalUsingsFile = GetHostGlobalUsingsPath(repoRoot);
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "McpServerAuthUtilities.cs");

        var kernelParitySource = AssertFileDeletedAndReturnEmpty(kernelParityFile);
        var runtimeSource = File.ReadAllText(runtimeFile);
        var kernelGlobalUsingsSource = File.ReadAllText(kernelGlobalUsingsFile);
        var helperSource = File.ReadAllText(helperFile);

        Assert.DoesNotContain("private async Task<List<string>> ListMcpServerNamesAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<string?> ResolveMcpServerAuthorizationUrlAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? TryReadMcpServerUrlFromToml(", kernelParitySource, StringComparison.Ordinal);

        Assert.False(File.Exists(kernelParitySurfaceFile));

        Assert.Contains("McpServerAuthUtilities.ListMcpServerNamesAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("McpServerAuthUtilities.ResolveMcpServerAuthorizationUrlAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("McpServerAuthUtilities.ResolveMcpServerAuthStatus(", runtimeSource, StringComparison.Ordinal);

        Assert.Contains("global using static TianShu.AppHost.Tools.McpServerAuthUtilities;", kernelGlobalUsingsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class McpServerAuthUtilities", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static async Task<List<string>> ListMcpServerNamesAsync(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static async Task<string?> ResolveMcpServerAuthorizationUrlAsync(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string? TryReadMcpServerUrlFromToml(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string ResolveMcpServerAuthStatus(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool HasConfiguredValue(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string? NormalizeRawConfigValue(", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelAppCatalogHelpers_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelParityFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var kernelPluginsFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Plugins.cs");
        var kernelGlobalUsingsFile = GetHostGlobalUsingsPath(repoRoot);
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelAppCatalogUtilities.cs");

        var kernelParitySource = AssertFileDeletedAndReturnEmpty(kernelParityFile);
        var kernelGlobalUsingsSource = File.ReadAllText(kernelGlobalUsingsFile);
        var helperSource = File.ReadAllText(helperFile);

        Assert.False(File.Exists(kernelPluginsFile));
        Assert.DoesNotContain("private async Task<List<KernelAppListItem>> LoadAppsAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<KernelAppConfigState> LoadAppConfigStateAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static List<KernelAppListItem> BuildAppsFromConfig(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static List<KernelAppListItem> BuildAppsFromConnectors(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelAppListItem CreateAppListItem(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static IReadOnlyList<string> ResolvePluginDisplayNames(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool ShouldSendAppListUpdatedNotification(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool AppListsEqual(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelAppConfigState(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelAppListItem(", kernelParitySource, StringComparison.Ordinal);

        Assert.Contains("global using static TianShu.AppHost.Tools.KernelAppCatalogUtilities;", kernelGlobalUsingsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelAppCatalogUtilities", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelAppConfigState BuildAppConfigState(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static List<ControlPlaneAppDescriptor> BuildAppsFromConfig(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static List<ControlPlaneAppDescriptor> BuildAppsFromConnectors(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelPluginConnectorInfo MergeConnector(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool ShouldSendAppListUpdatedNotification(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool AppListsEqual(", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelAppConfigState(", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelAppListItem(", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelPluginConnectorInfo(", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelFuzzyFileSearchHelpers_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelParityFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var kernelGlobalUsingsFile = GetHostGlobalUsingsPath(repoRoot);
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelFuzzyFileSearchUtilities.cs");

        var kernelParitySource = AssertFileDeletedAndReturnEmpty(kernelParityFile);
        var kernelGlobalUsingsSource = File.ReadAllText(kernelGlobalUsingsFile);
        var helperSource = File.ReadAllText(helperFile);

        Assert.DoesNotContain("private static object[] SearchFilesAcrossRoots(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static double ComputeFileMatchScore(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static uint[]? ComputeMatchIndices(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelFuzzyFileSearchSession(", kernelParitySource, StringComparison.Ordinal);

        Assert.Contains("global using static TianShu.AppHost.Tools.KernelFuzzyFileSearchUtilities;", kernelGlobalUsingsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelFuzzyFileSearchUtilities", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<string> NormalizeFuzzyFileSearchRoots(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelFuzzyFileSearchSession CreateFuzzyFileSearchSession(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelFuzzyFileSearchSession UpdateFuzzyFileSearchSessionQuery(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<KernelFuzzyFileSearchMatch> SearchFilesAcrossRoots(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static double ComputeFileMatchScore(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static uint[]? ComputeMatchIndices(", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelFuzzyFileSearchSession(", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelFuzzyFileSearchMatch(", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelConversationSummaryHelpers_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelParityFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var kernelGlobalUsingsFile = GetHostGlobalUsingsPath(repoRoot);
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelConversationSummaryUtilities.cs");

        var kernelParitySource = AssertFileDeletedAndReturnEmpty(kernelParityFile);
        var kernelGlobalUsingsSource = File.ReadAllText(kernelGlobalUsingsFile);
        var helperSource = File.ReadAllText(helperFile);

        Assert.DoesNotContain("private static string ReadRolloutPreview(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object BuildConversationSummaryPayload(", kernelParitySource, StringComparison.Ordinal);

        Assert.Contains("global using static TianShu.AppHost.Tools.KernelConversationSummaryUtilities;", kernelGlobalUsingsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelConversationSummaryUtilities", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string ReadRolloutPreview(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static KernelConversationSummaryPayload BuildConversationSummaryPayload(", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelConversationSummaryPayload(", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelConversationSummaryGitInfoPayload(", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelJsonInputHelpers_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelParityFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelToolJsonHelpers.cs");

        var kernelParitySource = AssertFileDeletedAndReturnEmpty(kernelParityFile);
        var helperSource = File.ReadAllText(helperFile);

        Assert.DoesNotContain("private static List<string> ReadStringArray(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryReadInputArray(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Dictionary<string, string[]> TryReadExtraSkillRoots(", kernelParitySource, StringComparison.Ordinal);

        Assert.Contains("public static List<string> ReadStringArray(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryReadInputArray(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, string[]> TryReadExtraSkillRoots(", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelParityDeadCode_ShouldBeRemoved()
    {
        var repoRoot = FindRepoRoot();
        var kernelParityFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var kernelParitySurfaceFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.Surface.cs");

        var kernelParitySource = AssertFileDeletedAndReturnEmpty(kernelParityFile);
        Assert.DoesNotContain("private static List<string> BuildShellCommandArguments(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelSkillsScanResult ScanSkills(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string? TryReadSkillDescription(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelSkillsScanResult(", kernelParitySource, StringComparison.Ordinal);
        Assert.False(File.Exists(kernelParitySurfaceFile));
    }

    [Fact]
    public void KernelCatalogSurfaceHelpers_ShouldLiveUnderAppHostCatalogProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerFile = GetAppHostServerSourcePath(repoRoot);
        var kernelParityFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var kernelGlobalUsingsFile = GetHostGlobalUsingsPath(repoRoot);
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Catalog",
            "KernelCatalogSurfaceUtilities.cs");
        var runtimeFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Catalog",
            "KernelCatalogSurfaceAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerFile);
        var kernelParitySource = AssertFileDeletedAndReturnEmpty(kernelParityFile);
        var kernelGlobalUsingsSource = File.ReadAllText(kernelGlobalUsingsFile);
        var helperSource = File.ReadAllText(helperFile);
        var runtimeSource = File.ReadAllText(runtimeFile);

        Assert.Contains("new KernelCatalogSurfaceAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("catalogSurfaceAppHostRuntime.HandleModelListAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("catalogSurfaceAppHostRuntime.HandleExperimentalFeatureListAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("catalogSurfaceAppHostRuntime.HandleCollaborationModeListAsync(id, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleModelListAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleExperimentalFeatureListAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleCollaborationModeListAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static IReadOnlyList<KernelModelDescriptor> GetBuiltInModels()", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object ToModelPayload(KernelModelDescriptor model)", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static IReadOnlyList<KernelExperimentalFeatureDescriptor> GetExperimentalFeatureDescriptors()", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelExperimentalFeatureDescriptor CreateUnifiedExecExperimentalFeatureDescriptor()", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelExperimentalFeatureDescriptor CreatePowershellUtf8ExperimentalFeatureDescriptor()", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelExperimentalFeatureDescriptor CreatePreventIdleSleepExperimentalFeatureDescriptor()", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static object ToExperimentalFeaturePayload(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool ResolveFeatureEnabledState(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static IReadOnlyList<KernelCollaborationModeMask> BuildCollaborationModeMasks()", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelModelDescriptor(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelReasoningEffortDescriptor(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record KernelExperimentalFeatureDescriptor(", kernelParitySource, StringComparison.Ordinal);

        Assert.Contains("global using TianShu.AppHost.Catalog;", kernelGlobalUsingsSource, StringComparison.Ordinal);
        Assert.Contains("global using static TianShu.AppHost.Catalog.KernelCatalogSurfaceUtilities;", kernelGlobalUsingsSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Catalog;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelCatalogSurfaceUtilities", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<ControlPlaneModelCatalogItem> GetBuiltInModels()", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryGetBuiltInModel(string? model, out ControlPlaneModelCatalogItem? descriptor)", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<string> GetBuiltInModelNames()", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string GetDefaultReasoningEffort(string? model)", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool SupportsReasoningSummaries(string? model)", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string? GetDefaultReasoningSummary(string? model)", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool SupportsVerbosity(string? model)", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string? GetDefaultVerbosity(string? model)", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string GetBaseInstructions(string? model)", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static object ToModelPayload(ControlPlaneModelCatalogItem model)", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<ControlPlaneExperimentalFeatureDescriptor> GetExperimentalFeatureDescriptors()", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static object ToExperimentalFeaturePayload(ControlPlaneExperimentalFeatureDescriptor descriptor, bool enabled)", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool ResolveFeatureEnabledState(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<ControlPlaneCollaborationModeDescriptor> BuildCollaborationModeMasks()", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static object ToCollaborationModePayload(ControlPlaneCollaborationModeDescriptor descriptor)", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelModelDescriptor(", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelReasoningEffortDescriptor(", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelExperimentalFeatureDescriptor(", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helperSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Catalog;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelCatalogSurfaceAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleModelListAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleExperimentalFeatureListAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleCollaborationModeListAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelCatalogSurfaceUtilities.GetBuiltInModels()", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelCatalogSurfaceUtilities.GetExperimentalFeatureDescriptors()", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelCatalogSurfaceUtilities.BuildCollaborationModeMasks()", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelCatalogSurfaceUtilities.ToCollaborationModePayload", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelConfigSurfaceAppHostRuntime_ShouldLiveUnderAppHostConfigurationProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerFile = GetAppHostServerSourcePath(repoRoot);
        var kernelParityFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.Parity.cs");
        var runtimeFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelConfigSurfaceAppHostRuntime.cs");

        var kernelAppServerSource = File.ReadAllText(kernelAppServerFile);
        var kernelParitySource = AssertFileDeletedAndReturnEmpty(kernelParityFile);
        var runtimeSource = File.ReadAllText(runtimeFile);

        Assert.Contains("new KernelConfigSurfaceAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("configSurfaceAppHostRuntime.HandleConfigReadAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("configSurfaceAppHostRuntime.HandleConfigValueWriteAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("configSurfaceAppHostRuntime.HandleConfigBatchWriteAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("configSurfaceAppHostRuntime.HandleConfigRequirementsReadAsync(id, @params, cancellationToken)", kernelAppServerSource, StringComparison.Ordinal);

        Assert.DoesNotContain("HandleConfigReadAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleConfigValueWriteAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleConfigBatchWriteAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleConfigRequirementsReadAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveConfigWritePath(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("MutatePersistedConfigTableAsync(", kernelParitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("ComputeConfigWriteOverriddenMetadata(", kernelParitySource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Configuration;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelConfigSurfaceAppHostRuntime", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleConfigReadAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleConfigValueWriteAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleConfigBatchWriteAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleConfigRequirementsReadAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelConfigWriteUtilities.ResolveConfigWritePath(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelConfigWriteUtilities.MutatePersistedConfigTableAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("KernelConfigRequirementsUtilities.BuildConfigRequirementsPayload(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostProject_ShouldReferenceAppHostToolsForTransportUtilities()
    {
        var repoRoot = FindRepoRoot();
        var projectSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost", "TianShu.AppHost.csproj"));
        var transportHostSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost", "AppHostWebSocketTransportHost.cs"));

        Assert.Contains("<ProjectReference Include=\"..\\TianShu.AppHost.Tools\\TianShu.AppHost.Tools.csproj\" />", projectSource, StringComparison.Ordinal);
        Assert.Contains("using TianShu.AppHost.Tools;", transportHostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostTestsProject_ShouldOwnHostSideUtilityPrimitiveTests()
    {
        var repoRoot = FindRepoRoot();
        var appHostProjectSource = File.ReadAllText(Path.Combine(repoRoot, "tests", "TianShu.AppHost.Tests", "TianShu.AppHost.Tests.csproj"));
        var executionRuntimeProjectSource = File.ReadAllText(Path.Combine(repoRoot, "tests", "TianShu.Execution.Runtime.Tests", "TianShu.Execution.Runtime.Tests.csproj"));
        var testFiles = new[]
        {
            "KernelAsyncReadWriteLockTests.cs",
            "KernelQueuePairTests.cs",
            "KernelReadinessFlagTests.cs",
            "KernelToolCallGateTests.cs",
        };

        foreach (var testFile in testFiles)
        {
            Assert.True(File.Exists(Path.Combine(repoRoot, "tests", "TianShu.AppHost.Tests", "Migrated", testFile)));
            Assert.False(File.Exists(Path.Combine(repoRoot, "tests", "TianShu.Execution.Runtime.Tests", "Migrated", "KernelTests", testFile)));
            Assert.DoesNotContain($"src\\Infrastructure\\TianShu.Kernel.Tests\\{testFile}", appHostProjectSource, StringComparison.Ordinal);
            Assert.DoesNotContain($"src\\Infrastructure\\TianShu.Kernel.Tests\\{testFile}", executionRuntimeProjectSource, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KernelFsWatchRuntime_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelFileSystemFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.FileSystem.cs");
        var fileSystemRuntimeFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelFileSystemAppHostRuntime.cs");
        var fsWatchRuntimeFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelFsWatchRuntime.cs");

        Assert.False(File.Exists(kernelFileSystemFile));
        Assert.True(File.Exists(fileSystemRuntimeFile));
        Assert.True(File.Exists(fsWatchRuntimeFile));

        var fileSystemRuntimeSource = File.ReadAllText(fileSystemRuntimeFile);
        var runtimeSource = File.ReadAllText(fsWatchRuntimeFile);

        Assert.Contains("private readonly KernelFsWatchManager fsWatchManager = new();", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record KernelFsWatchHandle", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed class KernelFsWatchManager", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed class KernelFsWatchRegistration", fileSystemRuntimeSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools;", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record KernelFsWatchHandle", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelFsWatchManager", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelFsWatchRegistration", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelFileSystemHelpers_ShouldLiveUnderAppHostToolsProject()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerFile = GetAppHostServerSourcePath(repoRoot);
        var kernelFileSystemFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel", "AppServer", "KernelAppServer.FileSystem.cs");
        var fileSystemRuntimeFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelFileSystemAppHostRuntime.cs");
        var helperFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelFileSystemUtilities.cs");

        Assert.True(File.Exists(kernelAppServerFile));
        Assert.False(File.Exists(kernelFileSystemFile));
        Assert.True(File.Exists(fileSystemRuntimeFile));
        Assert.True(File.Exists(helperFile));

        var kernelAppServerSource = File.ReadAllText(kernelAppServerFile);
        var fileSystemRuntimeSource = File.ReadAllText(fileSystemRuntimeFile);
        var helperSource = File.ReadAllText(helperFile);

        Assert.Contains("KernelFileSystemUtilities.ResolveInitializePlatformFamily()", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("KernelFileSystemUtilities.ResolveInitializePlatformOs()", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("private readonly KernelFileSystemAppHostRuntime fileSystemAppHostRuntime;", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("new KernelFileSystemAppHostRuntime(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await fileSystemAppHostRuntime.DisposeAsync().ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await fileSystemAppHostRuntime.HandleFsReadFileAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await fileSystemAppHostRuntime.HandleFsWriteFileAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await fileSystemAppHostRuntime.HandleFsCreateDirectoryAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await fileSystemAppHostRuntime.HandleFsGetMetadataAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await fileSystemAppHostRuntime.HandleFsReadDirectoryAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await fileSystemAppHostRuntime.HandleFsRemoveAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await fileSystemAppHostRuntime.HandleFsCopyAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await fileSystemAppHostRuntime.HandleFsWatchAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await fileSystemAppHostRuntime.HandleFsUnwatchAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await fileSystemAppHostRuntime.HandleFuzzyFileSearchLegacyAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await fileSystemAppHostRuntime.HandleFuzzyFileSearchSessionStartAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await fileSystemAppHostRuntime.HandleFuzzyFileSearchSessionUpdateAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("await fileSystemAppHostRuntime.HandleFuzzyFileSearchSessionStopAsync(id, @params, cancellationToken).ConfigureAwait(false);", kernelAppServerSource, StringComparison.Ordinal);

        Assert.Contains("using static TianShu.AppHost.Tools.KernelFileSystemUtilities;", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("using static TianShu.AppHost.Tools.KernelFuzzyFileSearchUtilities;", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("namespace TianShu.AppHost.Tools.Runtime;", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class KernelFileSystemAppHostRuntime", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleFsReadFileAsync(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleFsWriteFileAsync(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleFsCreateDirectoryAsync(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleFsGetMetadataAsync(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleFsReadDirectoryAsync(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleFsRemoveAsync(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleFsCopyAsync(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleFsWatchAsync(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleFsUnwatchAsync(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleFuzzyFileSearchLegacyAsync(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleFuzzyFileSearchSessionStartAsync(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleFuzzyFileSearchSessionUpdateAsync(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("public async Task HandleFuzzyFileSearchSessionStopAsync(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ResolveInitializePlatformFamily()", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string ResolveInitializePlatformOs()", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryReadRequiredAbsolutePath(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static long GetFileSystemTimeUtc(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static async Task CopyFileSystemEntryAsync(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static async Task CopyDirectoryRecursiveAsync(", fileSystemRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool IsSameOrDescendantPath(", fileSystemRuntimeSource, StringComparison.Ordinal);

        Assert.Contains("namespace TianShu.AppHost.Tools;", helperSource, StringComparison.Ordinal);
        Assert.Contains("internal static class KernelFileSystemUtilities", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string ResolveInitializePlatformFamily()", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static string ResolveInitializePlatformOs()", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryReadRequiredAbsolutePath(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static long GetFileSystemTimeUtc(", helperSource, StringComparison.Ordinal);
        Assert.Contains("public static async Task CopyFileSystemEntryAsync(", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostProject_ShouldExposeInternalsToAppHostTests_AndDropLegacyKernelTestsFriendAssembly()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost",
            "TianShu.AppHost.csproj");

        var source = File.ReadAllText(projectFile);

        Assert.Contains(
            "<InternalsVisibleTo Include=\"TianShu.AppHost.Tests\" />",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<InternalsVisibleTo Include=\"TianShu.Kernel.Tests\" />",
            source,
            StringComparison.Ordinal);
    }

    private static string ReadKernelThreadLifecycleFacadeSource(string repoRoot)
    {
        var threadLifecyclePath = Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Kernel", "AppServer", "KernelAppServer.ThreadLifecycle.cs");
        if (File.Exists(threadLifecyclePath))
        {
            return File.ReadAllText(threadLifecyclePath);
        }

        var kernelAppServerPath = GetAppHostServerSourcePath(repoRoot);
        return File.ReadAllText(kernelAppServerPath);
    }

    private static string AssertFileDeletedAndReturnEmpty(string filePath)
    {
        Assert.False(File.Exists(filePath), $"旧文件不应继续存在: {filePath}");
        return string.Empty;
    }

    private static string GetAppHostServerSourcePath(string repoRoot)
    {
        return Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "AppHostServer.cs");
    }

    private static string GetHostGlobalUsingsPath(string repoRoot)
    {
        return Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "GlobalUsings.cs");
    }

    private static string GetDeletedKernelProjectFilePath(string repoRoot)
    {
        return Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "TianShu.Kernel.csproj");
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
