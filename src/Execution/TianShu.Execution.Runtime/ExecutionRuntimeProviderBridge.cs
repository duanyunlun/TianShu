using TianShu.Contracts.Execution;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;
using TianShu.Provider.Abstractions;
using System.Diagnostics;

namespace TianShu.Execution.Runtime;

public interface IExecutionRuntimeProviderBridge
{
    Task<RuntimeStepResult> ExecuteAsync(
        ModelInvocationStep step,
        ExecutionRuntimeContext context,
        IProviderModule provider,
        CancellationToken cancellationToken);
}

public sealed class ExecutionRuntimeProviderBridge : IExecutionRuntimeProviderBridge
{
    private const int MaxModelAttemptCount = 5;
    private static readonly TimeSpan DefaultModelAttemptTimeout = TimeSpan.FromSeconds(30);

    private readonly IExecutionRuntimeMetricsSink? metricsSink;

    public ExecutionRuntimeProviderBridge(IExecutionRuntimeMetricsSink? metricsSink = null)
    {
        this.metricsSink = metricsSink;
    }

    public async Task<RuntimeStepResult> ExecuteAsync(
        ModelInvocationStep step,
        ExecutionRuntimeContext context,
        IProviderModule provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(provider);

        var governanceFailure = TianShuExecutionRuntime.ValidateStep(step, context);
        if (governanceFailure is not null)
        {
            return CreateBlockedResult(step, context, governanceFailure);
        }

        if (!string.Equals(step.ProviderModuleId, provider.Descriptor.ProviderId, StringComparison.Ordinal))
        {
            return CreateBlockedResult(
                step,
                context,
                new ExecutionFailure("provider_descriptor_mismatch", "ModelInvocationStep 的 provider 标识与 ProviderDescriptor 不一致。"));
        }

        var request = WithInvocationContext(step);
        var maxAttempts = ResolveMaxAttemptCount(step);
        var attemptTimeout = ResolveAttemptTimeout(step);
        ProviderAttemptResult? lastAttempt = null;
        var totalEventCount = 0;

        for (var attemptIndex = 1; attemptIndex <= maxAttempts; attemptIndex++)
        {
            var attempt = await ExecuteProviderAttemptAsync(
                    provider,
                    request,
                    attemptTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
            lastAttempt = attempt;
            totalEventCount += attempt.EventCount;

            await RecordMetricsAsync(
                    step,
                    context,
                    provider.Descriptor.ProviderId,
                    request.Model,
                    attempt.Usage,
                    attempt.Latency,
                    attemptIndex,
                    attempt.Failure?.Code,
                    cancellationToken)
                .ConfigureAwait(false);

            if (attempt.Failure is null)
            {
                return CreateSucceededResult(
                    step,
                    context,
                    provider.Descriptor.ProviderId,
                    request,
                    attempt,
                    totalEventCount,
                    attemptIndex,
                    maxAttempts);
            }

            if (!attempt.Failure.IsRetryable || attemptIndex == maxAttempts)
            {
                var failure = CreateFinalProviderFailure(attempt.Failure, attemptIndex, maxAttempts);
                return CreateFailedResult(step, context, failure, attemptIndex, maxAttempts);
            }
        }

        return CreateFailedResult(
            step,
            context,
            new ExecutionFailure(
                "provider_retry_budget_exhausted",
                "Provider 调用重试预算耗尽。",
                isRetryable: false,
                details: CreateProviderFailureDetails(lastAttempt?.Failure, maxAttempts, maxAttempts)),
            maxAttempts,
            maxAttempts);
    }

    private static async Task<ProviderAttemptResult> ExecuteProviderAttemptAsync(
        IProviderModule provider,
        ProviderInvocationRequest request,
        TimeSpan attemptTimeout,
        CancellationToken cancellationToken)
    {
        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptCts.CancelAfter(attemptTimeout);

        var stopwatch = Stopwatch.StartNew();
        var eventCount = 0;
        ProviderUsage? usage = null;
        ProviderCompletion? completion = null;
        var toolDirectives = new List<ProviderToolDirective>();
        var toolSurfaceNames = Array.Empty<string>();
        string? toolSurfaceWireApi = null;
        try
        {
            await foreach (var streamEvent in provider.InvokeAsync(request, attemptCts.Token).WithCancellation(attemptCts.Token).ConfigureAwait(false))
            {
                eventCount++;
                if (streamEvent is ProviderCompletionEvent completionEvent)
                {
                    completion = completionEvent.Completion;
                    usage = completionEvent.Completion.Usage;
                }

                if (streamEvent is ProviderToolDirectiveEvent toolDirectiveEvent)
                {
                    toolDirectives.Add(toolDirectiveEvent.Directive);
                }

                if (streamEvent is ProviderToolSurfaceEvent toolSurfaceEvent)
                {
                    toolSurfaceNames = toolSurfaceEvent.ToolNames
                        .Where(static name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    toolSurfaceWireApi = toolSurfaceEvent.WireApi;
                }

                if (streamEvent is ProviderFailureEvent failureEvent)
                {
                    stopwatch.Stop();
                    return new ProviderAttemptResult(eventCount, usage, stopwatch.Elapsed, failureEvent.Failure, completion, toolDirectives, toolSurfaceNames, toolSurfaceWireApi);
                }
            }

            stopwatch.Stop();
            return new ProviderAttemptResult(eventCount, usage, stopwatch.Elapsed, Failure: null, completion, toolDirectives, toolSurfaceNames, toolSurfaceWireApi);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attemptCts.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new ProviderAttemptResult(
                eventCount,
                usage,
                stopwatch.Elapsed,
                new ProviderFailure(
                    "provider_model_attempt_timeout",
                    $"Provider 模型调用超过单次限时 {attemptTimeout.TotalMilliseconds:0} ms。",
                    isRetryable: true),
                completion,
                toolDirectives,
                toolSurfaceNames,
                toolSurfaceWireApi);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new ProviderAttemptResult(
                eventCount,
                usage,
                stopwatch.Elapsed,
                new ProviderFailure(
                    "provider_invocation_exception",
                    exception.Message,
                    isRetryable: true,
                    additionalDetails: exception.GetType().FullName),
                completion,
                toolDirectives,
                toolSurfaceNames,
                toolSurfaceWireApi);
        }
    }

    private static int ResolveMaxAttemptCount(ModelInvocationStep step)
        => Math.Max(1, Math.Min(step.Budget.RetryBudget <= 0 ? 1 : step.Budget.RetryBudget, MaxModelAttemptCount));

    private static TimeSpan ResolveAttemptTimeout(ModelInvocationStep step)
    {
        if (step.Budget.TimeBudgetMs <= 0)
        {
            return DefaultModelAttemptTimeout;
        }

        return TimeSpan.FromMilliseconds(Math.Max(1, Math.Min(step.Budget.TimeBudgetMs, (long)DefaultModelAttemptTimeout.TotalMilliseconds)));
    }

    private static ExecutionFailure CreateFinalProviderFailure(ProviderFailure failure, int attemptCount, int maxAttemptCount)
    {
        if (failure.IsRetryable && maxAttemptCount > 1 && attemptCount >= maxAttemptCount)
        {
            return new ExecutionFailure(
                "provider_retry_budget_exhausted",
                "Provider 调用重试预算耗尽。",
                isRetryable: false,
                details: CreateProviderFailureDetails(failure, attemptCount, maxAttemptCount));
        }

        return new ExecutionFailure(
            failure.Code,
            failure.Message,
            failure.IsRetryable,
            CreateProviderFailureDetails(failure, attemptCount, maxAttemptCount));
    }

    private static StructuredValue CreateProviderFailureDetails(ProviderFailure? failure, int attemptCount, int maxAttemptCount)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["attemptCount"] = attemptCount,
            ["maxAttemptCount"] = maxAttemptCount,
            ["lastFailureCode"] = failure?.Code,
            ["lastFailureRetryable"] = failure?.IsRetryable,
            ["lastFailureDetails"] = failure?.AdditionalDetails,
        });

    private static RuntimeStepResult CreateSucceededResult(
        ModelInvocationStep step,
        ExecutionRuntimeContext context,
        string providerId,
        ProviderInvocationRequest request,
        ProviderAttemptResult attempt,
        int totalEventCount,
        int attemptCount,
        int maxAttemptCount)
    {
        var output = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["runtimeBoundary"] = "execution.runtime.provider_bridge",
            ["runtimePlanId"] = ResolvePlanId(step, context),
            ["stepId"] = step.StepId,
            ["stepKind"] = step.StepKind.ToString(),
            ["sourceIntentId"] = step.SourceIntentId.Value,
            ["sourceGraphId"] = step.SourceGraphId.Value,
            ["sourceStageId"] = step.SourceStageId.Value,
            ["sourceKernelOperationId"] = step.SourceKernelOperationId.Value,
            ["providerId"] = providerId,
            ["providerKey"] = request.ProviderKey,
            ["model"] = request.Model,
            ["streamEventCount"] = totalEventCount,
            ["providerAttemptCount"] = attemptCount,
            ["providerRetryCount"] = Math.Max(0, attemptCount - 1),
            ["providerMaxAttemptCount"] = maxAttemptCount,
            ["providerToolSurface"] = CreateProviderToolSurface(attempt),
        };

        if (attempt.ToolDirectives.Count > 0)
        {
            output["signals"] = new[] { "tool_requests_available" };
            output["toolRequests"] = attempt.ToolDirectives.Select(static directive => new Dictionary<string, object?>
            {
                ["callId"] = directive.CallId.Value,
                ["toolId"] = directive.ToolKey,
                ["operation"] = ResolveToolOperation(directive.Input),
                ["input"] = directive.Input,
            }).ToArray();
        }
        else
        {
            output["signals"] = new[] { "model_final_response" };
            output["assistantText"] = attempt.Completion?.OutputText;
        }

        return new RuntimeStepResult(
            step.StepId,
            RuntimeStepKind.ModelInvocation,
            RuntimeStepResultStatus.Succeeded,
            StructuredValue.FromPlainObject(output),
            diagnosticsRef: $"diagnostics://execution/{context.ExecutionId.Value}/{step.StepId}/provider",
            traceRef: $"trace://execution/{context.ExecutionId.Value}/{step.StepId}/provider");
    }

    private static string ResolveToolOperation(StructuredValue input)
        => input.TryGetProperty("operation", out var operation)
            && operation is not null
            && operation.Kind is StructuredValueKind.String or StructuredValueKind.Number
            && !string.IsNullOrWhiteSpace(operation.GetString())
            ? operation.GetString()!
            : "run";

    private static Dictionary<string, object?> CreateProviderToolSurface(ProviderAttemptResult attempt)
    {
        var names = attempt.ToolSurfaceNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["available"] = !string.IsNullOrWhiteSpace(attempt.ToolSurfaceWireApi) || names.Length > 0,
            ["wireApi"] = attempt.ToolSurfaceWireApi,
            ["count"] = names.Length,
            ["names"] = names,
            ["hasSpawnAgent"] = names.Contains("spawn_agent", StringComparer.Ordinal),
        };
    }

    private static ProviderInvocationRequest WithInvocationContext(ModelInvocationStep step)
        => new(
            step.InputEnvelope.ExecutionId,
            step.InputEnvelope.ProviderKey,
            step.InputEnvelope.Model,
            step.InputEnvelope.Conversation,
            step.InputEnvelope.Inputs,
            step.InputEnvelope.PreviousTurnState,
            step.InputEnvelope.Metadata,
            new ProviderInvocationContext(
                step.StepId,
                step.SourceIntentId.Value,
                step.SourceGraphId.Value,
                step.SourceStageId.Value,
                step.SourceKernelOperationId.Value,
                step.Permission,
                step.SideEffect,
                step.Metadata));

    private static RuntimeStepResult CreateBlockedResult(
        ModelInvocationStep step,
        ExecutionRuntimeContext context,
        ExecutionFailure failure)
        => new(
            step.StepId,
            RuntimeStepKind.ModelInvocation,
            RuntimeStepResultStatus.Blocked,
            failure: failure,
            diagnosticsRef: $"diagnostics://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}",
            traceRef: $"trace://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}");

    private static RuntimeStepResult CreateFailedResult(
        ModelInvocationStep step,
        ExecutionRuntimeContext context,
        ExecutionFailure failure,
        int attemptCount,
        int maxAttemptCount)
        => new(
            step.StepId,
            RuntimeStepKind.ModelInvocation,
            RuntimeStepResultStatus.Failed,
            StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["runtimeBoundary"] = "execution.runtime.provider_bridge",
                ["runtimePlanId"] = ResolvePlanId(step, context),
                ["stepId"] = step.StepId,
                ["stepKind"] = step.StepKind.ToString(),
                ["sourceIntentId"] = step.SourceIntentId.Value,
                ["sourceGraphId"] = step.SourceGraphId.Value,
                ["sourceStageId"] = step.SourceStageId.Value,
                ["sourceKernelOperationId"] = step.SourceKernelOperationId.Value,
                ["providerAttemptCount"] = attemptCount,
                ["providerRetryCount"] = Math.Max(0, attemptCount - 1),
                ["providerMaxAttemptCount"] = maxAttemptCount,
                ["failureCode"] = failure.Code,
            }),
            failure,
            diagnosticsRef: $"diagnostics://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}",
            traceRef: $"trace://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}");

    private async ValueTask RecordMetricsAsync(
        ModelInvocationStep step,
        ExecutionRuntimeContext context,
        string providerId,
        string modelId,
        ProviderUsage? usage,
        TimeSpan latency,
        int attemptIndex,
        string? failureCode,
        CancellationToken cancellationToken)
    {
        if (metricsSink is null)
        {
            return;
        }

        var tokenUsage = CreateTokenUsageSnapshot(usage);
        var missingReasons = new List<string>();
        if (tokenUsage.MissingReason is not null)
        {
            missingReasons.Add(tokenUsage.MissingReason);
        }

        if (!string.IsNullOrWhiteSpace(failureCode))
        {
            missingReasons.Add($"provider_failure:{failureCode}");
        }

        var cost = tokenUsage.Available && !tokenUsage.Estimated
            ? new RuntimeCostSnapshot(false, "price_model_missing", null, null, null)
            : new RuntimeCostSnapshot(false, "token_usage_missing", null, null, null);
        missingReasons.Add(cost.MissingReason!);

        await metricsSink.RecordAsync(
                new RuntimeMetricsEvent(
                    $"runtime-metrics:{context.ExecutionId.Value}:{step.StepId}:attempt-{attemptIndex}:{Guid.NewGuid():N}",
                    context.KernelRunId.Value,
                    context.ExecutionId.Value,
                    ResolvePlanId(step, context),
                    step.SourceGraphId.Value,
                    step.SourceStageId.Value,
                    step.StepId,
                    modelId,
                    attemptIndex,
                    reviseRound: null,
                    tokenUsage,
                    cost,
                    modelCallCount: 1,
                    latency,
                    missingReasons),
                cancellationToken)
            .ConfigureAwait(false);

        static TokenUsageSnapshot CreateTokenUsageSnapshot(ProviderUsage? usage)
        {
            if (usage is null)
            {
                return new TokenUsageSnapshot(
                    Available: false,
                    MissingReason: "provider_usage_missing",
                    Estimated: false,
                    InputTokens: null,
                    CachedInputTokens: null,
                    OutputTokens: null,
                    ReasoningOutputTokens: null,
                    TotalTokens: null,
                    Source: "provider.completion.usage");
            }

            var reasoningTokens = usage.ReasoningTokens ?? 0;
            return new TokenUsageSnapshot(
                Available: true,
                MissingReason: null,
                Estimated: false,
                InputTokens: usage.InputTokens,
                CachedInputTokens: null,
                OutputTokens: usage.OutputTokens,
                ReasoningOutputTokens: usage.ReasoningTokens,
                TotalTokens: usage.InputTokens + usage.OutputTokens + reasoningTokens,
                Source: "provider.completion.usage");
        }
    }

    private static string ResolvePlanId(ModelInvocationStep step, ExecutionRuntimeContext context)
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

    private sealed record ProviderAttemptResult(
        int EventCount,
        ProviderUsage? Usage,
        TimeSpan Latency,
        ProviderFailure? Failure,
        ProviderCompletion? Completion,
        IReadOnlyList<ProviderToolDirective> ToolDirectives,
        IReadOnlyList<string> ToolSurfaceNames,
        string? ToolSurfaceWireApi);
}
