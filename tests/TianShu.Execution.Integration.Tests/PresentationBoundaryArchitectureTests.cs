using System.IO;

namespace TianShu.Execution.Integration.Tests;

public sealed class PresentationBoundaryArchitectureTests
{
    [Fact]
    public void PresentationProjects_DoNotReferenceAgentRuntimeModelsNamespace()
    {
        var repoRoot = FindRepoRoot();
        var presentationRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar"),
        };

        var offenders = presentationRoots
            .Where(Directory.Exists)
            .SelectMany(static root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => File.ReadAllText(file).Contains("using TianShu.AgentRuntime.Models;", StringComparison.Ordinal))
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Presentation 层不应直接引用 TianShu.AgentRuntime.Models；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void PresentationProjects_DoNotReferenceLegacyAgentRuntimeConfigurationNamespace()
    {
        var repoRoot = FindRepoRoot();
        var presentationRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar"),
        };

        var offenders = presentationRoots
            .Where(Directory.Exists)
            .SelectMany(static root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => File.ReadAllText(file).Contains("using TianShu.AgentRuntime.Configuration;", StringComparison.Ordinal))
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Presentation 层不应继续引用旧配置命名空间 TianShu.AgentRuntime.Configuration；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void PresentationProjects_DoNotReferenceRuntimeStreamEventKinds()
    {
        var repoRoot = FindRepoRoot();
        var presentationRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar"),
        };

        var offenders = presentationRoots
            .Where(Directory.Exists)
            .SelectMany(static root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => File.ReadAllText(file).Contains("AgentStreamEventKind", StringComparison.Ordinal))
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Presentation 层不应继续引用 runtime 私有流事件枚举 AgentStreamEventKind；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void PresentationProjects_ShouldReferenceFormalControlPlaneProjectForNorthboundShell()
    {
        var repoRoot = FindRepoRoot();
        var projectFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "TianShu.Cli.csproj"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar", "TianShu.VSSDK.Sidecar.csproj"),
        };

        var offenders = projectFiles
            .Where(File.Exists)
            .Where(file => !File.ReadAllText(file).Contains("TianShu.ControlPlane\\TianShu.ControlPlane.csproj", StringComparison.Ordinal))
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Presentation 层项目应显式引用正式 TianShu.ControlPlane 入口；当前缺失文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void PresentationProjects_DoNotReferenceExecutionRuntimeControlPlaneNamespace()
    {
        var repoRoot = FindRepoRoot();
        var presentationRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar"),
        };

        var offenders = presentationRoots
            .Where(Directory.Exists)
            .SelectMany(static root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => File.ReadAllText(file).Contains("using TianShu.Execution.Runtime.ControlPlane;", StringComparison.Ordinal))
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Presentation 层不应继续直接引用 execution runtime control-plane 命名空间；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void PresentationProjects_DoNotConstructRuntimeControlPlaneAdapterDirectly()
    {
        var repoRoot = FindRepoRoot();
        var presentationRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar"),
        };

        var offenders = presentationRoots
            .Where(Directory.Exists)
            .SelectMany(static root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => File.ReadAllText(file).Contains("new RuntimeControlPlaneAdapter(", StringComparison.Ordinal))
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Presentation 层不应继续直接构造 RuntimeControlPlaneAdapter；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void PresentationProjects_DoNotCallRuntimeBackedControlPlaneExtensions()
    {
        var repoRoot = FindRepoRoot();
        var presentationRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar"),
        };
        var forbiddenTokens = new[]
        {
            ".AsControlPlane(",
            ".AsRuntimeControlPlane(",
        };

        var offenders = presentationRoots
            .Where(Directory.Exists)
            .SelectMany(static root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Presentation 层不应继续调用 runtime-backed control-plane extension；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void PresentationProjects_DoNotConstructTianShuControlPlaneDirectly()
    {
        var repoRoot = FindRepoRoot();
        var presentationRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar"),
        };

        var offenders = presentationRoots
            .Where(Directory.Exists)
            .SelectMany(static root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => File.ReadAllText(file).Contains("new TianShuControlPlane(", StringComparison.Ordinal))
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Presentation 层不应继续手动构造 TianShuControlPlane；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliPresentationSources_DoNotRetainInteractiveStateCompatibilitySymbol()
    {
        var repoRoot = FindRepoRoot();
        var cliRoot = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli");

        var offenders = Directory.EnumerateFiles(cliRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => File.ReadAllText(file).Contains("CliInteractiveStateCompatibility", StringComparison.Ordinal))
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI presentation 层不应继续保留 CliInteractiveStateCompatibility 命名；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliPresentationSource_ShouldPreferFormalDispatchBeforeLegacyDiagnosticsRpcFallback()
    {
        var repoRoot = FindRepoRoot();
        var commandRunnerFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "Commands", "Runners", "CliRuntimeCommandRunner.cs");
        var rpcHandlerFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "Interaction", "Commands", "Rpc", "InteractiveRpcCommandHandler.cs");

        var commandRunnerSource = File.ReadAllText(commandRunnerFile);
        var rpcHandlerSource = File.ReadAllText(rpcHandlerFile);

        Assert.Contains("TryInvokeFormalRpcAsync(", commandRunnerSource, StringComparison.Ordinal);
        Assert.Contains("TryInvokeFormalRpcAsync(", rpcHandlerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CliPresentationSource_ShouldNotUseLegacyDiagnosticsBridgeFallback()
    {
        var repoRoot = FindRepoRoot();
        var commandRunnerFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "Commands", "Runners", "CliRuntimeCommandRunner.cs");
        var rpcHandlerFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "Interaction", "Commands", "Rpc", "InteractiveRpcCommandHandler.cs");

        var commandRunnerSource = File.ReadAllText(commandRunnerFile);
        var rpcHandlerSource = File.ReadAllText(rpcHandlerFile);

        Assert.DoesNotContain("IExecutionRuntimeDiagnostics", commandRunnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".InvokeDiagnosticRpcAsync(", commandRunnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IExecutionRuntimeDiagnostics", rpcHandlerSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".InvokeDiagnosticRpcAsync(", rpcHandlerSource, StringComparison.Ordinal);
        Assert.Contains("BuildFormalRpcUnavailableMessage(", commandRunnerSource, StringComparison.Ordinal);
        Assert.Contains("BuildFormalRpcUnavailableMessage(", rpcHandlerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void VSExtensionPresentationSource_ShouldReuseProviderAbstractionsConfigKeyConstant()
    {
        var repoRoot = FindRepoRoot();
        var bridgeFile = Path.Combine(
            repoRoot,
            "src",
            "Presentations",
            "TianShu.VSSDK.VSExtension",
            "Services",
            "TianShuSidecarBridge.cs");
        var projectFile = Path.Combine(
            repoRoot,
            "src",
            "Presentations",
            "TianShu.VSSDK.VSExtension",
            "TianShu.VSSDK.VSExtension.csproj");

        var bridgeSource = File.ReadAllText(bridgeFile);
        var projectSource = File.ReadAllText(projectFile);

        Assert.Contains("using TianShu.Provider.Abstractions;", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("JsonPropertyName(OpenAiAppCatalogCompatibilityKeys.ChatGptBaseUrlConfigKey)", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("JsonPropertyName(OpenAiAppCatalogCompatibilityKeys.ForcedChatGptWorkspaceIdConfigKey)", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("JsonPropertyName(OpenAiAppCatalogCompatibilityKeys.ForcedLoginMethodConfigKey)", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("JsonPropertyName(OpenAiAppCatalogCompatibilityKeys.RequiresOpenAiAuthConfigKey)", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[JsonPropertyName(\"chatgpt_base_url\")]", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[JsonPropertyName(\"forced_chatgpt_workspace_id\")]", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[JsonPropertyName(\"forced_login_method\")]", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[JsonPropertyName(\"requires_openai_auth\")]", bridgeSource, StringComparison.Ordinal);
        Assert.Contains(
            "<Compile Include=\"..\\..\\Provider\\TianShu.Provider.Abstractions\\OpenAiAppCatalogCompatibilityKeys.cs\">",
            projectSource,
            StringComparison.Ordinal);
        Assert.Contains("<Link>Provider\\OpenAiAppCatalogCompatibilityKeys.cs</Link>", projectSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CliPresentationSources_DoNotRetainThreadCompatibilityNaming()
    {
        var repoRoot = FindRepoRoot();
        var cliRoot = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli");
        var forbiddenTokens = new[]
        {
            "ThreadCompatibilityModels",
            "AgentThreadSessionConfiguration",
            "AgentThreadInfo",
            "AgentThreadListResult",
            "AgentThreadResumeResult",
            "AgentThreadDetails",
            "PendingInteractiveRequestReplay",
            "legacy thread/session JSON output",
            "legacy thread summary JSON output",
            "legacy thread list JSON output",
            "legacy thread resume JSON output",
            "legacy thread detail JSON output",
            "pending interactive request JSON output",
        };

        var offenders = Directory.EnumerateFiles(cliRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI presentation 层不应继续保留线程兼容输出的旧命名；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliPresentationSources_DoNotRetainThreadProtocolRuntimeCarrierTokens()
    {
        var repoRoot = FindRepoRoot();
        var cliRoot = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli");
        var forbiddenTokens = new[]
        {
            "internal sealed class AgentThreadStatus",
            "internal sealed class AgentThreadGitInfo",
            "internal sealed class AgentThreadTurn",
            "internal sealed class AgentThreadTurnError",
            "internal abstract class AgentThreadTurnItem",
            "internal sealed class GenericThreadTurnItem",
            "internal sealed class AgentThreadSeedHistoryItem",
            "new AgentThreadStatus",
            "new AgentThreadGitInfo",
            "new AgentThreadTurnError",
            "new GenericThreadTurnItem",
            "ToAgentThreadOperationResult(",
            "ToAgentThreadLoadedListResult(",
            "ToAgentThreadUnsubscribeResult(",
            "ToAgentThreadElicitationResult(",
            "ToAgentThreadCommandAcceptedResult(",
            "ToAgentPendingInputState(",
            "ToAgentThreadTurn(",
            "ToAgentThreadTurnItem(",
            "ToAgentThreadSeedHistoryItem(",
            "AgentServiceTier.Parse(",
            "AgentApprovalPolicy.Parse(",
        };

        var offenders = Directory.EnumerateFiles(cliRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI presentation 层不应继续保留线程协议兼容的 runtime-ish carrier token；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void PresentationProjects_ShouldConsumeNorthboundExecutionEnvironmentSurface()
    {
        var repoRoot = FindRepoRoot();
        var presentationRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar"),
        };
        var forbiddenTokens = new[]
        {
            "runtime.StartCommandExecutionAsync(",
            "runtime.WriteCommandExecutionAsync(",
            "runtime.TerminateCommandExecutionAsync(",
            "runtime.ResizeCommandExecutionAsync(",
            "runtime.ExecuteCodeModeAsync(",
            "runtime.WaitCodeModeAsync(",
            "runtime.StartWindowsSandboxSetupAsync(",
        };

        var offenders = presentationRoots
            .Where(Directory.Exists)
            .SelectMany(static root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Presentation 层应通过正式 execution/environment surface 消费 northbound 执行入口；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliMainInteractionRunners_ShouldConsumeControlPlaneForTurnThreadAndGovernanceOperations()
    {
        var repoRoot = FindRepoRoot();
        var runnerFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "InteractiveChatRunner.cs"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "SendCommandRunner.cs"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "ConversationTurnCommandRunner.cs"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "ExecCommandRunner.cs"),
        };
        var forbiddenTokens = new[]
        {
            "runtime.SendAsync(",
            "runtime.SendFollowUpAsync(",
            "runtime.InterruptTurnAsync(",
            "runtime.ListThreadsAsync(",
            "runtime.StartNewThreadAsync(",
            "runtime.ForkThreadAsync(",
            "runtime.ArchiveThreadAsync(",
            "runtime.RenameThreadAsync(",
            "runtime.ResumeThreadAsync(",
            "runtime.RespondToApprovalAsync(",
            "runtime.RespondToPermissionRequestAsync(",
            "runtime.RespondToUserInputAsync(",
            "runtime.UnsubscribeThreadAsync(",
        };

        var offenders = runnerFiles
            .Where(File.Exists)
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI 主交互 runner 应通过正式 control-plane consumer 处理 turn/thread/governance 主链；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliPresentationSources_ShouldConsumeSessionSnapshotInsteadOfDirectRuntimeIndicators()
    {
        var repoRoot = FindRepoRoot();
        var runnerFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "InteractiveChatRunner.cs"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "SendCommandRunner.cs"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "ConversationTurnCommandRunner.cs"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "ExecCommandRunner.cs"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "Commands", "Runners", "CliRuntimeCommandRunner.cs"),
        };
        var forbiddenTokens = new[]
        {
            "runtime.RuntimeName",
            "runtime.ActiveThreadId",
            "runtime.HasActiveTurn",
        };

        var offenders = runnerFiles
            .Where(File.Exists)
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI presentation 层应通过 Sessions.GetSnapshotAsync() 消费 session state，而不是直接读取 runtime 指示器；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void SidecarPresentationSource_ShouldConsumeControlPlaneForCatalogAgentGovernanceRealtimeAndConfigOperations()
    {
        var repoRoot = FindRepoRoot();
        var sidecarFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar", "StdioSidecarHost.cs");
        var forbiddenTokens = new[]
        {
            "runtime.StartRealtimeAsync(",
            "runtime.AppendRealtimeTextAsync(",
            "runtime.AppendRealtimeAudioAsync(",
            "runtime.HandoffRealtimeOutputAsync(",
            "runtime.StopRealtimeAsync(",
            "runtime.UploadFeedbackAsync(",
            "runtime.ListModelsAsync(",
            "runtime.ListSkillsAsync(",
            "runtime.WriteSkillsConfigAsync(",
            "runtime.ListRemoteSkillsAsync(",
            "runtime.ExportRemoteSkillAsync(",
            "runtime.ListPluginsAsync(",
            "runtime.ReadPluginAsync(",
            "runtime.InstallPluginAsync(",
            "runtime.UninstallPluginAsync(",
            "runtime.ListAppsAsync(",
            "runtime.StartReviewAsync(",
            "runtime.ListExperimentalFeaturesAsync(",
            "runtime.ListCollaborationModesAsync(",
            "runtime.ListMcpServerStatusAsync(",
            "runtime.ReloadMcpServersAsync(",
            "runtime.StartMcpServerOauthLoginAsync(",
            "runtime.ReadConfigAsync(",
            "runtime.ReadConfigRequirementsAsync(",
            "runtime.WriteConfigValueAsync(",
            "runtime.WriteConfigBatchAsync(",
            "runtime.RegisterAgentThreadAsync(",
            "runtime.CreateAgentJobAsync(",
            "runtime.DispatchAgentJobAsync(",
            "runtime.ReportAgentJobItemAsync(",
            "runtime.ReadAgentJobAsync(",
        };

        var source = File.ReadAllText(sidecarFile);
        var offenders = forbiddenTokens.Where(source.Contains).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Sidecar presentation 层应通过正式 control-plane consumer 处理 catalog/config/agent/governance/realtime 主链；当前违规 token：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void SidecarPresentationSource_ShouldConsumeControlPlaneForConversationFormalQueriesAndCommands()
    {
        var repoRoot = FindRepoRoot();
        var sidecarFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar", "StdioSidecarHost.cs");
        var forbiddenTokens = new[]
        {
            "runtime.ListLoadedThreadsAsync(",
            "runtime.CompactThreadAsync(",
            "runtime.CleanBackgroundTerminalsAsync(",
            "runtime.IncrementThreadElicitationAsync(",
            "runtime.DecrementThreadElicitationAsync(",
            "runtime.SearchFuzzyFilesAsync(",
            "runtime.StartFuzzyFileSearchSessionAsync(",
            "runtime.UpdateFuzzyFileSearchSessionAsync(",
            "runtime.StopFuzzyFileSearchSessionAsync(",
        };

        var source = File.ReadAllText(sidecarFile);
        var offenders = forbiddenTokens.Where(source.Contains).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Sidecar presentation 层应通过正式 control-plane consumer 处理 conversations formal query/command 主链；当前违规 token：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void SidecarPresentationSource_ShouldConsumeControlPlaneForDiagnosticsCatalogAndAgentFormalQueries()
    {
        var repoRoot = FindRepoRoot();
        var sidecarFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar", "StdioSidecarHost.cs");
        var forbiddenTokens = new[]
        {
            "runtime.GetExecutionTraceAsync(",
            "runtime.ListAttemptSummariesAsync(",
            "runtime.GetCapabilityCatalogAsync(",
            "runtime.ResolveEngineBindingAsync(",
            "runtime.ListAgentsAsync(",
        };

        var source = File.ReadAllText(sidecarFile);
        var offenders = forbiddenTokens.Where(source.Contains).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Sidecar presentation 层应通过正式 control-plane consumer 处理 diagnostics/catalog/agents formal query 主链；当前违规 token：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void SidecarPresentationSource_ShouldPreferFormalDispatchBeforeLegacyDiagnosticsRpcFallback()
    {
        var repoRoot = FindRepoRoot();
        var sidecarFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar", "StdioSidecarHost.cs");
        var source = File.ReadAllText(sidecarFile);

        Assert.Contains("TryInvokeFormalRuntimeDispatchAsync(", source, StringComparison.Ordinal);
        Assert.Contains("runtime surface 调用成功", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SidecarPresentationSource_ShouldNotUseLegacyDiagnosticsBridgeFallback()
    {
        var repoRoot = FindRepoRoot();
        var sidecarFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar", "StdioSidecarHost.cs");
        var source = File.ReadAllText(sidecarFile);

        Assert.DoesNotContain("IExecutionRuntimeDiagnostics", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".InvokeDiagnosticRpcAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("invokeDiagnosticRpc", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BuildFormalRpcUnavailableMessage(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SidecarPresentationSource_ShouldConsumeControlPlaneForIdentityMemoryAndProjectionFormalQueries()
    {
        var repoRoot = FindRepoRoot();
        var sidecarFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar", "StdioSidecarHost.cs");
        var forbiddenTokens = new[]
        {
            "runtime.GetThreadProjectionAsync(",
            "runtime.GetApprovalQueueProjectionAsync(",
            "runtime.GetSpaceProjectionAsync(",
            "runtime.GetParticipantProjectionAsync(",
            "runtime.GetParticipantViewProjectionAsync(",
            "runtime.ListParticipantsInScopeAsync(",
            "runtime.GetAgentRosterProjectionAsync(",
            "runtime.GetTeamProjectionAsync(",
            "runtime.GetWorkflowBoardProjectionAsync(",
            "runtime.GetTaskBoardProjectionAsync(",
            "runtime.GetPlanProjectionAsync(",
            "runtime.GetArtifactProjectionAsync(",
            "runtime.GetArtifactCollectionProjectionAsync(",
            "runtime.GetAccountProfileAsync(",
            "runtime.ListBoundDevicesAsync(",
            "runtime.ListMemorySpacesAsync(",
            "runtime.ResolveMemoryOverlayAsync(",
        };

        var source = File.ReadAllText(sidecarFile);
        var offenders = forbiddenTokens.Where(source.Contains).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Sidecar presentation 层应通过正式 control-plane consumer 处理 identity/memory/projection formal query 主链；当前违规 token：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void SidecarPresentationSource_ShouldConsumeControlPlaneForSessionAndGovernanceFormalQueries()
    {
        var repoRoot = FindRepoRoot();
        var sidecarFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar", "StdioSidecarHost.cs");
        var forbiddenTokens = new[]
        {
            "runtime.GetSnapshotAsync(",
            "runtime.GetSessionOverviewAsync(",
            "runtime.ListSessionsAsync(",
            "runtime.ListUserInputRequestsAsync(",
        };

        var source = File.ReadAllText(sidecarFile);
        var offenders = forbiddenTokens.Where(source.Contains).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Sidecar presentation 层应通过正式 control-plane consumer 处理 session/governance formal query 主链；当前违规 token：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void SidecarPresentationSource_ShouldConsumeControlPlaneForWorkflowAndCollaborationFormalWrites()
    {
        var repoRoot = FindRepoRoot();
        var sidecarFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar", "StdioSidecarHost.cs");
        var forbiddenTokens = new[]
        {
            "runtime.CreateWorkflowAsync(",
            "runtime.PublishPlanAsync(",
            "runtime.CreateTaskAsync(",
            "runtime.UpdateTaskStateAsync(",
            "runtime.CreateSpaceAsync(",
            "runtime.ConfigureSpaceAsync(",
            "runtime.ArchiveSpaceAsync(",
            "runtime.BindParticipantToSessionAsync(",
            "runtime.BindParticipantToWorkflowAsync(",
            "runtime.UpdateParticipantRoleAsync(",
        };

        var source = File.ReadAllText(sidecarFile);
        var offenders = forbiddenTokens.Where(source.Contains).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Sidecar presentation 层应通过正式 control-plane consumer 处理 workflow/collaboration 写链；当前违规 token：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void SidecarPresentationSource_ShouldConsumeSessionSnapshotInsteadOfDirectRuntimeIndicators()
    {
        var repoRoot = FindRepoRoot();
        var sidecarFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar", "StdioSidecarHost.cs");
        var forbiddenTokens = new[]
        {
            "runtime.RuntimeName",
            "runtime.ActiveThreadId",
            "runtime.HasActiveTurn",
            "currentRuntime.RuntimeName",
            "currentRuntime.ActiveThreadId",
            "currentRuntime.HasActiveTurn",
            "newRuntime.RuntimeName",
            "newRuntime.ActiveThreadId",
            "newRuntime.HasActiveTurn",
        };

        var source = File.ReadAllText(sidecarFile);
        var offenders = forbiddenTokens.Where(source.Contains).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Sidecar presentation 层应通过 Sessions.GetSnapshotAsync() 消费 session state，而不是直接读取 runtime 指示器；当前违规 token：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliRuntimeSurfaceRunner_ShouldConsumeControlPlaneForWorkflowAndCollaborationFormalWrites()
    {
        var repoRoot = FindRepoRoot();
        var runnerFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "Commands", "Runners", "CliRuntimeCommandRunner.cs");
        var forbiddenTokens = new[]
        {
            "runtime.CreateWorkflowAsync(",
            "runtime.PublishPlanAsync(",
            "runtime.CreateTaskAsync(",
            "runtime.UpdateTaskStateAsync(",
            "runtime.CreateSpaceAsync(",
            "runtime.ConfigureSpaceAsync(",
            "runtime.ArchiveSpaceAsync(",
            "runtime.BindParticipantToSessionAsync(",
            "runtime.BindParticipantToWorkflowAsync(",
            "runtime.UpdateParticipantRoleAsync(",
        };

        var source = File.ReadAllText(runnerFile);
        var offenders = forbiddenTokens.Where(source.Contains).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI runtime-surface runner 应通过正式 control-plane consumer 处理 workflow/collaboration 写链；当前违规 token：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliRuntimeSurfaceRunner_ShouldConsumeControlPlaneForDiagnosticsCatalogAndAgentFormalOperations()
    {
        var repoRoot = FindRepoRoot();
        var runnerFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "Commands", "Runners", "CliRuntimeCommandRunner.cs");
        var forbiddenTokens = new[]
        {
            "runtime.GetExecutionTraceAsync(",
            "runtime.ListAttemptSummariesAsync(",
            "runtime.UploadFeedbackAsync(",
            "runtime.ListModelsAsync(",
            "runtime.GetCapabilityCatalogAsync(",
            "runtime.ResolveEngineBindingAsync(",
            "runtime.ListAgentsAsync(",
            "runtime.RegisterAgentThreadAsync(",
            "runtime.CreateAgentJobAsync(",
            "runtime.DispatchAgentJobAsync(",
            "runtime.ReportAgentJobItemAsync(",
            "runtime.ReadAgentJobAsync(",
        };

        var source = File.ReadAllText(runnerFile);
        var offenders = forbiddenTokens.Where(source.Contains).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI runtime-surface runner 应通过正式 control-plane consumer 处理 diagnostics/catalog/agents formal query 与 orchestration 主链；当前违规 token：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliRuntimeSurfaceRunner_ShouldConsumeControlPlaneForCatalogConfigAndReviewFormalOperations()
    {
        var repoRoot = FindRepoRoot();
        var runnerFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "Commands", "Runners", "CliRuntimeCommandRunner.cs");
        var forbiddenTokens = new[]
        {
            "runtime.ReadConfigAsync(",
            "runtime.ReadConfigRequirementsAsync(",
            "runtime.WriteConfigValueAsync(",
            "runtime.WriteConfigBatchAsync(",
            "runtime.ListAppsAsync(",
            "runtime.ListSkillsAsync(",
            "runtime.WriteSkillConfigAsync(",
            "runtime.ListRemoteSkillsAsync(",
            "runtime.ExportRemoteSkillAsync(",
            "runtime.ListPluginsAsync(",
            "runtime.ReadPluginAsync(",
            "runtime.InstallPluginAsync(",
            "runtime.UninstallPluginAsync(",
            "runtime.StartReviewAsync(",
            "runtime.ListExperimentalFeaturesAsync(",
            "runtime.ListCollaborationModesAsync(",
            "runtime.ListMcpServerStatusAsync(",
            "runtime.ReloadMcpServersAsync(",
            "runtime.StartMcpServerOauthLoginAsync(",
        };

        var source = File.ReadAllText(runnerFile);
        var offenders = forbiddenTokens.Where(source.Contains).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI runtime-surface runner 应通过正式 control-plane consumer 处理 catalog/config/review/mcp formal surface 主链；当前违规 token：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliRuntimeSurfaceRunner_ShouldConsumeControlPlaneForIdentityMemoryAndProjectionFormalQueries()
    {
        var repoRoot = FindRepoRoot();
        var runnerFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "Commands", "Runners", "CliRuntimeCommandRunner.cs");
        var forbiddenTokens = new[]
        {
            "runtime.GetThreadProjectionAsync(",
            "runtime.GetApprovalQueueProjectionAsync(",
            "runtime.GetSpaceProjectionAsync(",
            "runtime.GetParticipantProjectionAsync(",
            "runtime.GetParticipantViewProjectionAsync(",
            "runtime.ListParticipantsInScopeAsync(",
            "runtime.GetAgentRosterProjectionAsync(",
            "runtime.GetTeamProjectionAsync(",
            "runtime.GetWorkflowBoardProjectionAsync(",
            "runtime.GetTaskBoardProjectionAsync(",
            "runtime.GetPlanProjectionAsync(",
            "runtime.GetArtifactProjectionAsync(",
            "runtime.GetArtifactCollectionProjectionAsync(",
            "runtime.GetAccountProfileAsync(",
            "runtime.ListBoundDevicesAsync(",
            "runtime.ListMemorySpacesAsync(",
            "runtime.ResolveMemoryOverlayAsync(",
        };

        var source = File.ReadAllText(runnerFile);
        var offenders = forbiddenTokens.Where(source.Contains).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI runtime-surface runner 应通过正式 control-plane consumer 处理 identity/memory/projection formal query 主链；当前违规 token：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliRuntimeSurfaceRunner_ShouldConsumeControlPlaneForSessionAndGovernanceFormalQueries()
    {
        var repoRoot = FindRepoRoot();
        var runnerFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "Commands", "Runners", "CliRuntimeCommandRunner.cs");
        var forbiddenTokens = new[]
        {
            "runtime.GetSnapshotAsync(",
            "runtime.GetSessionOverviewAsync(",
            "runtime.ListSessionsAsync(",
            "runtime.ListUserInputRequestsAsync(",
        };

        var source = File.ReadAllText(runnerFile);
        var offenders = forbiddenTokens.Where(source.Contains).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI runtime-surface runner 应通过正式 control-plane consumer 处理 session/governance formal query 主链；当前违规 token：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliRuntimeSurfaceInvocation_ShouldUseCanonicalLowerCaseMethodIds()
    {
        var repoRoot = FindRepoRoot();
        var runnerFile = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli", "Commands", "Runners", "CliRuntimeCommandRunner.cs");
        var forbiddenTokens = new[]
        {
            "\"governance/approvalQueue/read\"",
            "\"governance/userInputs/list\"",
            "\"participant/bindSession\"",
            "\"participant/bindWorkflow\"",
            "\"participant/updateRole\"",
            "\"workflow/task/updateState\"",
            "\"workflow/taskBoard/read\"",
            "\"experimentalFeature/list\"",
            "\"collaborationMode/list\"",
            "\"mcpServerStatus/list\"",
            "\"config/mcpServer/reload\"",
            "\"configRequirements/read\"",
            "\"config/batchWrite\"",
            "\"mcpServer/oauth/login\"",
            "\"artifact/conversationSummary/read\"",
            "\"artifact/gitDiffToRemote/read\"",
            "\"externalAgentConfig/detect\"",
            "\"externalAgentConfig/import\"",
        };

        var source = File.ReadAllText(runnerFile);
        var offenders = forbiddenTokens.Where(source.Contains).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI runtime-surface invocation 应统一发送 canonical 全小写 method id；当前残留旧协议名：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliPresentationSources_DoNotRetainRuntimeAgentJobProgressPayload()
    {
        var repoRoot = FindRepoRoot();
        var cliRoot = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli");

        var offenders = Directory.EnumerateFiles(cliRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => File.ReadAllText(file).Contains("AgentJobProgressPayload", StringComparison.Ordinal))
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI presentation 层不应继续直接保留 runtime 事件载荷 AgentJobProgressPayload；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliPresentationSources_DoNotRetainAgentRuntimeBootstrapHelperNaming()
    {
        var repoRoot = FindRepoRoot();
        var cliRoot = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli");
        var forbiddenTokens = new[]
        {
            "AgentRuntimeCliBootstrapper",
            "AgentRuntimeBootstrapResult",
        };

        var offenders = Directory.EnumerateFiles(cliRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI presentation 层不应继续保留 AgentRuntimeCliBootstrapper / AgentRuntimeBootstrapResult 这组本地 bootstrap 旧命名；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliPresentationSources_DoNotRetainAgentRuntimeCommandRunnerNaming()
    {
        var repoRoot = FindRepoRoot();
        var cliRoot = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli");
        var forbiddenTokens = new[]
        {
            "internal sealed class AgentRuntimeCommandRunner",
            "new AgentRuntimeCommandRunner(",
            "AgentRuntimeCommandRunner()",
            "AgentRuntimeCommandRunner(Func<IAgentRuntime> runtimeFactory)",
        };

        var offenders = Directory.EnumerateFiles(cliRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI presentation 层不应继续保留 AgentRuntimeCommandRunner 这组本地 orchestrator 旧命名；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void PresentationProjects_DoNotRetainAgentRuntimeKernelLaunchLocatorNaming()
    {
        var repoRoot = FindRepoRoot();
        var presentationRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar"),
        };

        var offenders = presentationRoots
            .Where(Directory.Exists)
            .SelectMany(static root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => File.ReadAllText(file).Contains("AgentRuntimeKernelLaunchLocator", StringComparison.Ordinal))
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"presentation 层不应继续引用旧命名 AgentRuntimeKernelLaunchLocator；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void PresentationProjects_DoNotRetainLegacyAgentRuntimeBrandingStrings()
    {
        var repoRoot = FindRepoRoot();
        var presentationRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.VSExtension"),
        };
        var forbiddenTokens = new[]
        {
            "AgentRuntime 首个完整消费者",
            "通过 AgentRuntime 通用入口直接调用 Kernel request method",
            "消费 AgentRuntime 的最小会话扩展",
            "AgentRuntime typed 能力",
            "初始化 AgentRuntime",
        };

        var offenders = presentationRoots
            .Where(Directory.Exists)
            .SelectMany(static root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"presentation 层面向用户的文案不应继续暴露 AgentRuntime 旧 branding；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void VsixManifestMetadata_DoNotRetainLegacyAgentRuntimeBrandingStrings()
    {
        var repoRoot = FindRepoRoot();
        var manifestFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.VSExtension", "source.extension.vsixmanifest"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.VSExtension", "extension.vsixmanifest"),
        };
        var forbiddenTokens = new[]
        {
            "消费 AgentRuntime 的最小会话扩展",
            "AgentRuntime 的最小会话扩展",
        };

        var offenders = manifestFiles
            .Where(File.Exists)
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"VSIX manifest 元数据不应继续暴露 AgentRuntime 旧 branding；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void PresentationProjects_DoNotAliasControlPlaneConversationContractsWithAgentPrefixes()
    {
        var repoRoot = FindRepoRoot();
        var presentationRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar"),
        };
        var forbiddenTokens = new[]
        {
            "using AgentSessionSource = TianShu.Contracts.Conversations.ControlPlaneSessionSource;",
            "using AgentSubAgentSource = TianShu.Contracts.Conversations.ControlPlaneSubAgentSource;",
            "using AgentThreadSourceKind = TianShu.Contracts.Conversations.ControlPlaneThreadSourceKind;",
            "ToAgentSessionSource(",
        };

        var offenders = presentationRoots
            .Where(Directory.Exists)
            .SelectMany(static root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"presentation 层不应继续以 Agent* 别名包装 control-plane conversation contracts；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliPresentationSources_DoNotRetainInteractiveRuntimePayloadBridges()
    {
        var repoRoot = FindRepoRoot();
        var cliRoot = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli");
        var forbiddenTokens = new[]
        {
            "implicit operator CliApprovalRequestPayload?(ApprovalRequestPayload?",
            "implicit operator ApprovalRequestPayload?(CliApprovalRequestPayload?",
            "implicit operator CliPermissionRequestPayload?(PermissionRequestPayload?",
            "implicit operator PermissionRequestPayload?(CliPermissionRequestPayload?",
            "implicit operator CliUserInputRequestPayload?(UserInputRequestPayload?",
            "implicit operator UserInputRequestPayload?(CliUserInputRequestPayload?",
            "implicit operator CliPendingFollowUpPayload?(PendingFollowUpLifecyclePayload?",
            "implicit operator PendingFollowUpLifecyclePayload?(CliPendingFollowUpPayload?",
            "implicit operator CliPendingInputStatePayload?(PendingInputStatePayload?",
            "implicit operator PendingInputStatePayload?(CliPendingInputStatePayload?",
            "ReadPayload<ApprovalRequestPayload>(",
            "ReadPayload<PermissionRequestPayload>(",
            "ReadPayload<UserInputRequestPayload>(",
            "ReadPayload<PendingFollowUpLifecyclePayload>(",
            "ReadPayload<PendingInputStatePayload>(",
        };

        var offenders = Directory.EnumerateFiles(cliRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI presentation 层不应继续保留交互型 runtime payload 兼容桥；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliPresentationSources_DoNotRetainRuntimeStructuredValueOrUserInputCarriers()
    {
        var repoRoot = FindRepoRoot();
        var cliRoot = Path.Combine(repoRoot, "src", "Presentations", "TianShu.Cli");
        var forbiddenTokens = new[]
        {
            "AgentStructuredValue",
            "AgentUserInput",
        };

        var offenders = Directory.EnumerateFiles(cliRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI presentation 层不应继续保留 runtime structured-value / user-input carrier；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void SidecarPresentationSources_DoNotRetainThreadHistoryCompatibilityNaming()
    {
        var repoRoot = FindRepoRoot();
        var presentationRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar"),
            Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.VSExtension"),
        };
        var forbiddenTokens = new[]
        {
            "SidecarResponseItem",
            "TianShuSidecarResponseItem",
            "thread-history compatibility item used only for stdio resume payloads",
            "thread-history compatibility item used only for sidecar resume payloads",
        };

        var offenders = presentationRoots
            .Where(Directory.Exists)
            .SelectMany(static root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"sidecar / VSIX presentation 层不应继续保留线程 history compatibility 命名；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void SidecarPresentationSources_DoNotRetainInteractiveRuntimePayloadBridges()
    {
        var repoRoot = FindRepoRoot();
        var sidecarRoot = Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar");
        var forbiddenTokens = new[]
        {
            "ReadTypedPayload<ApprovalRequestPayload>(",
            "ReadTypedPayload<PermissionRequestPayload>(",
            "ReadTypedPayload<UserInputRequestPayload>(",
            "ReadTypedPayload<ServerRequestResolvedPayload>(",
            "ReadTypedPayload<PendingFollowUpLifecyclePayload>(",
            "FromContracts(ApprovalRequestPayload?",
            "FromContracts(PermissionRequestPayload?",
            "FromContracts(UserInputRequestPayload?",
            "FromContracts(ServerRequestResolvedPayload?",
            "FromContracts(PendingFollowUpLifecyclePayload?",
            "FromContracts(PendingInputStatePayload?",
            "FromContracts(ApprovalDecisionOptionPayload?",
            "FromContracts(ExecPolicyAmendmentPayload?",
            "FromContracts(NetworkPolicyAmendmentPayload?",
            "FromContracts(ApprovalMetadataFieldPayload",
            "FromContracts(PermissionFieldPayload",
            "FromContracts(UserInputOptionPayload",
            "FromContracts(UserInputQuestionPayload",
            "FromContracts(PendingFollowUpCompareKeyPayload?",
            "FromContracts(PendingInputStateEntryPayload",
        };

        var offenders = Directory.EnumerateFiles(sidecarRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"sidecar presentation 层不应继续保留交互型 runtime payload 兼容桥；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void SidecarPresentationSources_DoNotRetainRuntimeStructuredValueOrUserInputCarriers()
    {
        var repoRoot = FindRepoRoot();
        var sidecarRoot = Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.Sidecar");
        var forbiddenTokens = new[]
        {
            "AgentStructuredValue",
            "AgentUserInput",
        };

        var offenders = Directory.EnumerateFiles(sidecarRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"sidecar presentation 层不应继续保留 runtime structured-value / user-input carrier；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void VSExtensionPresentationProject_ShouldNotReferenceRuntimeControlPlaneOrProviderProjects()
    {
        var repoRoot = FindRepoRoot();
        var projectFile = Path.Combine(
            repoRoot,
            "src",
            "Presentations",
            "TianShu.VSSDK.VSExtension",
            "TianShu.VSSDK.VSExtension.csproj");
        var projectSource = File.ReadAllText(projectFile);
        var forbiddenTokens = new[]
        {
            "<ProjectReference",
            "TianShu.Execution.Runtime.csproj",
            "TianShu.ControlPlane",
            "TianShu.Contracts",
            "TianShu.Provider.Abstractions.csproj",
        };

        var offenders = forbiddenTokens.Where(projectSource.Contains).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"VSExtension 展示层项目不应直接引用 runtime/control-plane/contracts/provider 工程；当前违规 token：{string.Join(", ", offenders)}");
        Assert.Contains(
            "<Compile Include=\"..\\..\\Provider\\TianShu.Provider.Abstractions\\OpenAiAppCatalogCompatibilityKeys.cs\">",
            projectSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "<Link>Provider\\OpenAiAppCatalogCompatibilityKeys.cs</Link>",
            projectSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void VSExtensionPresentationSources_DoNotRetainInteractiveRuntimePayloadBridges()
    {
        var repoRoot = FindRepoRoot();
        var extensionRoot = Path.Combine(repoRoot, "src", "Presentations", "TianShu.VSSDK.VSExtension");
        var forbiddenTokens = new[]
        {
            "ReadTypedPayload<ApprovalRequestPayload>(",
            "ReadTypedPayload<PermissionRequestPayload>(",
            "ReadTypedPayload<UserInputRequestPayload>(",
            "ReadTypedPayload<ServerRequestResolvedPayload>(",
            "ReadTypedPayload<PendingFollowUpLifecyclePayload>(",
            "ReadTypedPayload<PendingInputStatePayload>(",
            "ParseStructuredPayload<ApprovalRequestPayload>(",
            "ParseStructuredPayload<PermissionRequestPayload>(",
            "ParseStructuredPayload<UserInputRequestPayload>(",
            "ParseStructuredPayload<ServerRequestResolvedPayload>(",
            "ParseStructuredPayload<PendingInputStatePayload>(",
        };

        var offenders = Directory.EnumerateFiles(extensionRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens.Any(content.Contains);
            })
            .Select(static file => Path.GetRelativePath(FindRepoRoot(), file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"VSExtension 展示层不应继续保留交互型 runtime payload 兼容桥；当前违规文件：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void VSExtensionPresentationSources_ShouldUseLocalSidecarTypesForThreadAndInteractiveState()
    {
        var repoRoot = FindRepoRoot();
        var bridgeFile = Path.Combine(
            repoRoot,
            "src",
            "Presentations",
            "TianShu.VSSDK.VSExtension",
            "Services",
            "TianShuSidecarBridge.cs");
        var sourceTypesFile = Path.Combine(
            repoRoot,
            "src",
            "Presentations",
            "TianShu.VSSDK.VSExtension",
            "Services",
            "TianShuSidecarSourceTypes.cs");
        var threadOptionTypesFile = Path.Combine(
            repoRoot,
            "src",
            "Presentations",
            "TianShu.VSSDK.VSExtension",
            "Services",
            "TianShuSidecarThreadOptionTypes.cs");
        var bridgeSource = File.ReadAllText(bridgeFile);
        var sourceTypesSource = File.ReadAllText(sourceTypesFile);
        var threadOptionTypesSource = File.ReadAllText(threadOptionTypesFile);

        Assert.Contains("TianShuSidecarSessionSource", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("TianShuSidecarThreadSourceKind", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("TianShuSidecarThreadHistoryItem", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("TianShuSidecarStructuredValue", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("TianShuSidecarPendingInputStatePayload", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("TianShuSidecarPendingInteractiveRequestReplayPayload", bridgeSource, StringComparison.Ordinal);

        Assert.Contains("internal sealed class TianShuSidecarSessionSource", sourceTypesSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class TianShuSidecarThreadSourceKind", sourceTypesSource, StringComparison.Ordinal);

        Assert.Contains("internal sealed class TianShuSidecarServiceTierOverride", threadOptionTypesSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class TianShuSidecarApprovalPolicy", threadOptionTypesSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class TianShuSidecarPersonality", threadOptionTypesSource, StringComparison.Ordinal);
    }

    [Fact]
    public void VSExtensionPresentationSources_ShouldExposeFormalRuntimeSurfaceMethodsAndPreserveCapabilityPayloads()
    {
        var repoRoot = FindRepoRoot();
        var bridgeFile = Path.Combine(
            repoRoot,
            "src",
            "Presentations",
            "TianShu.VSSDK.VSExtension",
            "Services",
            "TianShuSidecarBridge.cs");
        var toolWindowFile = Path.Combine(
            repoRoot,
            "src",
            "Presentations",
            "TianShu.VSSDK.VSExtension",
            "TianShuConversationToolWindowControl.xaml.cs");

        var bridgeSource = File.ReadAllText(bridgeFile);
        var toolWindowSource = File.ReadAllText(toolWindowFile);

        Assert.Contains("public string? PayloadJson { get; set; }", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("PayloadJson = response.PayloadData.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("response.PayloadJson", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("GetCapabilityProtocolMethod(SelectedCapability, SelectedActionMethod)", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"ConversationThreadRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"SessionSnapshotRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"SessionOverviewRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"SessionList\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"GovernanceApprovalQueueRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"GovernanceUserInputsList\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"DiagnosticsTraceRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"DiagnosticsAttemptsList\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"ModelCatalogRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"ModelBindingResolve\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"SkillsConfigWrite\" => new { path = \"skills/sample-skill\", enabled = true, cwd = WorkingDirectory }", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"SkillsRemoteList\" => new { hazelnutScope = string.Empty, productSurface = string.Empty, enabled = true }", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"AppList\" => new { limit = 20, cursor = string.Empty, threadId = ThreadId ?? string.Empty, forceRefetch = false }", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"PluginUninstall\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"CollaborationCreate\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"CollaborationConfigure\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"CollaborationArchive\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"CollaborationOverviewRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"CollaborationSpaceRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"CollaborationList\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"ParticipantBindSession\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"ParticipantBindWorkflow\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"ParticipantUpdateRole\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"ParticipantRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"ParticipantViewRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"ParticipantList\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"ArtifactDetailRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"ArtifactCollectionRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"WorkflowCreate\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"WorkflowPlanPublish\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"WorkflowTaskCreate\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"WorkflowTaskUpdateState\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"WorkflowBoardRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"WorkflowTaskBoardRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"WorkflowPlanRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"AgentList\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"AgentRosterRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"AgentTeamRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"IdentityAccountRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"IdentityDevicesList\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryProvidersList\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemorySpacesList\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryOverlayRead\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryFilter\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryAdd\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryExtract\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryImport\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryExport\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryBindProvider\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryForget\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryDelete\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryFeedbackRecord\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryCitationRecord\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryProvidersList\" => new { scopeKind = \"Workspace\" }", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryFilter\" => new { memorySpaceId = new { value = \"memory-space-capability-001\" }, key = \"pref.shell\", sourceKind = \"conversation\", scopeKind = \"Workspace\" }", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryAdd\" => new { memorySpaceId = new { value = \"memory-space-capability-001\" }, key = \"pref.shell\", value = \"pwsh\", confidence = 0.9 }", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryForget\" => new { memoryRecordId = new { value = \"memory-record-capability-001\" } }", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryDelete\" => new { memoryRecordId = new { value = \"memory-record-capability-001\" }, reason = \"cleanup\" }", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"MemoryFeedbackRecord\" => new { memoryRecordId = new { value = \"memory-record-capability-001\" }, decision = \"applied\", feedback = \"accepted\" }", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"ExperimentalFeatureList\" => new { limit = 20, cursor = string.Empty }", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"model/catalog/read\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"configrequirements/read\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"conversation/thread/read\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"plugin/uninstall\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"artifact/detail/read\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"collaboration/create\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"workflow/create\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"identity/account/read\"", toolWindowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"configRequirements/read\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"memory/providers/list\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"memory/overlay/read\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"memory/filter\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"memory/add\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"memory/extract\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"memory/import\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"memory/export\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"memory/provider/bind\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"memory/forget\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"memory/delete\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"memory/feedback/record\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"memory/citation/record\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"mcpserver/oauth/login\"", toolWindowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mcpServer/oauth/login\"", toolWindowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExternalAgentConfig", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("TianShuSidecarCapability.FuzzyFileSearch => method switch", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"fuzzyfilesearch/sessionstart\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("TianShuSidecarCapability.ThreadOperation => method switch", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"thread/loaded/list\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"thread/backgroundterminals/clean\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("TianShuSidecarCapability.Realtime => method switch", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"thread/realtime/start\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"thread/realtime/handoffoutput\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("TianShuSidecarCapability.AgentOperation => method switch", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"agent/thread/register\"", toolWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"agent/job/item/report\"", toolWindowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void VSExtensionCapabilityPanel_ShouldUseCanonicalLowerCaseMethodIds()
    {
        var repoRoot = FindRepoRoot();
        var toolWindowFile = Path.Combine(
            repoRoot,
            "src",
            "Presentations",
            "TianShu.VSSDK.VSExtension",
            "TianShuConversationToolWindowControl.xaml.cs");
        var forbiddenTokens = new[]
        {
            "\"governance/approvalQueue/read\"",
            "\"governance/userInputs/list\"",
            "\"workflow/task/updateState\"",
            "\"workflow/taskBoard/read\"",
            "\"experimentalFeature/list\"",
            "\"collaborationMode/list\"",
            "\"mcpServerStatus/list\"",
            "\"config/mcpServer/reload\"",
            "\"artifact/conversationSummary/read\"",
            "\"artifact/gitDiffToRemote/read\"",
            "\"externalAgentConfig/detect\"",
            "\"externalAgentConfig/import\"",
        };

        var source = File.ReadAllText(toolWindowFile);
        var offenders = forbiddenTokens.Where(source.Contains).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"VSExtension capability panel 应统一发送 canonical 全小写 method id；当前残留旧协议名：{string.Join(", ", offenders)}");
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
