using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Provider;

namespace TianShu.Execution.Runtime;

/// <summary>
/// Execution Runtime 的模型路由物化入口，只消费 Kernel 已批准策略。
/// Execution Runtime model-route materialization entry point; only consumes Kernel-approved policies.
/// </summary>
public interface IExecutionRuntimeModelRouteBridge
{
    Task<ExecutionRuntimeModelRouteResult> MaterializeModelInvocationStepAsync(
        ExecutionRuntimeModelRouteRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// 模型路由物化请求，携带模型调用所需的 provider-neutral 输入和 RuntimeStep 边界。
/// Model-route materialization request carrying provider-neutral inputs and RuntimeStep boundaries required for model invocation.
/// </summary>
public sealed record ExecutionRuntimeModelRouteRequest
{
    public ExecutionRuntimeModelRouteRequest(
        string stepId,
        ExecutionId executionId,
        ApprovedModelRoutePolicy approvedPolicy,
        ProviderConversationContext conversation,
        IReadOnlyList<ProviderInputItem> inputs,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        KernelBudget budget,
        ContractRef expectedOutputContract,
        TracePolicy tracePolicy,
        MetadataBag? metadata = null)
    {
        StepId = IdentifierGuard.AgainstNullOrWhiteSpace(stepId, nameof(stepId));
        ExecutionId = executionId;
        ApprovedPolicy = approvedPolicy ?? throw new ArgumentNullException(nameof(approvedPolicy));
        Conversation = conversation ?? throw new ArgumentNullException(nameof(conversation));
        Inputs = inputs is { Count: > 0 } ? inputs : throw new ArgumentException("模型路由物化至少需要一个 provider-neutral 输入。", nameof(inputs));
        Permission = permission ?? throw new ArgumentNullException(nameof(permission));
        SideEffect = sideEffect ?? throw new ArgumentNullException(nameof(sideEffect));
        Budget = budget ?? throw new ArgumentNullException(nameof(budget));
        ExpectedOutputContract = expectedOutputContract ?? throw new ArgumentNullException(nameof(expectedOutputContract));
        TracePolicy = tracePolicy ?? throw new ArgumentNullException(nameof(tracePolicy));
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string StepId { get; }

    public ExecutionId ExecutionId { get; }

    public ApprovedModelRoutePolicy ApprovedPolicy { get; }

    public ProviderConversationContext Conversation { get; }

    public IReadOnlyList<ProviderInputItem> Inputs { get; }

    public PermissionEnvelope Permission { get; }

    public SideEffectProfile SideEffect { get; }

    public KernelBudget Budget { get; }

    public ContractRef ExpectedOutputContract { get; }

    public TracePolicy TracePolicy { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 模型路由物化结果，返回 RuntimeStep 或 fail-closed failure。
/// Model-route materialization result returning a RuntimeStep or fail-closed failure.
/// </summary>
public sealed record ExecutionRuntimeModelRouteResult
{
    public ExecutionRuntimeModelRouteResult(
        bool success,
        ModelInvocationStep? step = null,
        ModelRoutePolicyApplicationReport? report = null,
        ExecutionFailure? failure = null)
    {
        Success = success;
        Step = step;
        Report = report;
        Failure = failure;
    }

    public bool Success { get; }

    public ModelInvocationStep? Step { get; }

    public ModelRoutePolicyApplicationReport? Report { get; }

    public ExecutionFailure? Failure { get; }
}

/// <summary>
/// 默认模型路由桥接器，负责从已批准候选中选择可用候选并生成 ModelInvocationStep。
/// Default model-route bridge that selects an available approved candidate and creates a ModelInvocationStep.
/// </summary>
public sealed class ExecutionRuntimeModelRouteBridge : IExecutionRuntimeModelRouteBridge
{
    public Task<ExecutionRuntimeModelRouteResult> MaterializeModelInvocationStepAsync(
        ExecutionRuntimeModelRouteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var policy = request.ApprovedPolicy.Policy;
        var candidate = SelectCandidate(policy);
        if (candidate is null)
        {
            return Task.FromResult(Fail("model_route_missing_candidate", "ModelRoutePolicy 没有可用的已批准候选。"));
        }

        if (!string.IsNullOrWhiteSpace(candidate.UnavailableReason))
        {
            return Task.FromResult(Fail("model_route_candidate_unavailable", $"Model route candidate `{candidate.CandidateId}` 不可用。"));
        }

        var metadata = MergeMetadata(request.Metadata, policy, candidate);
        var providerRequest = new ProviderInvocationRequest(
            request.ExecutionId,
            candidate.ProviderKey,
            candidate.Model,
            request.Conversation,
            request.Inputs,
            metadata: metadata);
        var step = new ModelInvocationStep(
            request.StepId,
            request.ApprovedPolicy.SourceIntentId,
            request.ApprovedPolicy.SourceGraphId,
            request.ApprovedPolicy.SourceStageId,
            request.ApprovedPolicy.SourceKernelOperationId,
            candidate.ProviderModuleId,
            policy,
            providerRequest,
            request.Permission,
            request.SideEffect,
            request.Budget,
            request.ExpectedOutputContract,
            request.TracePolicy,
            metadata);
        var report = new ModelRoutePolicyApplicationReport(
            policy.PolicyId,
            policy.RouteKind,
            candidate.CandidateId,
            candidate.ProviderModuleId,
            candidate.ProviderKey,
            candidate.Model,
            candidate.CandidateIndex,
            candidate.Protocol,
            candidate.EndpointRef,
            ReadString(policy.Metadata, "diagnosticsCorrelationId"),
            policy.Candidates
                .Where(item => !string.IsNullOrWhiteSpace(item.UnavailableReason))
                .Select(item => item.CandidateId)
                .ToArray());

        return Task.FromResult(new ExecutionRuntimeModelRouteResult(true, step, report));
    }

    private static ModelRouteCandidateBinding? SelectCandidate(ModelRoutePolicy policy)
    {
        if (policy.Candidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(policy.PreferredRouteId))
        {
            return policy.Candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.CandidateId, policy.PreferredRouteId, StringComparison.Ordinal));
        }

        var orderedCandidateIds = policy.RouteCandidateIds.Count == 0
            ? policy.Candidates.Select(static candidate => candidate.CandidateId).ToArray()
            : policy.RouteCandidateIds;
        foreach (var candidateId in orderedCandidateIds)
        {
            var candidate = policy.Candidates.FirstOrDefault(item =>
                string.Equals(item.CandidateId, candidateId, StringComparison.Ordinal));
            if (candidate is not null && string.IsNullOrWhiteSpace(candidate.UnavailableReason))
            {
                return candidate;
            }
        }

        return policy.Candidates.FirstOrDefault(static candidate => string.IsNullOrWhiteSpace(candidate.UnavailableReason));
    }

    private static ExecutionRuntimeModelRouteResult Fail(string code, string message)
        => new(false, failure: new ExecutionFailure(code, message));

    private static MetadataBag MergeMetadata(MetadataBag requestMetadata, ModelRoutePolicy policy, ModelRouteCandidateBinding candidate)
    {
        var entries = new Dictionary<string, StructuredValue>(requestMetadata.Entries, StringComparer.Ordinal)
        {
            ["modelRoute.policyId"] = StructuredValue.FromString(policy.PolicyId),
            ["modelRoute.candidateId"] = StructuredValue.FromString(candidate.CandidateId),
            ["modelRoute.providerModuleId"] = StructuredValue.FromString(candidate.ProviderModuleId),
            ["modelRoute.providerKey"] = StructuredValue.FromString(candidate.ProviderKey),
            ["modelRoute.model"] = StructuredValue.FromString(candidate.Model),
            ["modelRoute.candidateIndex"] = StructuredValue.FromNumber(candidate.CandidateIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };

        if (!string.IsNullOrWhiteSpace(policy.RouteKind))
        {
            entries["modelRoute.routeKind"] = StructuredValue.FromString(policy.RouteKind);
        }

        if (!string.IsNullOrWhiteSpace(candidate.Protocol))
        {
            entries["modelRoute.protocol"] = StructuredValue.FromString(candidate.Protocol);
        }

        if (!string.IsNullOrWhiteSpace(candidate.EndpointRef))
        {
            entries["modelRoute.endpointRef"] = StructuredValue.FromString(candidate.EndpointRef);
        }

        if (ReadString(policy.Metadata, "diagnosticsCorrelationId") is { } diagnosticsCorrelationId)
        {
            entries["modelRoute.diagnosticsCorrelationId"] = StructuredValue.FromString(diagnosticsCorrelationId);
        }

        return new MetadataBag(entries);
    }

    private static string? ReadString(MetadataBag metadata, string key)
        => metadata.TryGetValue(key, out var value) ? value.GetString() : null;
}
