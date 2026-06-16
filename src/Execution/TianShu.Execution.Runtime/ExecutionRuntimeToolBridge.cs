using TianShu.Contracts.Execution;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;
using System.Diagnostics;

namespace TianShu.Execution.Runtime;

public interface IExecutionRuntimeToolBridge
{
    Task<RuntimeStepResult> ExecuteAsync(
        ToolInvocationStep step,
        ExecutionRuntimeContext context,
        ITianShuTool tool,
        CancellationToken cancellationToken);
}

public sealed class ExecutionRuntimeToolBridge : IExecutionRuntimeToolBridge
{
    private readonly IExecutionRuntimeMetricsSink? metricsSink;

    public ExecutionRuntimeToolBridge(IExecutionRuntimeMetricsSink? metricsSink = null)
    {
        this.metricsSink = metricsSink;
    }

    public async Task<RuntimeStepResult> ExecuteAsync(
        ToolInvocationStep step,
        ExecutionRuntimeContext context,
        ITianShuTool tool,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tool);

        var governanceFailure = TianShuExecutionRuntime.ValidateStep(step, context);
        if (governanceFailure is not null)
        {
            return CreateBlockedResult(step, context, governanceFailure);
        }

        if (!string.Equals(step.InputEnvelope.ToolId, tool.Descriptor.ToolId, StringComparison.Ordinal))
        {
            return CreateBlockedResult(
                step,
                context,
                new ExecutionFailure("tool_descriptor_mismatch", "ToolInvocationStep 的工具标识与 ToolDescriptor 不一致。"));
        }

        if (!tool.Descriptor.IsAllowedBy(context.Governance))
        {
            return CreateBlockedResult(
                step,
                context,
                new ExecutionFailure("tool_descriptor_not_allowed_by_governance", "工具描述符不满足 GovernanceEnvelope 的工具、副作用或人工 gate 边界。"));
        }

        var stopwatch = Stopwatch.StartNew();
        ToolInvocationResult result;
        try
        {
            result = await tool.InvokeAsync(
                    step.InputEnvelope,
                    new ToolInvocationContext(
                        step.StepId,
                        step.SourceIntentId.Value,
                        step.SourceGraphId.Value,
                        step.SourceStageId.Value,
                        step.SourceKernelOperationId.Value,
                        context.WorkingDirectory,
                        step.Metadata),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            await RecordMetricsAsync(step, context, stopwatch.Elapsed, CancellationToken.None).ConfigureAwait(false);
            return CreateTerminalToolResult(
                step,
                context,
                RuntimeStepResultStatus.Cancelled,
                "cancelled",
                new ExecutionFailure("tool_invocation_cancelled", "工具调用已取消。"));
        }

        stopwatch.Stop();

        if (result.Failure is not null)
        {
            await RecordMetricsAsync(step, context, stopwatch.Elapsed, cancellationToken).ConfigureAwait(false);
            return CreateFailedToolResult(step, context, result);
        }

        var stepResult = new RuntimeStepResult(
            step.StepId,
            RuntimeStepKind.ToolInvocation,
            RuntimeStepResultStatus.Succeeded,
            CreateToolResultOutput(step, context, result, "succeeded", failure: null),
            diagnosticsRef: $"diagnostics://execution/{context.ExecutionId.Value}/{step.StepId}/tool",
            traceRef: $"trace://execution/{context.ExecutionId.Value}/{step.StepId}/tool");
        await RecordMetricsAsync(step, context, stopwatch.Elapsed, cancellationToken).ConfigureAwait(false);
        return stepResult;
    }

    private static RuntimeStepResult CreateFailedToolResult(
        ToolInvocationStep step,
        ExecutionRuntimeContext context,
        ToolInvocationResult result)
    {
        var failure = result.Failure!;
        var status = ClassifyToolFailureStatus(failure.Code);
        var runtimeStatus = status switch
        {
            "blocked" or "approval-required" => RuntimeStepResultStatus.Blocked,
            "cancelled" => RuntimeStepResultStatus.Cancelled,
            _ => RuntimeStepResultStatus.Failed,
        };
        var executionFailure = new ExecutionFailure(failure.Code, failure.Message, failure.IsRetryable);
        return new RuntimeStepResult(
            step.StepId,
            RuntimeStepKind.ToolInvocation,
            runtimeStatus,
            CreateToolResultOutput(step, context, result, status, executionFailure),
            executionFailure,
            diagnosticsRef: $"diagnostics://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}",
            traceRef: $"trace://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}");
    }

    private static RuntimeStepResult CreateTerminalToolResult(
        ToolInvocationStep step,
        ExecutionRuntimeContext context,
        RuntimeStepResultStatus runtimeStatus,
        string toolStatus,
        ExecutionFailure failure)
        => new(
            step.StepId,
            RuntimeStepKind.ToolInvocation,
            runtimeStatus,
            CreateToolResultOutput(step, context, toolStatus, failure),
            failure,
            diagnosticsRef: $"diagnostics://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}",
            traceRef: $"trace://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}");

    private static StructuredValue CreateToolResultOutput(
        ToolInvocationStep step,
        ExecutionRuntimeContext context,
        ToolInvocationResult result,
        string status,
        ExecutionFailure? failure)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["runtimeBoundary"] = "execution.runtime.tool_bridge",
            ["runtimePlanId"] = ResolvePlanId(step, context),
            ["stepId"] = step.StepId,
            ["stepKind"] = step.StepKind.ToString(),
            ["sourceIntentId"] = step.SourceIntentId.Value,
            ["sourceGraphId"] = step.SourceGraphId.Value,
            ["sourceStageId"] = step.SourceStageId.Value,
            ["sourceKernelOperationId"] = step.SourceKernelOperationId.Value,
            ["signals"] = new[] { "tool.results.materialized", $"tool.result.{status}" },
            ["toolId"] = result.ToolKey,
            ["streamItemCount"] = result.StreamItems.Count,
            ["outputContentItemCount"] = result.OutputContentItems.Count,
            ["auditRef"] = $"audit://execution/{context.ExecutionId.Value}/{step.StepId}/tool/{result.CallId.Value}",
            ["toolResults"] = new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["callId"] = result.CallId.Value,
                    ["toolId"] = result.ToolKey,
                    ["status"] = status,
                    ["output"] = CreateToolOutput(result),
                    ["failure"] = failure is null ? null : CreateFailureOutput(failure),
                    ["auditRef"] = $"audit://execution/{context.ExecutionId.Value}/{step.StepId}/tool/{result.CallId.Value}",
                },
            },
        });

    private static StructuredValue CreateToolResultOutput(
        ToolInvocationStep step,
        ExecutionRuntimeContext context,
        string status,
        ExecutionFailure failure)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["runtimeBoundary"] = "execution.runtime.tool_bridge",
            ["runtimePlanId"] = ResolvePlanId(step, context),
            ["stepId"] = step.StepId,
            ["stepKind"] = step.StepKind.ToString(),
            ["sourceIntentId"] = step.SourceIntentId.Value,
            ["sourceGraphId"] = step.SourceGraphId.Value,
            ["sourceStageId"] = step.SourceStageId.Value,
            ["sourceKernelOperationId"] = step.SourceKernelOperationId.Value,
            ["signals"] = new[] { "tool.results.materialized", $"tool.result.{status}" },
            ["toolId"] = step.InputEnvelope.ToolId,
            ["streamItemCount"] = 0,
            ["outputContentItemCount"] = 0,
            ["auditRef"] = $"audit://execution/{context.ExecutionId.Value}/{step.StepId}/tool/{step.InputEnvelope.CallId.Value}",
            ["toolResults"] = new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["callId"] = step.InputEnvelope.CallId.Value,
                    ["toolId"] = step.InputEnvelope.ToolId,
                    ["status"] = status,
                    ["output"] = new Dictionary<string, object?>
                    {
                        ["streamItems"] = Array.Empty<object>(),
                        ["outputContentItems"] = Array.Empty<object>(),
                        ["outputArtifactId"] = null,
                    },
                    ["failure"] = CreateFailureOutput(failure),
                    ["auditRef"] = $"audit://execution/{context.ExecutionId.Value}/{step.StepId}/tool/{step.InputEnvelope.CallId.Value}",
                },
            },
        });

    private static Dictionary<string, object?> CreateToolOutput(ToolInvocationResult result)
        => new(StringComparer.Ordinal)
        {
            ["streamItems"] = result.StreamItems.Select(static item => new Dictionary<string, object?>
            {
                ["channel"] = item.Channel,
                ["payload"] = item.Payload,
                ["isTerminal"] = item.IsTerminal,
            }).ToArray(),
            ["outputContentItems"] = result.OutputContentItems.Select(static item => new Dictionary<string, object?>
            {
                ["type"] = item.Type,
                ["text"] = item.Text,
                ["imageUrl"] = item.ImageUrl,
                ["detail"] = item.Detail,
            }).ToArray(),
            ["outputArtifactId"] = result.OutputArtifact?.Id.Value,
        };

    private static Dictionary<string, object?> CreateFailureOutput(ExecutionFailure failure)
        => new(StringComparer.Ordinal)
        {
            ["code"] = failure.Code,
            ["message"] = failure.Message,
            ["isRetryable"] = failure.IsRetryable,
        };

    private static RuntimeStepResult CreateBlockedResult(
        ToolInvocationStep step,
        ExecutionRuntimeContext context,
        ExecutionFailure failure)
        => new(
            step.StepId,
            RuntimeStepKind.ToolInvocation,
            RuntimeStepResultStatus.Blocked,
            CreateToolResultOutput(step, context, ClassifyGovernanceFailureStatus(failure.Code), failure),
            failure: failure,
            diagnosticsRef: $"diagnostics://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}",
            traceRef: $"trace://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}");

    private static string ClassifyGovernanceFailureStatus(string failureCode)
        => failureCode.Contains("approval", StringComparison.OrdinalIgnoreCase)
            || failureCode.Contains("human_gate", StringComparison.OrdinalIgnoreCase)
            ? "approval-required"
            : "blocked";

    private static string ClassifyToolFailureStatus(string failureCode)
    {
        if (failureCode.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || failureCode.Contains("timed_out", StringComparison.OrdinalIgnoreCase))
        {
            return "timeout";
        }

        if (failureCode.Contains("cancel", StringComparison.OrdinalIgnoreCase))
        {
            return "cancelled";
        }

        if (failureCode.Contains("approval", StringComparison.OrdinalIgnoreCase)
            || failureCode.Contains("human_gate", StringComparison.OrdinalIgnoreCase))
        {
            return "approval-required";
        }

        if (failureCode.Contains("blocked", StringComparison.OrdinalIgnoreCase)
            || failureCode.Contains("not_allowed", StringComparison.OrdinalIgnoreCase)
            || failureCode.Contains("denied", StringComparison.OrdinalIgnoreCase)
            || failureCode.Contains("sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return "blocked";
        }

        return "failed";
    }

    private async ValueTask RecordMetricsAsync(
        ToolInvocationStep step,
        ExecutionRuntimeContext context,
        TimeSpan latency,
        CancellationToken cancellationToken)
    {
        if (metricsSink is null)
        {
            return;
        }

        var tokenUsage = new TokenUsageSnapshot(
            Available: false,
            MissingReason: "tool_usage_not_applicable",
            Estimated: false,
            InputTokens: null,
            CachedInputTokens: null,
            OutputTokens: null,
            ReasoningOutputTokens: null,
            TotalTokens: null,
            Source: "execution.runtime.tool_bridge");
        var cost = new RuntimeCostSnapshot(false, "token_usage_missing", null, null, null);

        await metricsSink.RecordAsync(
                new RuntimeMetricsEvent(
                    $"runtime-metrics:{context.ExecutionId.Value}:{step.StepId}:{Guid.NewGuid():N}",
                    context.KernelRunId.Value,
                    context.ExecutionId.Value,
                    ResolvePlanId(step, context),
                    step.SourceGraphId.Value,
                    step.SourceStageId.Value,
                    step.StepId,
                    step.InputEnvelope.ToolId,
                    attemptIndex: 1,
                    reviseRound: null,
                    tokenUsage,
                    cost,
                    modelCallCount: 0,
                    latency,
                    ["tool_usage_not_applicable", "token_usage_missing"]),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string ResolvePlanId(ToolInvocationStep step, ExecutionRuntimeContext context)
    {
        if (step.Metadata.TryGetValue("planId", out var stepPlanId)
            && stepPlanId.StringValue is { Length: > 0 } stepPlanIdValue)
        {
            return stepPlanIdValue;
        }

        if (context.Metadata.TryGetValue("planId", out var contextPlanId)
            && contextPlanId.StringValue is { Length: > 0 } contextPlanIdValue)
        {
            return contextPlanIdValue;
        }

        return "runtime-plan-unknown";
    }
}
