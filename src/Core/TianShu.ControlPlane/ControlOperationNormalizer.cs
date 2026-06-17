using TianShu.ControlPlane.Abstractions.Operations;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.ControlPlane;

/// <summary>
/// Control Plane operation 归一化器，只负责分类、治理信封和 CoreIntent 生成。
/// Control Plane operation normalizer that only classifies operations and creates governance envelopes or CoreIntent objects.
/// </summary>
public sealed class ControlOperationNormalizer
{
    public static ControlOperationNormalizer Default { get; } = new();

    public ControlOperationResult Normalize(ControlOperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kind = Classify(request.OperationName);
        return kind switch
        {
            ControlOperationKind.Query => ControlOperationResult.Completed(request, kind, request.Payload),
            ControlOperationKind.Control => ControlOperationResult.Rejected(
                request,
                kind,
                "control.operation.control_handler_missing",
                "Control operation 尚未接入受控处理器，默认拒绝以避免绕过 Control Plane 状态边界。"),
            ControlOperationKind.State => ControlOperationResult.Rejected(
                request,
                kind,
                "control.operation.state_handler_missing",
                "State operation 尚未接入受控状态处理器，默认拒绝以避免直接修改 session/thread/workflow 状态。"),
            ControlOperationKind.Governance => NormalizeGovernance(request),
            ControlOperationKind.CoreIntent => NormalizeCoreIntent(request),
            _ => ControlOperationResult.Rejected(
                request,
                ControlOperationKind.Unspecified,
                "control.operation.unclassified",
                $"无法归一化 Control Plane operation：{request.OperationName}"),
        };
    }

    public ControlOperationKind Classify(string operationName)
    {
        var normalized = NormalizeOperationName(operationName);
        if (IsQueryOperation(normalized))
        {
            return ControlOperationKind.Query;
        }

        if (IsCoreIntentOperation(normalized))
        {
            return ControlOperationKind.CoreIntent;
        }

        if (IsGovernanceOperation(normalized))
        {
            return ControlOperationKind.Governance;
        }

        if (IsStateOperation(normalized))
        {
            return ControlOperationKind.State;
        }

        if (IsControlOperation(normalized))
        {
            return ControlOperationKind.Control;
        }

        return ControlOperationKind.Unspecified;
    }

    private static ControlOperationResult NormalizeGovernance(ControlOperationRequest request)
    {
        if (request.Governance is null)
        {
            return ControlOperationResult.Rejected(
                request,
                ControlOperationKind.Governance,
                "control.operation.governance_missing",
                "Governance operation 必须携带治理信封请求。");
        }

        var envelope = request.Governance.ToGovernanceEnvelope();
        return ControlOperationResult.Completed(request, ControlOperationKind.Governance, request.Payload, envelope);
    }

    private static ControlOperationResult NormalizeCoreIntent(ControlOperationRequest request)
    {
        if (request.Subject is null)
        {
            return ControlOperationResult.Rejected(
                request,
                ControlOperationKind.CoreIntent,
                "control.operation.subject_missing",
                "Core intent operation 必须携带 session/thread subject。");
        }

        if (request.Governance is null)
        {
            return ControlOperationResult.Rejected(
                request,
                ControlOperationKind.CoreIntent,
                "control.operation.governance_missing",
                "Core intent operation 必须携带治理信封请求。");
        }

        var subject = request.Subject.ToKernelSubjectRef();
        var governance = request.Governance.ToGovernanceEnvelope();
        var intentId = new CoreIntentId($"intent:{request.OperationId}");
        var operationName = NormalizeOperationName(request.OperationName);

        CoreIntent intent;
        try
        {
            if (HasPrefix(operationName, "turn.") || string.Equals(operationName, "remote.submit_message", StringComparison.Ordinal))
            {
                intent = new TurnIntent(
                    intentId,
                    subject,
                    governance,
                    ReadRequiredString(request.Payload, "user_input_ref", "userInputRef"));
            }
            else if (HasPrefix(operationName, "resume.") || string.Equals(operationName, "remote.resume", StringComparison.Ordinal))
            {
                intent = new ResumeIntent(
                    intentId,
                    subject,
                    governance,
                    ReadRequiredString(request.Payload, "resume_token", "resumeToken"),
                    ReadRequiredString(request.Payload, "checkpoint_ref", "checkpointRef"));
            }
            else if (HasPrefix(operationName, "recovery."))
            {
                intent = new RecoveryIntent(
                    intentId,
                    subject,
                    governance,
                    new KernelRunId(ReadRequiredString(request.Payload, "failed_run_id", "failedRunId")),
                    new StageId(ReadRequiredString(request.Payload, "failed_stage_id", "failedStageId")),
                    ReadRequiredString(request.Payload, "error_signal_ref", "errorSignalRef"));
            }
            else if (HasPrefix(operationName, "evaluation."))
            {
                intent = new EvaluationIntent(
                    intentId,
                    subject,
                    governance,
                    new KernelRunId(ReadRequiredString(request.Payload, "run_id", "runId")));
            }
            else if (HasPrefix(operationName, "review."))
            {
                intent = new ReviewIntent(
                    intentId,
                    subject,
                    governance,
                    ReadRequiredString(request.Payload, "review_target_ref", "reviewTargetRef"));
            }
            else if (HasPrefix(operationName, "compact.") || HasPrefix(operationName, "compaction."))
            {
                intent = new CompactionIntent(
                    intentId,
                    subject,
                    governance,
                    ReadRequiredString(request.Payload, "context_scope_ref", "contextScopeRef"));
            }
            else if (HasPrefix(operationName, "interrupt.") || string.Equals(operationName, "remote.interrupt", StringComparison.Ordinal))
            {
                intent = new InterruptIntent(
                    intentId,
                    subject,
                    governance,
                    ReadRequiredString(request.Payload, "interrupt_reason", "interruptReason"));
            }
            else
            {
                return ControlOperationResult.Rejected(
                    request,
                    ControlOperationKind.CoreIntent,
                    "control.operation.intent_unknown",
                    $"无法为 operation 生成 CoreIntent：{request.OperationName}");
            }
        }
        catch (ArgumentException ex)
        {
            return ControlOperationResult.Rejected(
                request,
                ControlOperationKind.CoreIntent,
                "control.operation.intent_payload_invalid",
                ex.Message);
        }

        return ControlOperationResult.CoreIntentGenerated(request, intent);
    }

    private static bool IsQueryOperation(string operationName)
        => HasAnyPrefix(operationName, "query.", "catalog.", "diagnostics.", "projection.", "read.", "list.", "get.")
            || string.Equals(operationName, "threads.list", StringComparison.Ordinal)
            || string.Equals(operationName, "thread.read", StringComparison.Ordinal);

    private static bool IsCoreIntentOperation(string operationName)
        => HasAnyPrefix(operationName, "turn.", "resume.", "recovery.", "evaluation.", "review.", "compact.", "compaction.", "interrupt.")
            || string.Equals(operationName, "remote.submit_message", StringComparison.Ordinal)
            || string.Equals(operationName, "remote.interrupt", StringComparison.Ordinal)
            || string.Equals(operationName, "remote.resume", StringComparison.Ordinal);

    private static bool IsGovernanceOperation(string operationName)
        => HasAnyPrefix(operationName, "governance.", "approval.", "permission.", "policy.")
            || string.Equals(operationName, "remote.approval_decision", StringComparison.Ordinal);

    private static bool IsStateOperation(string operationName)
        => HasAnyPrefix(operationName, "state.", "thread.", "workflow.");

    private static bool IsControlOperation(string operationName)
        => HasAnyPrefix(operationName, "control.", "command.", "session.")
            || string.Equals(operationName, "remote.steer", StringComparison.Ordinal)
            || string.Equals(operationName, "remote.cancel_pending_operation", StringComparison.Ordinal);

    private static bool HasAnyPrefix(string value, params string[] prefixes)
        => prefixes.Any(prefix => HasPrefix(value, prefix));

    private static bool HasPrefix(string value, string prefix)
        => value.StartsWith(prefix, StringComparison.Ordinal);

    private static string NormalizeOperationName(string operationName)
        => IdentifierGuard.AgainstNullOrWhiteSpace(operationName, nameof(operationName)).ToLowerInvariant();

    private static string ReadRequiredString(StructuredValue payload, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (payload.TryGetProperty(propertyName, out var value)
                && value is not null
                && value.Kind == StructuredValueKind.String
                && !string.IsNullOrWhiteSpace(value.StringValue))
            {
                return value.StringValue.Trim();
            }
        }

        throw new ArgumentException($"缺少 CoreIntent payload 字段：{string.Join("/", propertyNames)}。", nameof(payload));
    }
}
