using TianShu.ControlPlane.Abstractions;
using TianShu.ControlPlane.Abstractions.Operations;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using Xunit;

namespace TianShu.ControlPlane.Tests;

public sealed class ControlOperationNormalizationTests
{
    [Fact]
    public void TianShuControlPlaneContract_ShouldExposeUnifiedOperationEntry()
    {
        Assert.True(typeof(IControlPlane).IsAssignableFrom(typeof(ITianShuControlPlane)));
    }

    [Fact]
    public void Normalize_ShouldReturnTypedResultForCatalogQuery()
    {
        var payload = StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["catalog"] = StructuredValue.FromString("models"),
        });
        var request = new ControlOperationRequest("op-query-001", "catalog.list", payload: payload);

        var result = ControlOperationNormalizer.Default.Normalize(request);

        Assert.Equal(ControlOperationKind.Query, result.OperationKind);
        Assert.Equal(ControlOperationStatus.Completed, result.Status);
        Assert.Same(payload, result.TypedResult);
        Assert.Null(result.CoreIntent);
        Assert.Null(result.GovernanceEnvelope);
    }

    [Fact]
    public void Normalize_ShouldGenerateCoreIntentForTurnOperation()
    {
        var request = new ControlOperationRequest(
            "op-turn-001",
            "turn.submit",
            new ControlOperationSubject(new SessionId("session-1"), new ThreadId("thread-1"), turnId: new TurnId("turn-1")),
            new ControlOperationGovernanceRequest(
                "governance-1",
                policyIds: [" policy.runtime ", "policy.runtime"],
                allowedToolIds: ["tool.read"],
                allowedModuleIds: ["provider.openai"],
                maxSideEffectLevel: SideEffectLevel.ExternalNetwork,
                requiresHumanGate: true),
            StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["user_input_ref"] = StructuredValue.FromString("input-ref-1"),
            }));

        var result = ControlOperationNormalizer.Default.Normalize(request);

        Assert.Equal(ControlOperationKind.CoreIntent, result.OperationKind);
        Assert.Equal(ControlOperationStatus.Accepted, result.Status);
        var intent = Assert.IsType<TurnIntent>(result.CoreIntent);
        Assert.Equal(new CoreIntentId("intent:op-turn-001"), intent.IntentId);
        Assert.Equal(CoreIntentKind.Turn, intent.IntentKind);
        Assert.Equal("input-ref-1", intent.UserInputRef);
        Assert.Equal(new SessionId("session-1"), intent.Subject.SessionId);
        Assert.Equal(new ThreadId("thread-1"), intent.Subject.ThreadId);
        Assert.Equal(["policy.runtime"], intent.Governance.PolicyIds);
        Assert.Equal(["tool.read"], intent.Governance.AllowedToolIds);
        Assert.Equal(["provider.openai"], intent.Governance.AllowedModuleIds);
        Assert.Same(intent.Governance, result.GovernanceEnvelope);
    }

    [Fact]
    public void Normalize_ShouldRejectCoreIntentWithoutGovernanceEnvelope()
    {
        var request = new ControlOperationRequest(
            "op-turn-002",
            "turn.submit",
            new ControlOperationSubject(new SessionId("session-1"), new ThreadId("thread-1")),
            payload: StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["user_input_ref"] = StructuredValue.FromString("input-ref-1"),
            }));

        var result = ControlOperationNormalizer.Default.Normalize(request);

        Assert.Equal(ControlOperationKind.CoreIntent, result.OperationKind);
        Assert.Equal(ControlOperationStatus.Rejected, result.Status);
        Assert.Equal("control.operation.governance_missing", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void Normalize_ShouldRejectStateAndControlWhenNoControlledHandlerIsRegistered()
    {
        var state = ControlOperationNormalizer.Default.Normalize(new ControlOperationRequest("op-state-001", "thread.archive"));
        var control = ControlOperationNormalizer.Default.Normalize(new ControlOperationRequest("op-control-001", "session.start"));

        Assert.Equal(ControlOperationKind.State, state.OperationKind);
        Assert.Equal(ControlOperationStatus.Rejected, state.Status);
        Assert.Equal("control.operation.state_handler_missing", Assert.Single(state.Issues).Code);
        Assert.Null(state.CoreIntent);

        Assert.Equal(ControlOperationKind.Control, control.OperationKind);
        Assert.Equal(ControlOperationStatus.Rejected, control.Status);
        Assert.Equal("control.operation.control_handler_missing", Assert.Single(control.Issues).Code);
        Assert.Null(control.CoreIntent);
    }

    [Fact]
    public void Normalize_ShouldClassifyRemoteCommandIngressOperations()
    {
        Assert.Equal(ControlOperationKind.CoreIntent, ControlOperationNormalizer.Default.Classify("remote.submit_message"));
        Assert.Equal(ControlOperationKind.CoreIntent, ControlOperationNormalizer.Default.Classify("remote.interrupt"));
        Assert.Equal(ControlOperationKind.CoreIntent, ControlOperationNormalizer.Default.Classify("remote.resume"));
        Assert.Equal(ControlOperationKind.Governance, ControlOperationNormalizer.Default.Classify("remote.approval_decision"));
        Assert.Equal(ControlOperationKind.Control, ControlOperationNormalizer.Default.Classify("remote.steer"));
        Assert.Equal(ControlOperationKind.Control, ControlOperationNormalizer.Default.Classify("remote.cancel_pending_operation"));
        Assert.Equal(ControlOperationKind.Unspecified, ControlOperationNormalizer.Default.Classify("remote.direct_runtime_write"));
    }

    [Fact]
    public void Normalize_ShouldGenerateCoreIntentForRemoteSubmitMessage()
    {
        var request = new ControlOperationRequest(
            "remote-command-001",
            "remote.submit_message",
            new ControlOperationSubject(new SessionId("session-remote-001"), new ThreadId("thread-remote-001")),
            new ControlOperationGovernanceRequest(
                "remote-command-001-governance",
                maxSideEffectLevel: SideEffectLevel.ReadOnly,
                requiresHumanGate: true),
            StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["user_input_ref"] = StructuredValue.FromString("remote-command:remote-command-001:message"),
            }));

        var result = ControlOperationNormalizer.Default.Normalize(request);

        Assert.Equal(ControlOperationKind.CoreIntent, result.OperationKind);
        Assert.Equal(ControlOperationStatus.Accepted, result.Status);
        var intent = Assert.IsType<TurnIntent>(result.CoreIntent);
        Assert.Equal("remote-command:remote-command-001:message", intent.UserInputRef);
        Assert.Equal(SideEffectLevel.ReadOnly, intent.Governance.MaxSideEffectLevel);
        Assert.True(intent.Governance.RequiresHumanGate);
    }

    [Fact]
    public void Normalize_ShouldFailClosedForRemoteControlCommandsWithoutHandler()
    {
        var steer = ControlOperationNormalizer.Default.Normalize(new ControlOperationRequest("remote-steer-001", "remote.steer"));
        var cancel = ControlOperationNormalizer.Default.Normalize(new ControlOperationRequest("remote-cancel-001", "remote.cancel_pending_operation"));

        Assert.Equal(ControlOperationKind.Control, steer.OperationKind);
        Assert.Equal(ControlOperationStatus.Rejected, steer.Status);
        Assert.Equal("control.operation.control_handler_missing", Assert.Single(steer.Issues).Code);

        Assert.Equal(ControlOperationKind.Control, cancel.OperationKind);
        Assert.Equal(ControlOperationStatus.Rejected, cancel.Status);
        Assert.Equal("control.operation.control_handler_missing", Assert.Single(cancel.Issues).Code);
    }

    [Fact]
    public void Normalize_ShouldCreateGovernanceEnvelopeForRemoteApprovalDecision()
    {
        var result = ControlOperationNormalizer.Default.Normalize(new ControlOperationRequest(
            "remote-approval-001",
            "remote.approval_decision",
            governance: new ControlOperationGovernanceRequest(
                "remote-approval-001-governance",
                maxSideEffectLevel: SideEffectLevel.HostMutation,
                requiresHumanGate: true)));

        Assert.Equal(ControlOperationKind.Governance, result.OperationKind);
        Assert.Equal(ControlOperationStatus.Completed, result.Status);
        Assert.Equal("remote-approval-001-governance", result.GovernanceEnvelope?.EnvelopeId);
        Assert.True(result.GovernanceEnvelope?.RequiresHumanGate);
    }

    [Fact]
    public void Normalize_ShouldRejectUnknownOperation()
    {
        var result = ControlOperationNormalizer.Default.Normalize(new ControlOperationRequest("op-unknown-001", "legacy.freeform"));

        Assert.Equal(ControlOperationKind.Unspecified, result.OperationKind);
        Assert.Equal(ControlOperationStatus.Rejected, result.Status);
        Assert.Equal("control.operation.unclassified", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void UnifiedControlOperationEntry_ShouldNotContainKernelExecutionSemantics()
    {
        var repoRoot = FindRepoRoot();
        var guardedFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Core", "TianShu.ControlPlane.Abstractions", "Operations", "ControlOperationContracts.cs"),
            Path.Combine(repoRoot, "src", "Core", "TianShu.ControlPlane", "ControlOperationNormalizer.cs"),
        };
        var forbiddenTokens = new[] { "StageGraph", "RuntimeStep", "ProviderWire", "ToolInvocationStep" };

        var violations = guardedFiles
            .SelectMany(file =>
            {
                var relativePath = Path.GetRelativePath(repoRoot, file);
                return File.ReadAllLines(file)
                    .Select((line, index) => new { Line = line, LineNumber = index + 1 })
                    .Where(item => forbiddenTokens.Any(token => item.Line.Contains(token, StringComparison.Ordinal)))
                    .Select(item => $"{relativePath}:{item.LineNumber}: {item.Line.Trim()}");
            })
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "统一 ControlOperation 入口不得包含 StageGraph 解释、RuntimeStep 执行或 provider/tool 私有调用语义。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ControlPlaneProjects_ShouldNotOwnStageGraphRuntimeStepOrPrivateImplementationCalls()
    {
        var repoRoot = FindRepoRoot();
        var guardedRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Core", "TianShu.ControlPlane"),
            Path.Combine(repoRoot, "src", "Core", "TianShu.ControlPlane.Abstractions"),
        };
        var forbiddenTokens = new[]
        {
            "StageGraph",
            "RuntimeStep",
            "TianShu.Provider.",
            "TianShu.Tools.",
            "TianShu.AppHost.State",
            "KernelThreadStore",
            "IProjectionRuntimeStores",
            "IArtifactStore",
            "IMemoryService",
        };

        var violations = guardedRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file =>
            {
                var relativePath = Path.GetRelativePath(repoRoot, file);
                return File.ReadAllLines(file)
                    .Select((line, index) => new { Line = line, LineNumber = index + 1 })
                    .Where(item => forbiddenTokens.Any(token => item.Line.Contains(token, StringComparison.Ordinal)))
                    .Select(item => $"{relativePath}:{item.LineNumber}: {item.Line.Trim()}");
            })
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Control Plane 不得解释 StageGraph、生成 RuntimeStep，或直接调用 provider/tool/state store 私有实现。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
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

        throw new InvalidOperationException("无法从测试运行目录定位 TianShu 仓库根目录。");
    }
}
