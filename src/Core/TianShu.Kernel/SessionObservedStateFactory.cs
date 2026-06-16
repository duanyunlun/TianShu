using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.Kernel;

/// <summary>
/// Session observed state 工厂，集中固定 Observe 阶段外部状态进入上下文账本的 Kernel 规则。
/// Session observed-state factory that centralizes Kernel rules for projecting external Observe state into the context ledger.
/// </summary>
public static class SessionObservedStateFactory
{
    /// <summary>
    /// 创建 Observe 阶段看到的状态快照。
    /// Creates the state snapshot seen by the Observe stage.
    /// </summary>
    public static SessionObservedState Create(
        string? workspaceCwd,
        string? workspaceSandboxMode,
        string? workspaceWebSearchMode,
        string? workspaceWindowsSandboxLevel,
        bool allowLoginShell,
        IReadOnlyList<ArtifactRef>? artifactRefs,
        IReadOnlyList<RuntimeStageRegistryIssue>? stageRegistryIssues,
        string? memoryMode,
        string? approvalPolicy,
        string? policySandboxMode,
        string? policyWebSearchMode,
        bool defaultModeRequestUserInputEnabled)
    {
        var workspaceSegments = BuildWorkspaceStateSegments(
            workspaceCwd,
            workspaceSandboxMode,
            workspaceWebSearchMode,
            workspaceWindowsSandboxLevel,
            allowLoginShell);
        var artifactSegments = BuildArtifactStateSegments(artifactRefs);
        var diagnosticSegments = BuildDiagnosticStateSegments(stageRegistryIssues);
        var memorySegments = BuildMemoryStateSegments(memoryMode);
        var policySegments = BuildPolicyStateSegments(
            approvalPolicy,
            policySandboxMode,
            policyWebSearchMode,
            defaultModeRequestUserInputEnabled);

        return new SessionObservedState(
            workspaceSegments,
            artifactSegments,
            diagnosticSegments,
            memorySegments,
            policySegments,
            policySegments.Count == 0 ? [] : ["runtime-policy-context"]);
    }

    private static IReadOnlyList<StageContextSegment> BuildWorkspaceStateSegments(
        string? cwd,
        string? sandboxMode,
        string? webSearchMode,
        string? windowsSandboxLevel,
        bool allowLoginShell)
    {
        var normalizedCwd = Normalize(cwd);
        var normalizedSandboxMode = Normalize(sandboxMode);
        if (normalizedCwd is null && normalizedSandboxMode is null && Normalize(webSearchMode) is null)
        {
            return [];
        }

        var content = string.Join(
            "; ",
            new[]
            {
                FormatObservedPart("cwd", normalizedCwd),
                FormatObservedPart("sandbox_mode", normalizedSandboxMode),
                FormatObservedPart("windows_sandbox", Normalize(windowsSandboxLevel)),
                FormatObservedPart("allow_login_shell", allowLoginShell.ToString()),
            }.Where(static item => item is not null).Cast<string>());
        return
        [
            new StageContextSegment(
                "workspace_state",
                content,
                title: "Observed workspace state",
                source: new ResourceRef("workspace", normalizedCwd ?? "current"),
                required: true,
                estimatedTokens: EstimateTokens(content)),
        ];
    }

    private static IReadOnlyList<StageContextSegment> BuildArtifactStateSegments(IReadOnlyList<ArtifactRef>? artifactRefs)
    {
        var distinctArtifactRefs = (artifactRefs ?? [])
            .Where(static artifact => !string.IsNullOrWhiteSpace(artifact.Id.Value))
            .DistinctBy(static artifact => artifact.Id.Value, StringComparer.Ordinal)
            .ToArray();
        if (distinctArtifactRefs.Length == 0)
        {
            return [];
        }

        var content = string.Join(
            "; ",
            distinctArtifactRefs.Select(static artifact =>
                $"id={artifact.Id.Value}, name={artifact.Name ?? "<unnamed>"}, kind={artifact.Kind ?? "<unknown>"}"));
        return
        [
            new StageContextSegment(
                "artifact_state",
                content,
                title: "Observed artifact refs",
                source: new ResourceRef("artifact_state", "thread"),
                required: false,
                estimatedTokens: EstimateTokens(content)),
        ];
    }

    private static IReadOnlyList<StageContextSegment> BuildDiagnosticStateSegments(
        IReadOnlyList<RuntimeStageRegistryIssue>? issues)
    {
        var warnings = (issues ?? [])
            .Where(static issue => issue.Severity == RuntimeStageRegistryIssueSeverity.Warning)
            .ToArray();
        if (warnings.Length == 0)
        {
            return [];
        }

        return warnings
            .Select(static issue =>
            {
                var content = $"code={issue.Code}; stage={issue.StageId ?? "<none>"}; message={issue.Message}";
                return new StageContextSegment(
                    "diagnostic_state",
                    content,
                    title: "Observed Stage Registry warning",
                    source: new ResourceRef("stage_registry_issue", issue.Code),
                    required: false,
                    estimatedTokens: EstimateTokens(content));
            })
            .ToArray();
    }

    private static IReadOnlyList<StageContextSegment> BuildMemoryStateSegments(string? memoryMode)
    {
        var normalizedMemoryMode = Normalize(memoryMode);
        if (normalizedMemoryMode is null)
        {
            return [];
        }

        var content = $"memory_mode={normalizedMemoryMode}";
        return
        [
            new StageContextSegment(
                "memory_state",
                content,
                title: "Observed memory state",
                source: new ResourceRef("memory", "thread"),
                required: false,
                estimatedTokens: EstimateTokens(content)),
        ];
    }

    private static IReadOnlyList<StageContextSegment> BuildPolicyStateSegments(
        string? approvalPolicy,
        string? sandboxMode,
        string? webSearchMode,
        bool defaultModeRequestUserInputEnabled)
    {
        var content = string.Join(
            "; ",
            new[]
            {
                FormatObservedPart("approval_policy", Normalize(approvalPolicy)),
                FormatObservedPart("sandbox_mode", Normalize(sandboxMode)),
                FormatObservedPart("web_search_mode", Normalize(webSearchMode)),
                FormatObservedPart("default_mode_request_user_input", defaultModeRequestUserInputEnabled.ToString()),
            }.Where(static item => item is not null).Cast<string>());
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        return
        [
            new StageContextSegment(
                "policy_state",
                content,
                title: "Observed runtime policy state",
                source: new ResourceRef("policy", "runtime"),
                required: false,
                estimatedTokens: EstimateTokens(content)),
        ];
    }

    private static string? FormatObservedPart(string key, string? value)
        => value is null ? null : $"{key}={value}";

    private static int EstimateTokens(string content)
        => Math.Max(16, (content.Length + 3) / 4);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
