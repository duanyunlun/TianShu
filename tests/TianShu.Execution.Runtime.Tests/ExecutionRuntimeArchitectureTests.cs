using System.IO;

namespace TianShu.Execution.Runtime.Tests;

public sealed class ExecutionRuntimeArchitectureTests
{
    [Fact]
    public void PrimarySolution_ShouldIncludeExecutionRuntimeTestsProject()
    {
        var solutionFile = Path.Combine(FindRepoRoot(), "TianShu.sln");
        var source = File.ReadAllText(solutionFile);

        Assert.Contains(
            "\"TianShu.Execution.Runtime.Tests\", \"tests\\TianShu.Execution.Runtime.Tests\\TianShu.Execution.Runtime.Tests.csproj\"",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PrimarySolution_ShouldIncludeExecutionIntegrationTestsProject()
    {
        var solutionFile = Path.Combine(FindRepoRoot(), "TianShu.sln");
        var source = File.ReadAllText(solutionFile);

        Assert.Contains(
            "\"TianShu.Execution.Integration.Tests\", \"tests\\TianShu.Execution.Integration.Tests\\TianShu.Execution.Integration.Tests.csproj\"",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PrimarySolution_ShouldIncludeAppHostAndProviderSplitTestProjects()
    {
        var solutionFile = Path.Combine(FindRepoRoot(), "TianShu.sln");
        var source = File.ReadAllText(solutionFile);

        Assert.Contains(
            "\"TianShu.AppHost.Tests\", \"tests\\TianShu.AppHost.Tests\\TianShu.AppHost.Tests.csproj\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"TianShu.Provider.OpenAI.Tests\", \"tests\\TianShu.Provider.OpenAI.Tests\\TianShu.Provider.OpenAI.Tests.csproj\"",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PrimarySolution_ShouldNotRetainKernelTestsProject()
    {
        var primarySolution = File.ReadAllText(Path.Combine(FindRepoRoot(), "TianShu.sln"));

        Assert.DoesNotContain(
            "\"TianShu.Kernel.Tests\", \"src\\Infrastructure\\TianShu.Kernel.Tests\\TianShu.Kernel.Tests.csproj\"",
            primarySolution,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AgentRuntimeTests_Project_ShouldNotRetainExecutionRuntimeArchitectureLock()
    {
        var oldFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Infrastructure",
            "TianShu.AgentRuntime.Tests",
            "ExecutionRuntimeArchitectureTests.cs");

        Assert.False(File.Exists(oldFile));
    }

    [Fact]
    public void ControlPlaneClientInterfaces_ShouldLiveUnderExecutionRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var oldRuntimeDirectory = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.AgentRuntime",
            "Runtime");
        var newRuntimeDirectory = Path.Combine(
            repoRoot,
            "src",
            "Execution",
            "TianShu.Execution.Runtime");
        var fileNames = new[]
        {
            "IExecutionRuntime.cs",
            "ISessionControlPlaneClient.cs",
            "IConversationControlPlaneClient.cs",
            "IWorkflowControlPlaneClient.cs",
            "IAgentControlPlaneClient.cs",
            "IGovernanceControlPlaneClient.cs",
            "ICatalogControlPlaneClient.cs",
            "IDiagnosticsControlPlaneClient.cs",
            "IArtifactControlPlaneClient.cs",
        };

        foreach (var fileName in fileNames)
        {
            Assert.False(
                File.Exists(Path.Combine(oldRuntimeDirectory, fileName)),
                $"旧 runtime 接口文件仍存在：{fileName}");
            Assert.True(
                File.Exists(Path.Combine(newRuntimeDirectory, fileName)),
                $"新 runtime 接口文件缺失：{fileName}");
        }
    }

    [Fact]
    public void ClosedSiblingControlPlaneInterfaces_ShouldNotRetainDefaultNotSupportedFallbacks()
    {
        var runtimeDirectory = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime");
        var fileNames = new[]
        {
            "ICollaborationControlPlaneClient.cs",
            "ISessionControlPlaneClient.cs",
            "IIdentityControlPlaneClient.cs",
            "IMemoryControlPlaneClient.cs",
        };

        foreach (var fileName in fileNames)
        {
            var source = File.ReadAllText(Path.Combine(runtimeDirectory, fileName));
            Assert.DoesNotContain("Task.FromException<", source, StringComparison.Ordinal);
            Assert.DoesNotContain("当前运行时尚未实现", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RuntimeControlPlaneAdapter_ShouldLiveUnderExecutionRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var oldControlPlaneDirectory = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.AgentRuntime",
            "Runtime",
            "ControlPlane");
        var newControlPlaneDirectory = Path.Combine(
            repoRoot,
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "ControlPlane");
        var fileNames = new[]
        {
            "RuntimeControlPlaneAdapter.cs",
            "RuntimeControlPlaneAdapter.Agents.cs",
            "RuntimeControlPlaneAdapter.Artifacts.cs",
            "RuntimeControlPlaneAdapter.Catalog.cs",
            "RuntimeControlPlaneAdapter.Diagnostics.cs",
            "RuntimeControlPlaneAdapter.Mapping.cs",
            "RuntimeControlPlaneAdapter.Subscriptions.cs",
            "RuntimeControlPlaneAdapterExtensions.cs",
        };

        foreach (var fileName in fileNames)
        {
            Assert.False(
                File.Exists(Path.Combine(oldControlPlaneDirectory, fileName)),
                $"旧 control-plane adapter 文件仍存在：{fileName}");
            Assert.True(
                File.Exists(Path.Combine(newControlPlaneDirectory, fileName)),
                $"新 control-plane adapter 文件缺失：{fileName}");
        }
    }

    [Fact]
    public void RuntimeControlPlaneAdapterExtensions_ShouldExposeRuntimeSpecificEntryPointOnly()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "ControlPlane",
            "RuntimeControlPlaneAdapterExtensions.cs"));

        Assert.Contains("AsRuntimeControlPlane(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public static ITianShuControlPlane AsControlPlane(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutionRuntimeControlPlaneSources_ShouldNotRetainAgentRuntimeControlPlaneNaming()
    {
        var controlPlaneDirectory = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "ControlPlane");

        var offenders = Directory.EnumerateFiles(controlPlaneDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("AgentRuntimeControlPlane", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"execution runtime control-plane adapter 不应继续保留 AgentRuntimeControlPlane 旧命名；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void ExecutionRuntimeSources_ShouldNotRetainIAgentRuntimeNaming()
    {
        var runtimeDirectory = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime");

        var offenders = Directory.EnumerateFiles(runtimeDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return content.Contains("IAgentRuntimeDiagnostics", StringComparison.Ordinal)
                    || content.Contains("IAgentRuntime", StringComparison.Ordinal);
            })
            .Select(file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"execution runtime 根接口源码不应继续保留 IAgentRuntime 旧命名；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void ExecutionRuntimeSources_ShouldNotRetainAgentRuntimeOptionsNaming()
    {
        var runtimeDirectory = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime");

        var offenders = Directory.EnumerateFiles(runtimeDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("AgentRuntimeOptions", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"execution runtime 源码不应继续保留 AgentRuntimeOptions 旧命名；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void TianShuExecutionRuntimeCoreFiles_ShouldLiveUnderExecutionRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var oldProjectRoot = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.AgentRuntime");
        var newProjectRoot = Path.Combine(
            repoRoot,
            "src",
            "Execution",
            "TianShu.Execution.Runtime");
        var fileMappings = new (string OldRelativePath, string NewRelativePath)[]
        {
            ("Runtime\\TianShuExecutionRuntime.cs", "TianShuExecutionRuntime.cs"),
            ("Runtime\\TianShuExecutionRuntime.ControlPlaneConversationMapping.cs", "TianShuExecutionRuntime.ControlPlaneConversationMapping.cs"),
            ("Runtime\\IAgentRuntimeDiagnostics.cs", "IExecutionRuntimeDiagnostics.cs"),
            ("Runtime\\AgentSendResult.cs", "AgentSendResult.cs"),
            ("Runtime\\AppServerRpcException.cs", "AppServerRpcException.cs"),
            ("Runtime\\AgentRuntimeOptions.cs", "ExecutionRuntimeOptions.cs"),
            ("Runtime\\AgentRuntimeProviderCatalogResolver.cs", "ExecutionProviderCatalogResolver.cs"),
            ("Runtime\\AgentRuntimeKernelLaunchLocator.cs", "RuntimeHostLaunchLocator.cs"),
            ("Runtime\\AgentStructuredValue.cs", "AgentStructuredValue.cs"),
            ("Runtime\\ApprovalResponse.cs", "ApprovalResponse.cs"),
            ("Runtime\\PermissionGrantResponse.cs", "PermissionGrantResponse.cs"),
            ("Runtime\\UserInputSubmission.cs", "UserInputSubmission.cs"),
            ("Runtime\\AgentThreadModels.cs", "AgentThreadModels.cs"),
            ("Runtime\\AgentThreadProtocolTypes.cs", "AgentThreadProtocolTypes.cs"),
            ("Models\\ConversationMessage.cs", "Models\\ConversationMessage.cs"),
            ("Runtime\\Providers\\IProviderExecutionEventProjector.cs", "Providers\\IProviderExecutionEventProjector.cs"),
            ("Runtime\\Providers\\ProviderEventProjectionContext.cs", "Providers\\ProviderEventProjectionContext.cs"),
            ("Runtime\\Providers\\ProviderExecutionEventProjector.cs", "Providers\\ProviderExecutionEventProjector.cs"),
            ("Runtime\\Events\\AgentEventPayloads.cs", "Events\\AgentEventPayloads.cs"),
            ("Runtime\\Events\\AgentStreamEvent.cs", "Events\\AgentStreamEvent.cs"),
            ("Runtime\\Events\\AgentStreamEvent.ControlPlaneCompatibility.cs", "Events\\AgentStreamEvent.ControlPlaneCompatibility.cs"),
            ("Runtime\\Diagnostics\\DiagnosticsJsonAccessAllowedAttribute.cs", "Diagnostics\\DiagnosticsJsonAccessAllowedAttribute.cs"),
        };

        foreach (var (oldRelativePath, newRelativePath) in fileMappings)
        {
            Assert.False(
                File.Exists(Path.Combine(oldProjectRoot, oldRelativePath)),
                $"旧 runtime core 文件仍存在：{oldRelativePath}");
            Assert.True(
                File.Exists(Path.Combine(newProjectRoot, newRelativePath)),
                $"新 Execution.Runtime 文件缺失：{newRelativePath}");
        }
    }

    [Fact]
    public void AgentRuntimeShellProject_ShouldBeRemoved()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Infrastructure",
            "TianShu.AgentRuntime",
            "TianShu.AgentRuntime.csproj");

        Assert.False(File.Exists(projectFile));
    }

    [Fact]
    public void ExecutionRuntimeProject_ShouldReferenceProtocolAndProviderBoundaries()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShu.Execution.Runtime.csproj");
        var source = File.ReadAllText(projectFile);

        Assert.Contains(
            "<InternalsVisibleTo Include=\"TianShu.Execution.Integration.Tests\" />",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<InternalsVisibleTo Include=\"TianShu.AgentRuntime.Tests\" />",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "<ProjectReference Include=\"..\\..\\Contracts\\TianShu.Contracts.Provider\\TianShu.Contracts.Provider.csproj\" />",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "<ProjectReference Include=\"..\\..\\Execution\\TianShu.Execution.Protocol\\TianShu.Execution.Protocol.csproj\" />",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "<ProjectReference Include=\"..\\..\\Provider\\TianShu.Provider.Abstractions\\TianShu.Provider.Abstractions.csproj\" />",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AgentRuntimeTestsProject_ShouldDeclareNeutralExecutionIntegrationAssemblyName()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "tests",
            "TianShu.Execution.Integration.Tests",
            "TianShu.Execution.Integration.Tests.csproj");
        var source = File.ReadAllText(projectFile);

        Assert.Contains(
            "<AssemblyName>TianShu.Execution.Integration.Tests</AssemblyName>",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutionRuntimeSources_ShouldUseExecutionRuntimeNamespaces()
    {
        var runtimeDirectory = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime");
        var sourceFiles = Directory
            .EnumerateFiles(runtimeDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        var offenders = sourceFiles
            .Where(static file =>
            {
                var source = File.ReadAllText(file);
                return source.Contains("namespace TianShu.AgentRuntime.Runtime", StringComparison.Ordinal)
                    || source.Contains("namespace TianShu.AgentRuntime.Models", StringComparison.Ordinal)
                    || source.Contains("using TianShu.AgentRuntime.Runtime", StringComparison.Ordinal)
                    || source.Contains("using TianShu.AgentRuntime.Models", StringComparison.Ordinal);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .OrderBy(static file => file, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void KernelThreadSessionCoreFiles_ShouldLiveUnderExecutionRuntimeProject()
    {
        var repoRoot = FindRepoRoot();
        var oldProjectRoot = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer");
        var newProjectRoot = Path.Combine(
            repoRoot,
            "src",
            "Execution",
            "TianShu.Execution.Runtime");
        var fileMappings = new (string OldRelativePath, string NewRelativePath)[]
        {
            ("KernelDependencyEnvironmentScope.cs", "KernelDependencyEnvironmentScope.cs"),
            ("KernelShellEnvironmentPolicy.cs", "KernelShellEnvironmentPolicy.cs"),
            ("KernelDynamicToolResolver.cs", "KernelDynamicToolResolver.cs"),
            ("KernelSessionSource.cs", "KernelSessionSource.cs"),
            ("KernelPendingInputState.cs", "KernelPendingInputState.cs"),
            ("KernelConversationHistoryUtilities.cs", "KernelConversationHistoryUtilities.cs"),
            ("KernelThreadTransportOverrides.cs", "KernelThreadTransportOverrides.cs"),
            ("KernelThreadProtocolTypes.cs", "KernelThreadProtocolTypes.cs"),
            ("KernelThreadConfigSnapshot.cs", "KernelThreadConfigSnapshot.cs"),
            ("KernelThreadSessionBuilder.cs", "KernelThreadSessionBuilder.cs"),
            ("KernelThreadRuntime.cs", "KernelThreadRuntime.cs"),
            ("KernelFileWatcher.cs", "KernelFileWatcher.cs"),
        };

        foreach (var (oldRelativePath, newRelativePath) in fileMappings)
        {
            Assert.False(
                File.Exists(Path.Combine(oldProjectRoot, oldRelativePath)),
                $"旧 thread session core 文件仍存在：{oldRelativePath}");
            Assert.True(
                File.Exists(Path.Combine(newProjectRoot, newRelativePath)),
                $"新 Execution.Runtime 文件缺失：{newRelativePath}");
        }

        Assert.True(
            File.Exists(Path.Combine(newProjectRoot, "KernelThreadSessionState.cs")),
            "新 session state 文件缺失：KernelThreadSessionState.cs");
        Assert.True(
            File.Exists(Path.Combine(newProjectRoot, "KernelThreadStateModels.cs")),
            "新 thread state model 文件缺失：KernelThreadStateModels.cs");
    }

    [Fact]
    public void LegacyKernelProject_ShouldBeDeleted_AfterThreadSessionCoreMigration()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "TianShu.Kernel.csproj");

        Assert.False(File.Exists(projectFile));
    }

    [Fact]
    public void KernelLegacyThreadRuntimeSources_ShouldNotRetainMovedThreadSessionDefinitions()
    {
        var repoRoot = FindRepoRoot();
        var appServerRoot = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer");
        var legacyThreadStoreFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelThreadStore.cs");
        var appHostThreadStoreFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.State",
            "KernelThreadStore.cs");
        var threadStoreSource = File.ReadAllText(appHostThreadStoreFile);
        var remainingKernelSources = Directory.Exists(appServerRoot)
            ? Directory
                .EnumerateFiles(appServerRoot, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText)
                .ToArray()
            : [];

        Assert.False(File.Exists(Path.Combine(appServerRoot, "KernelThreadRuntime.cs")));
        Assert.False(File.Exists(Path.Combine(appServerRoot, "KernelFileWatcher.cs")));
        Assert.False(File.Exists(legacyThreadStoreFile));
        Assert.True(File.Exists(appHostThreadStoreFile));

        Assert.DoesNotContain(
            remainingKernelSources,
            static source => source.Contains("internal enum KernelRealtimeEventParser", StringComparison.Ordinal));
        Assert.DoesNotContain(
            remainingKernelSources,
            static source => source.Contains("internal enum KernelRealtimeSessionMode", StringComparison.Ordinal));
        Assert.DoesNotContain(
            remainingKernelSources,
            static source => source.Contains("internal sealed record KernelRealtimeTranscriptEntry", StringComparison.Ordinal));
        Assert.DoesNotContain(
            remainingKernelSources,
            static source => source.Contains("internal sealed class KernelRealtimeSessionState", StringComparison.Ordinal));
        Assert.DoesNotContain(
            remainingKernelSources,
            static source => source.Contains("internal sealed class KernelRuntimeThread", StringComparison.Ordinal));
        Assert.DoesNotContain(
            remainingKernelSources,
            static source => source.Contains("internal sealed class KernelThreadManager", StringComparison.Ordinal));
        Assert.DoesNotContain(
            remainingKernelSources,
            static source => source.Contains("internal enum KernelFileWatcherEventKind", StringComparison.Ordinal));
        Assert.DoesNotContain(
            remainingKernelSources,
            static source => source.Contains("internal sealed record KernelFileWatcherEvent", StringComparison.Ordinal));
        Assert.DoesNotContain(
            remainingKernelSources,
            static source => source.Contains("internal sealed class KernelWatchRegistration", StringComparison.Ordinal));
        Assert.DoesNotContain(
            remainingKernelSources,
            static source => source.Contains("internal sealed class KernelFileWatcher", StringComparison.Ordinal));
        Assert.DoesNotContain("internal sealed class KernelThreadRecord", threadStoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed class KernelConversationHistoryItem", threadStoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed class KernelGitInfoRecord", threadStoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed class KernelTurnRecord", threadStoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed class KernelTurnItemRecord", threadStoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed class KernelTurnErrorRecord", threadStoreSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutionRuntimeTestsProject_ShouldNotRetainKernelThreadStoreTests()
    {
        var repoRoot = FindRepoRoot();
        var projectFile = Path.Combine(
            repoRoot,
            "tests",
            "TianShu.Execution.Runtime.Tests",
            "TianShu.Execution.Runtime.Tests.csproj");
        var source = File.ReadAllText(projectFile);
        var runtimeMigratedFile = Path.Combine(
            repoRoot,
            "tests",
            "TianShu.Execution.Runtime.Tests",
            "Migrated",
            "KernelTests",
            "KernelThreadStoreTests.cs");

        Assert.False(File.Exists(runtimeMigratedFile));
        Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests\\KernelThreadStoreTests.cs", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutionRuntimeHomePathResolution_ShouldUseSharedCoreConfigurationHelper()
    {
        var repoRoot = FindRepoRoot();
        var runtimeFile = Path.Combine(
            repoRoot,
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.cs");
        var optionsFile = Path.Combine(
            repoRoot,
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "ExecutionRuntimeOptions.cs");
        var watcherFile = Path.Combine(
            repoRoot,
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "KernelFileWatcher.cs");

        var runtimeSource = File.ReadAllText(runtimeFile);
        var optionsSource = File.ReadAllText(optionsFile);
        var watcherSource = File.ReadAllText(watcherFile);

        Assert.Contains("using TianShu.Contracts.Configuration;", optionsSource, StringComparison.Ordinal);
        Assert.Contains("TianShuRuntimeLayoutPaths.ResolveTianShuConfigFilePath()", optionsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.GetEnvironmentVariable(\"TIANSHU_HOME\")", optionsSource, StringComparison.Ordinal);

        Assert.Contains("TianShuHomePathUtilities.ResolveTianShuStateRootPath()", runtimeSource, StringComparison.Ordinal);

        Assert.Contains("using TianShu.Configuration;", watcherSource, StringComparison.Ordinal);
        Assert.Contains("TianShuHomePathUtilities.ResolveTianShuHomePath()", watcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly string tianShuHome;", watcherSource, StringComparison.Ordinal);
    }

    [Fact]
    public void MigratedTestProjects_ShouldNotReferenceLegacyKernelTestsSourceDirectory()
    {
        var repoRoot = FindRepoRoot();
        var projectFiles = new[]
        {
            Path.Combine(repoRoot, "tests", "TianShu.AppHost.Tests", "TianShu.AppHost.Tests.csproj"),
            Path.Combine(repoRoot, "tests", "TianShu.Execution.Runtime.Tests", "TianShu.Execution.Runtime.Tests.csproj"),
            Path.Combine(repoRoot, "tests", "TianShu.Execution.Integration.Tests", "TianShu.Execution.Integration.Tests.csproj"),
            Path.Combine(repoRoot, "tests", "TianShu.Provider.OpenAI.Tests", "TianShu.Provider.OpenAI.Tests.csproj"),
        };

        foreach (var projectFile in projectFiles)
        {
            var source = File.ReadAllText(projectFile);
            Assert.DoesNotContain("src\\Infrastructure\\TianShu.Kernel.Tests", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ExecutionRuntimeProject_ShouldDropLegacyKernelTestsFriendAssembly()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShu.Execution.Runtime.csproj");
        var source = File.ReadAllText(projectFile);

        Assert.DoesNotContain(
            "<InternalsVisibleTo Include=\"TianShu.Kernel.Tests\" />",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutionRuntimeProject_ShouldNotReferenceHostingLayerProjects()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShu.Execution.Runtime.csproj");
        var source = File.ReadAllText(projectFile);

        Assert.DoesNotContain("..\\..\\Hosting\\", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TianShu.AppHost.Configuration.csproj", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TianShu.AppHost.Tools.csproj", source, StringComparison.Ordinal);
        Assert.Contains("..\\..\\Core\\TianShu.Configuration\\TianShu.Configuration.csproj", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutionRuntimeSources_ShouldNotLoadMemoryProviderConfigurationDirectly()
    {
        var runtimeDirectory = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime");
        var offenders = Directory
            .EnumerateFiles(runtimeDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(static file => File.ReadAllText(file).Contains("TianShuMemoryProviderConfigurationLoader", StringComparison.Ordinal))
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .OrderBy(static file => file, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Execution.Runtime 不应直接解析 memory provider 配置；正式装配入口应在 AppHost composition root。当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliProject_ShouldReferenceExecutionRuntimeAndAppHostConfiguration_InsteadOfAgentRuntimeShell()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "TianShu.Cli.csproj");
        var source = File.ReadAllText(projectFile);

        Assert.DoesNotContain("..\\..\\Infrastructure\\TianShu.AgentRuntime\\TianShu.AgentRuntime.csproj", source, StringComparison.Ordinal);
        Assert.Contains("..\\..\\Execution\\TianShu.Execution.Runtime\\TianShu.Execution.Runtime.csproj", source, StringComparison.Ordinal);
        Assert.Contains("..\\..\\Hosting\\TianShu.AppHost.Configuration\\TianShu.AppHost.Configuration.csproj", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SidecarProject_ShouldReferenceExecutionRuntimeAndAppHostConfiguration_InsteadOfAgentRuntimeShell()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Presentations",
            "TianShu.VSSDK.Sidecar",
            "TianShu.VSSDK.Sidecar.csproj");
        var source = File.ReadAllText(projectFile);

        Assert.DoesNotContain("..\\..\\Infrastructure\\TianShu.AgentRuntime\\TianShu.AgentRuntime.csproj", source, StringComparison.Ordinal);
        Assert.Contains("..\\..\\Execution\\TianShu.Execution.Runtime\\TianShu.Execution.Runtime.csproj", source, StringComparison.Ordinal);
        Assert.Contains("..\\..\\Hosting\\TianShu.AppHost.Configuration\\TianShu.AppHost.Configuration.csproj", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzerTestsProject_ShouldReferenceExecutionRuntime_InsteadOfAgentRuntimeShell()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "tests",
            "TianShu.ArchitectureAnalyzers.Tests",
            "TianShu.ArchitectureAnalyzers.Tests.csproj");
        var source = File.ReadAllText(projectFile);

        Assert.DoesNotContain("..\\TianShu.AgentRuntime\\TianShu.AgentRuntime.csproj", source, StringComparison.Ordinal);
        Assert.Contains("..\\..\\src\\Execution\\TianShu.Execution.Runtime\\TianShu.Execution.Runtime.csproj", source, StringComparison.Ordinal);
        Assert.Contains("..\\..\\src\\Analyzers\\TianShu.ArchitectureAnalyzers\\TianShu.ArchitectureAnalyzers.csproj", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzerProjects_ShouldKeepProductionCodeUnderAnalyzers_AndTestsUnderTests()
    {
        var repoRoot = FindRepoRoot();
        var oldFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Analyzers", "TianShu.Analyzers.csproj"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Analyzers", "AgentStreamEventDiagnosticsJsonAccessAnalyzer.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Analyzers.Tests", "TianShu.Analyzers.Tests.csproj"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Analyzers.Tests", "AgentStreamEventDiagnosticsJsonAccessAnalyzerTests.cs"),
            Path.Combine(repoRoot, "src", "Tooling", "TianShu.ArchitectureAnalyzers.Tests", "TianShu.ArchitectureAnalyzers.Tests.csproj"),
            Path.Combine(repoRoot, "src", "Tooling", "TianShu.ArchitectureAnalyzers.Tests", "AgentStreamEventDiagnosticsJsonAccessAnalyzerTests.cs"),
            Path.Combine(repoRoot, "src", "Tooling", "TianShu.ArchitectureAnalyzers", "TianShu.ArchitectureAnalyzers.csproj"),
            Path.Combine(repoRoot, "src", "Tooling", "TianShu.ArchitectureAnalyzers", "AgentStreamEventDiagnosticsJsonAccessAnalyzer.cs"),
        };
        var newFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Analyzers", "TianShu.ArchitectureAnalyzers", "TianShu.ArchitectureAnalyzers.csproj"),
            Path.Combine(repoRoot, "src", "Analyzers", "TianShu.ArchitectureAnalyzers", "AgentStreamEventDiagnosticsJsonAccessAnalyzer.cs"),
            Path.Combine(repoRoot, "tests", "TianShu.ArchitectureAnalyzers.Tests", "TianShu.ArchitectureAnalyzers.Tests.csproj"),
            Path.Combine(repoRoot, "tests", "TianShu.ArchitectureAnalyzers.Tests", "AgentStreamEventDiagnosticsJsonAccessAnalyzerTests.cs"),
        };

        Assert.All(oldFiles, static path => Assert.False(File.Exists(path), $"旧 analyzer 文件仍存在：{path}"));
        Assert.All(newFiles, static path => Assert.True(File.Exists(path), $"新 analyzer 文件缺失：{path}"));
    }

    [Fact]
    public void PresentationProjects_ShouldReferenceArchitectureAnalyzers_FromAnalyzers()
    {
        var repoRoot = FindRepoRoot();
        var projectFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "TianShu.Cli.csproj"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar", "TianShu.VSSDK.Sidecar.csproj"),
        };

        foreach (var projectFile in projectFiles)
        {
            var source = File.ReadAllText(projectFile);
            Assert.DoesNotContain("..\\..\\Infrastructure\\TianShu.Analyzers\\TianShu.Analyzers.csproj", source, StringComparison.Ordinal);
            Assert.Contains("..\\..\\Analyzers\\TianShu.ArchitectureAnalyzers\\TianShu.ArchitectureAnalyzers.csproj", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Solutions_ShouldUseArchitectureAnalyzers_ProjectIdentity()
    {
        var repoRoot = FindRepoRoot();
        var primarySolution = File.ReadAllText(Path.Combine(repoRoot, "TianShu.sln"));

        Assert.Contains(
            "\"TianShu.ArchitectureAnalyzers\", \"src\\Analyzers\\TianShu.ArchitectureAnalyzers\\TianShu.ArchitectureAnalyzers.csproj\"",
            primarySolution,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"TianShu.ArchitectureAnalyzers.Tests\", \"tests\\TianShu.ArchitectureAnalyzers.Tests\\TianShu.ArchitectureAnalyzers.Tests.csproj\"",
            primarySolution,
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(repoRoot, "TianShu.Deperated.sln")));
    }

    [Fact]
    public void AgentRuntimeTestsProject_ShouldReferenceExecutionRuntimeAndAppHostConfiguration_InsteadOfAgentRuntimeShell()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "tests",
            "TianShu.Execution.Integration.Tests",
            "TianShu.Execution.Integration.Tests.csproj");
        var source = File.ReadAllText(projectFile);

        Assert.DoesNotContain("..\\TianShu.AgentRuntime\\TianShu.AgentRuntime.csproj", source, StringComparison.Ordinal);
        Assert.Contains("..\\..\\src\\Execution\\TianShu.Execution.Runtime\\TianShu.Execution.Runtime.csproj", source, StringComparison.Ordinal);
        Assert.Contains("..\\..\\src\\Hosting\\TianShu.AppHost.Configuration\\TianShu.AppHost.Configuration.csproj", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimarySolution_ShouldNotIncludeAgentRuntimeShellProject()
    {
        var solutionFile = Path.Combine(FindRepoRoot(), "TianShu.sln");
        var source = File.ReadAllText(solutionFile);

        Assert.DoesNotContain(
            "\"TianShu.AgentRuntime\", \"src\\Infrastructure\\TianShu.AgentRuntime\\TianShu.AgentRuntime.csproj\"",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Solutions_ShouldNotIncludeAgentRuntimeShellProject()
    {
        var repoRoot = FindRepoRoot();
        const string shellProjectEntry = "\"TianShu.AgentRuntime\", \"src\\Infrastructure\\TianShu.AgentRuntime\\TianShu.AgentRuntime.csproj\"";
        var solutionFiles = Directory
            .EnumerateFiles(repoRoot, "*.sln", SearchOption.TopDirectoryOnly)
            .Select(static path => (Path: path, Source: File.ReadAllText(path)))
            .Where(static item => item.Source.Contains(shellProjectEntry, StringComparison.Ordinal))
            .Select(item => Path.GetRelativePath(repoRoot, item.Path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(solutionFiles);
    }

    [Fact]
    public void AgentRuntimeShellDirectory_ShouldNotContainProjectFileOrProductionSourceFiles()
    {
        var projectDirectory = Path.Combine(
            FindRepoRoot(),
            "src",
            "Infrastructure",
            "TianShu.AgentRuntime");
        Assert.False(
            Directory.Exists(projectDirectory),
            $"旧 AgentRuntime 过渡目录不应继续保留：{Path.GetRelativePath(FindRepoRoot(), projectDirectory)}");
    }

    [Fact]
    public void LegacyInfrastructureAnalyzerDirectories_ShouldBeRemoved()
    {
        var repoRoot = FindRepoRoot();
        var legacyDirectories = new[]
        {
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Analyzers"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.Analyzers.Tests"),
        };

        Assert.All(
            legacyDirectories,
            directory => Assert.False(
                Directory.Exists(directory),
                $"旧 analyzer 残留目录不应继续保留：{Path.GetRelativePath(repoRoot, directory)}"));
    }

    [Fact]
    public void LegacyExecutionEngineAbstractionsDirectory_ShouldBeRemoved()
    {
        var repoRoot = FindRepoRoot();
        var legacyDirectory = Path.Combine(
            repoRoot,
            "src",
            "Core",
            "TianShu.ExecutionEngine.Abstractions");

        Assert.False(
            Directory.Exists(legacyDirectory),
            $"旧执行抽象残留目录不应继续保留：{Path.GetRelativePath(repoRoot, legacyDirectory)}");
    }

    [Fact]
    public void IdentityMemoryPlane_ShouldRouteMemoryOperationsThroughDefaultMemoryServiceFacade()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Core",
            "TianShu.IdentityMemory",
            "DefaultTianShuIdentityMemoryPlane.cs"));

        var requiredDelegations = new[]
        {
            "CreateMemoryService(context).ListProviders(query)",
            "CreateMemoryService(context).ListSpacesAsync(query, cancellationToken)",
            "CreateMemoryService(context, spaces).ResolveOverlayAsync(",
            "CreateMemoryService(context).FilterAsync(query, CreateMemoryOperationContext(context), cancellationToken)",
            "CreateMemoryService(context).AddAsync(command, CreateMemoryOperationContext(context), cancellationToken)",
            "CreateMemoryService(context).ExtractAsync(command, CreateMemoryOperationContext(context), cancellationToken)",
            "CreateMemoryService(context).ImportAsync(command, CreateMemoryOperationContext(context), cancellationToken)",
            "CreateMemoryService(context).ExportAsync(command, CreateMemoryOperationContext(context), cancellationToken)",
            "CreateMemoryService(context).BindProviderAsync(command, CreateMemoryOperationContext(context), cancellationToken)",
            "CreateMemoryService(context).ForgetAsync(command, CreateMemoryOperationContext(context), cancellationToken)",
            "CreateMemoryService(context).DeleteAsync(command, CreateMemoryOperationContext(context), cancellationToken)",
            "CreateMemoryService(context).RecordFeedbackAsync(command, CreateMemoryOperationContext(context), cancellationToken)",
            "CreateMemoryService(context).RecordCitationAsync(command, CreateMemoryOperationContext(context), cancellationToken)",
        };

        Assert.All(
            requiredDelegations,
            delegation => Assert.Contains(delegation, source, StringComparison.Ordinal));

        var serviceFactoryStart = source.IndexOf("private DefaultMemoryService CreateMemoryService", StringComparison.Ordinal);
        Assert.True(serviceFactoryStart > 0, "默认记忆平面必须集中保留一个 DefaultMemoryService 工厂入口。");

        var publicFacadeSource = source[..serviceFactoryStart];
        AssertSourceDoesNotContainAny(
            publicFacadeSource,
            "new DefaultMemoryService",
            "new TianShuLocalMemoryProvider",
            "new MemoryProviderRegistry",
            "new MemoryOverlayResolver",
            "new MemoryPolicyEngine");
    }

    [Fact]
    public void ExecutionRuntimeIdentityMemoryFacade_ShouldUseAppHostRuntimeSurface()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.IdentityMemory.cs"));

        var requiredSurfaceMethods = new[]
        {
            "\"identity/accountProfile/read\"",
            "\"identity/devices/list\"",
            "\"memory/providers/list\"",
            "\"memory/spaces/list\"",
            "\"memory/overlay/read\"",
            "\"memory/filter\"",
            "\"memory/review/list\"",
            "\"memory/add\"",
            "\"memory/extract\"",
            "\"memory/import\"",
            "\"memory/export\"",
            "\"memory/provider/bind\"",
            "\"memory/consolidation/run\"",
            "\"memory/forget\"",
            "\"memory/delete\"",
            "\"memory/supersede\"",
            "\"memory/review/approve\"",
            "\"memory/review/demote\"",
            "\"memory/review/merge\"",
            "\"memory/review/restore\"",
            "\"memory/feedback/record\"",
            "\"memory/citation/record\"",
        };

        Assert.All(
            requiredSurfaceMethods,
            method => Assert.Contains(method, source, StringComparison.Ordinal));
        AssertSourceDoesNotContainAny(
            source,
            "identityMemoryPlane.GetAccountProfileAsync(query, BuildIdentityMemoryContext(), cancellationToken)",
            "identityMemoryPlane.ListMemorySpacesAsync(query, BuildIdentityMemoryContext(), cancellationToken)",
            "identityMemoryPlane.AddMemoryAsync(command, BuildIdentityMemoryContext(), cancellationToken)",
            "new DefaultMemoryService",
            "new TianShuLocalMemoryProvider",
            "new MemoryProviderRegistry",
            "new MemoryOverlayResolver",
            "new MemoryPolicyEngine");
    }

    [Fact]
    public void RuntimeControlPlaneAdapterIdentityMemoryFacade_ShouldDelegateToExecutionRuntime()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "ControlPlane",
            "RuntimeControlPlaneAdapter.IdentityMemory.cs"));

        var requiredDelegations = new[]
        {
            "runtime.GetAccountProfileAsync(query, cancellationToken)",
            "runtime.ListBoundDevicesAsync(query, cancellationToken)",
            "runtime.ListMemoryProvidersAsync(query, cancellationToken)",
            "runtime.ListMemorySpacesAsync(query, cancellationToken)",
            "runtime.ResolveMemoryOverlayAsync(query, cancellationToken)",
            "runtime.FilterMemoryAsync(query, cancellationToken)",
            "runtime.ListMemoryReviewsAsync(query, cancellationToken)",
            "runtime.AddMemoryAsync(command, cancellationToken)",
            "runtime.ExtractMemoryAsync(command, cancellationToken)",
            "runtime.ImportMemoryAsync(command, cancellationToken)",
            "runtime.ExportMemoryAsync(command, cancellationToken)",
            "runtime.BindMemoryProviderAsync(command, cancellationToken)",
            "runtime.ForgetMemoryAsync(command, cancellationToken)",
            "runtime.DeleteMemoryAsync(command, cancellationToken)",
            "runtime.ApproveMemoryReviewAsync(command, cancellationToken)",
            "runtime.DemoteMemoryReviewAsync(command, cancellationToken)",
            "runtime.MergeMemoryReviewAsync(command, cancellationToken)",
            "runtime.RestoreMemoryReviewAsync(command, cancellationToken)",
            "runtime.RecordMemoryFeedbackAsync(command, cancellationToken)",
            "runtime.RecordMemoryCitationAsync(command, cancellationToken)",
        };

        Assert.All(
            requiredDelegations,
            delegation => Assert.Contains(delegation, source, StringComparison.Ordinal));
        AssertSourceDoesNotContainAny(
            source,
            "new DefaultMemoryService",
            "new TianShuLocalMemoryProvider",
            "new MemoryProviderRegistry",
            "new MemoryOverlayResolver",
            "new MemoryPolicyEngine");
    }

    [Fact]
    public void CoreLoopEntryPlanner_ShouldUseStageExecutorContractsWithoutRuntimeImplementationDependency()
    {
        var repoRoot = FindRepoRoot();
        var plannerSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Core",
            "TianShu.Kernel",
            "SessionCoreLoopEntryPlanner.cs"));
        var contractsSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Contracts",
            "TianShu.Contracts.Orchestration",
            "Models",
            "StageExecutorContracts.cs"));
        var checkpointBuilderSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "StageExecutorCheckpointBuilder.cs"));
        var dispatcherSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "StageExecutorDispatcher.cs"));
        var dispatchPlanFactorySource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "StageExecutorDispatchPlanFactory.cs"));

        Assert.DoesNotContain("using TianShu.Execution.Runtime;", plannerSource, StringComparison.Ordinal);
        Assert.Contains("public sealed record StageExecutorRuntimeContext", contractsSource, StringComparison.Ordinal);
        Assert.Contains("public enum StageExecutorDispatchKind", contractsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed record StageExecutorRuntimeContext", checkpointBuilderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public enum StageExecutorDispatchKind", dispatcherSource, StringComparison.Ordinal);
        Assert.Contains("StageExecutorDispatcher", dispatchPlanFactorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("using TianShu.Kernel;", dispatchPlanFactorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionCoreLoopEntryPlan", dispatchPlanFactorySource, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeStepExecutionEntry_ShouldNotOwnKernelOrchestrationDecisions()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.RuntimeSteps.cs"));

        AssertSourceDoesNotContainAny(
            source,
            "IAdaptiveOrchestrator",
            "AdaptiveOrchestrator",
            "StageGraphInterpreter",
            "ComposeStageGraph",
            "ProposeStage",
            "KernelProposal",
            "PromoteStrategy",
            "StrategyRegistry");
    }

    private static void AssertSourceDoesNotContainAny(string source, params string[] forbiddenFragments)
    {
        Assert.All(
            forbiddenFragments,
            fragment => Assert.DoesNotContain(fragment, source, StringComparison.Ordinal));
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
