using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Remote;

/// <summary>
/// 远端投影安全策略，控制 snapshot / event 出站前的敏感字段净化。
/// Remote projection security policy controlling outbound snapshot / event redaction.
/// </summary>
public sealed record RemoteProjectionSecurityPolicy
{
    private static readonly string[] DefaultSecretKeys =
    [
        "apiKey",
        "api_key",
        "authorization",
        "bearer",
        "credential",
        "password",
        "secret",
        "token",
    ];

    private static readonly string[] DefaultWorkspaceContentKeys =
    [
        "file_content",
        "fileContent",
        "workspace_file_content",
        "workspaceFileContent",
        "raw_content",
        "rawContent",
        "full_text",
        "fullText",
    ];

    public RemoteProjectionSecurityPolicy(
        bool allowWorkspaceFileContent = false,
        string policyRef = "remote-redaction-policy://default",
        IReadOnlyList<string>? secretKeys = null,
        IReadOnlyList<string>? workspaceContentKeys = null)
    {
        AllowWorkspaceFileContent = allowWorkspaceFileContent;
        PolicyRef = IdentifierGuard.AgainstNullOrWhiteSpace(policyRef, nameof(policyRef));
        SecretKeys = Normalize(secretKeys ?? DefaultSecretKeys);
        WorkspaceContentKeys = Normalize(workspaceContentKeys ?? DefaultWorkspaceContentKeys);
    }

    public bool AllowWorkspaceFileContent { get; }

    public string PolicyRef { get; }

    public IReadOnlyList<string> SecretKeys { get; }

    public IReadOnlyList<string> WorkspaceContentKeys { get; }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string> values)
        => values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

/// <summary>
/// Remote Module 出站投影安全净化器；它不读取 workspace，只替换不应远端暴露的值。
/// Outbound Remote Module projection sanitizer; it does not read workspace and only replaces values that must not be exposed remotely.
/// </summary>
public static class RemoteProjectionSecurityProjector
{
    private const string SecretKind = "secret";
    private const string AbsolutePathKind = "absolute_path";
    private const string WorkspaceFileContentKind = "workspace_file_content";

    /// <summary>
    /// 净化远程线程快照，并把发生过的脱敏类别写回 redaction 摘要。
    /// Sanitizes a remote thread snapshot and records redaction categories in its redaction summary.
    /// </summary>
    public static RemoteThreadSnapshot ProjectSnapshot(
        RemoteThreadSnapshot snapshot,
        RemoteProjectionSecurityPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var effectivePolicy = policy ?? new RemoteProjectionSecurityPolicy();
        var redactions = RedactionCollector.From(snapshot.Redaction);

        var projected = new RemoteThreadSnapshot(
            RedactText(snapshot.SnapshotId, null, effectivePolicy, redactions) ?? snapshot.SnapshotId,
            snapshot.ThreadId,
            ProjectRunState(snapshot.RunState, effectivePolicy, redactions),
            snapshot.CurrentStage is null ? null : ProjectStageState(snapshot.CurrentStage, effectivePolicy, redactions),
            snapshot.ToolStates.Select(tool => ProjectToolState(tool, effectivePolicy, redactions)).ToArray(),
            snapshot.SubAgentStates.Select(agent => ProjectSubAgentState(agent, effectivePolicy, redactions)).ToArray(),
            snapshot.PendingApprovals.Select(approval => ProjectPendingApproval(approval, effectivePolicy, redactions)).ToArray(),
            snapshot.Artifacts.Select(artifact => ProjectArtifactRef(artifact, effectivePolicy, redactions)).ToArray(),
            ProjectDiagnostics(snapshot.Diagnostics, effectivePolicy, redactions),
            ProjectEvidence(snapshot.Evidence, effectivePolicy, redactions),
            redaction: redactions.ToSnapshotRedaction(effectivePolicy),
            capturedAt: snapshot.CapturedAt,
            version: snapshot.Version);

        return projected;
    }

    /// <summary>
    /// 净化远程连续性事件 payload 与 visibility。
    /// Sanitizes remote continuity event payload and visibility.
    /// </summary>
    public static RemoteContinuityEvent ProjectEvent(
        RemoteContinuityEvent @event,
        RemoteProjectionSecurityPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var effectivePolicy = policy ?? new RemoteProjectionSecurityPolicy();
        var redactions = RedactionCollector.From(@event.Visibility);
        var payload = @event.Payload is null
            ? null
            : ProjectStructuredValue(@event.Payload, null, effectivePolicy, redactions);
        var visibility = redactions.ToEventVisibility(@event.Visibility, effectivePolicy);

        return new RemoteContinuityEvent(
            RedactText(@event.EventId, null, effectivePolicy, redactions) ?? @event.EventId,
            @event.ThreadId,
            @event.Cursor,
            @event.Kind,
            @event.OccurredAt,
            payload,
            visibility,
            RedactText(@event.CorrelationId, null, effectivePolicy, redactions));
    }

    private static RemoteRunState ProjectRunState(
        RemoteRunState state,
        RemoteProjectionSecurityPolicy policy,
        RedactionCollector redactions)
        => new(
            state.Lifecycle,
            RedactText(state.ActiveRunRef, null, policy, redactions),
            state.ActiveTurnId,
            state.ActiveExecutionId,
            RedactText(state.NotificationCode, null, policy, redactions),
            state.UpdatedAt);

    private static RemoteStageState ProjectStageState(
        RemoteStageState state,
        RemoteProjectionSecurityPolicy policy,
        RedactionCollector redactions)
        => new(
            RedactText(state.GraphId, null, policy, redactions) ?? state.GraphId,
            RedactText(state.StageId, null, policy, redactions) ?? state.StageId,
            state.Status,
            RedactText(state.StageKind, null, policy, redactions),
            RedactText(state.Objective, null, policy, redactions),
            state.StartedAt,
            state.UpdatedAt,
            ProjectStringList(state.DiagnosticsRefs, policy, redactions));

    private static RemoteToolState ProjectToolState(
        RemoteToolState tool,
        RemoteProjectionSecurityPolicy policy,
        RedactionCollector redactions)
        => new(
            RedactText(tool.ToolId, null, policy, redactions) ?? tool.ToolId,
            RedactText(tool.ToolName, null, policy, redactions) ?? tool.ToolName,
            tool.Status,
            tool.CallId,
            tool.SideEffectLevel,
            RequiresHumanGate(tool.SideEffectLevel, tool.RequiresHumanGate),
            RedactText(tool.ApprovalRef, null, policy, redactions),
            RedactText(tool.ResultRef, null, policy, redactions),
            RedactText(tool.FailureCode, null, policy, redactions));

    private static RemoteSubAgentState ProjectSubAgentState(
        RemoteSubAgentState agent,
        RemoteProjectionSecurityPolicy policy,
        RedactionCollector redactions)
        => new(
            agent.AgentId,
            RedactText(agent.Role, null, policy, redactions) ?? agent.Role,
            agent.Status,
            agent.Depth,
            RedactText(agent.ParentAgentRef, null, policy, redactions),
            RedactText(agent.TaskRef, null, policy, redactions),
            RedactText(agent.DiagnosticsRef, null, policy, redactions));

    private static RemotePendingApproval ProjectPendingApproval(
        RemotePendingApproval approval,
        RemoteProjectionSecurityPolicy policy,
        RedactionCollector redactions)
        => new(
            approval.ApprovalId,
            RedactText(approval.Title, null, policy, redactions) ?? approval.Title,
            approval.State,
            approval.SideEffectLevel,
            requiresHumanGate: true,
            ProjectStringList(approval.DecisionOptions, policy, redactions),
            RedactText(approval.RiskSummary, null, policy, redactions),
            RedactText(approval.DiffRef, null, policy, redactions),
            RedactText(approval.ArtifactRef, null, policy, redactions),
            approval.ExpiresAt);

    private static RemoteArtifactRef ProjectArtifactRef(
        RemoteArtifactRef artifact,
        RemoteProjectionSecurityPolicy policy,
        RedactionCollector redactions)
        => new(
            artifact.ArtifactId,
            RedactText(artifact.Name, null, policy, redactions) ?? artifact.Name,
            RedactText(artifact.Kind, null, policy, redactions) ?? artifact.Kind,
            RedactText(artifact.State, null, policy, redactions) ?? artifact.State,
            RedactText(artifact.UriRef, null, policy, redactions),
            RedactText(artifact.Summary, "summary", policy, redactions));

    private static RemoteDiagnosticsSummary ProjectDiagnostics(
        RemoteDiagnosticsSummary diagnostics,
        RemoteProjectionSecurityPolicy policy,
        RedactionCollector redactions)
        => new(
            ProjectStringList(diagnostics.RuntimeTraceRefs, policy, redactions),
            ProjectStringList(diagnostics.DiagnosticsRefs, policy, redactions),
            ProjectStringList(diagnostics.MetricsEventIds, policy, redactions),
            ProjectStringList(diagnostics.FailureCodes, policy, redactions),
            ProjectStringList(diagnostics.MissingReasons, policy, redactions));

    private static RemoteEvidenceSummary ProjectEvidence(
        RemoteEvidenceSummary evidence,
        RemoteProjectionSecurityPolicy policy,
        RedactionCollector redactions)
        => new(
            RedactText(evidence.TurnLogRef, null, policy, redactions),
            RedactText(evidence.RolloutRef, null, policy, redactions),
            ProjectStringList(evidence.AuditRefs, policy, redactions),
            ProjectStringList(evidence.DowngradeReasons, policy, redactions));

    private static StructuredValue ProjectStructuredValue(
        StructuredValue value,
        string? propertyName,
        RemoteProjectionSecurityPolicy policy,
        RedactionCollector redactions)
    {
        if (IsSecretKey(propertyName, policy))
        {
            redactions.Add(SecretKind);
            return StructuredValue.FromString("[redacted:secret]");
        }

        if (!policy.AllowWorkspaceFileContent && IsWorkspaceContentKey(propertyName, policy))
        {
            redactions.Add(WorkspaceFileContentKind);
            return StructuredValue.FromString("[redacted:workspace_file_content]");
        }

        return value.Kind switch
        {
            StructuredValueKind.Object => StructuredValue.FromObject(value.Properties.ToDictionary(
                static pair => pair.Key,
                pair => ProjectStructuredValue(pair.Value, pair.Key, policy, redactions),
                StringComparer.Ordinal)),
            StructuredValueKind.Array => StructuredValue.FromArray(value.Items.Select(item => ProjectStructuredValue(item, propertyName, policy, redactions)).ToArray()),
            StructuredValueKind.String => StructuredValue.FromString(RedactText(value.StringValue, propertyName, policy, redactions) ?? string.Empty),
            _ => value,
        };
    }

    private static IReadOnlyList<string> ProjectStringList(
        IReadOnlyList<string> values,
        RemoteProjectionSecurityPolicy policy,
        RedactionCollector redactions)
        => values
            .Select(value => RedactText(value, null, policy, redactions) ?? value)
            .ToArray();

    private static string? RedactText(
        string? value,
        string? propertyName,
        RemoteProjectionSecurityPolicy policy,
        RedactionCollector redactions)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        if (IsSecretKey(propertyName, policy) || LooksLikeSecret(value))
        {
            redactions.Add(SecretKind);
            return "[redacted:secret]";
        }

        if (!policy.AllowWorkspaceFileContent && IsWorkspaceContentKey(propertyName, policy))
        {
            redactions.Add(WorkspaceFileContentKind);
            return "[redacted:workspace_file_content]";
        }

        if (LooksLikeLocalAbsolutePath(value))
        {
            redactions.Add(AbsolutePathKind);
            return "[redacted:absolute_path]";
        }

        return value.Trim();
    }

    private static bool RequiresHumanGate(SideEffectLevel sideEffectLevel, bool current)
        => current || sideEffectLevel > SideEffectLevel.ReadOnly;

    private static bool IsSecretKey(string? propertyName, RemoteProjectionSecurityPolicy policy)
        => !string.IsNullOrWhiteSpace(propertyName)
            && policy.SecretKeys.Any(key => propertyName.Contains(key, StringComparison.OrdinalIgnoreCase));

    private static bool IsWorkspaceContentKey(string? propertyName, RemoteProjectionSecurityPolicy policy)
        => !string.IsNullOrWhiteSpace(propertyName)
            && policy.WorkspaceContentKeys.Any(key => propertyName.Equals(key, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeSecret(string value)
        => value.Contains("authorization:", StringComparison.OrdinalIgnoreCase)
            || value.Contains("api_key=", StringComparison.OrdinalIgnoreCase)
            || value.Contains("apikey=", StringComparison.OrdinalIgnoreCase)
            || value.Contains("password=", StringComparison.OrdinalIgnoreCase)
            || value.Contains("secret=", StringComparison.OrdinalIgnoreCase)
            || value.Contains("bearer ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("sk-", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeLocalAbsolutePath(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        if (trimmed.Length >= 3
            && char.IsLetter(trimmed[0])
            && trimmed[1] == ':'
            && (trimmed[2] == '\\' || trimmed[2] == '/'))
        {
            return true;
        }

        return trimmed.StartsWith("/home/", StringComparison.Ordinal)
            || trimmed.StartsWith("/Users/", StringComparison.Ordinal)
            || trimmed.StartsWith("/mnt/", StringComparison.Ordinal)
            || trimmed.StartsWith("/tmp/", StringComparison.Ordinal)
            || trimmed.StartsWith("/var/", StringComparison.Ordinal)
            || trimmed.StartsWith("/etc/", StringComparison.Ordinal);
    }

    private sealed class RedactionCollector
    {
        private readonly HashSet<string> kinds = new(StringComparer.Ordinal);
        private readonly HashSet<string> policyRefs = new(StringComparer.Ordinal);

        private RedactionCollector()
        {
        }

        public static RedactionCollector From(RemoteSnapshotRedaction redaction)
        {
            var collector = new RedactionCollector();
            foreach (var kind in redaction.RedactedKinds)
            {
                collector.Add(kind);
            }

            foreach (var policyRef in redaction.PolicyRefs)
            {
                collector.policyRefs.Add(policyRef);
            }

            return collector;
        }

        public static RedactionCollector From(RemoteEventVisibility visibility)
        {
            var collector = new RedactionCollector();
            foreach (var kind in visibility.RedactedKinds)
            {
                collector.Add(kind);
            }

            if (!string.IsNullOrWhiteSpace(visibility.PolicyRef))
            {
                collector.policyRefs.Add(visibility.PolicyRef);
            }

            return collector;
        }

        public void Add(string kind)
        {
            if (!string.IsNullOrWhiteSpace(kind))
            {
                kinds.Add(kind.Trim());
            }
        }

        public RemoteSnapshotRedaction ToSnapshotRedaction(RemoteProjectionSecurityPolicy policy)
        {
            if (kinds.Count > 0)
            {
                policyRefs.Add(policy.PolicyRef);
            }

            return new RemoteSnapshotRedaction(
                kinds.Count > 0,
                kinds.Order(StringComparer.Ordinal).ToArray(),
                policyRefs.Order(StringComparer.Ordinal).ToArray());
        }

        public RemoteEventVisibility ToEventVisibility(
            RemoteEventVisibility original,
            RemoteProjectionSecurityPolicy policy)
        {
            if (kinds.Count > 0)
            {
                policyRefs.Add(policy.PolicyRef);
            }

            return new RemoteEventVisibility(
                original.Redacted || kinds.Count > 0,
                original.VisibleScopes,
                kinds.Order(StringComparer.Ordinal).ToArray(),
                policyRefs.Order(StringComparer.Ordinal).FirstOrDefault());
        }
    }
}
