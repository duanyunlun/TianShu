using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Kernel.Abstractions;
using TianShu.Kernel.Adaptive.Tools;

namespace TianShu.Kernel.Adaptive;

/// <summary>
/// 默认 Adaptive StageGraph 候选生成器，通过 KernelTool 生成多个结构化 StageGraph proposal。
/// Default Adaptive StageGraph candidate generator that uses KernelTool to produce multiple structured StageGraph proposals.
/// </summary>
public sealed class DefaultAdaptiveStageGraphCandidateGenerator : IAdaptiveStageGraphCandidateGenerator
{
    private static readonly IReadOnlyList<AdaptiveStageGraphCandidateProfile> DefaultCandidateProfiles =
    [
        new("direct", "Direct readonly stage graph candidate.", "route.default", "module.core_loop", 2_048, "proposal_validity"),
        new("context_guarded", "Context-guarded readonly stage graph candidate.", "route.context_guarded", "module.core_loop", 4_096, "context_policy_fit"),
        new("recovery_checked", "Recovery-aware readonly stage graph candidate.", "route.recovery_checked", "module.core_loop", 3_072, "recovery_readiness"),
    ];

    private readonly IKernelTool composeStageGraphTool;

    public DefaultAdaptiveStageGraphCandidateGenerator(IKernelTool? composeStageGraphTool = null)
    {
        this.composeStageGraphTool = composeStageGraphTool ?? new ComposeStageGraphKernelTool();
    }

    public async Task<IReadOnlyList<StageGraphProposal>> GenerateCandidatesAsync(
        CoreIntent intent,
        KernelRunState state,
        KernelRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(options);

        var proposals = new List<StageGraphProposal>(DefaultCandidateProfiles.Count);
        foreach (var profile in DefaultCandidateProfiles)
        {
            var result = await composeStageGraphTool.InvokeKernelAsync(
                new KernelToolInvocation(intent, state, options, profile.ToInput(options.PreferredGraphId)),
                cancellationToken).ConfigureAwait(false);

            if (result.Proposal is StageGraphProposal proposal)
            {
                proposals.Add(proposal);
            }
        }

        return proposals;
    }

    private sealed record AdaptiveStageGraphCandidateProfile(
        string VariantId,
        string Objective,
        string RouteId,
        string CapabilityToolId,
        int MaxInputTokens,
        string EvaluationMetricId)
    {
        public StructuredValue ToInput(StageGraphId? preferredGraphId)
        {
            var graphId = preferredGraphId is null
                ? $"graph.adaptive.{VariantId}"
                : $"{preferredGraphId.Value.Value}.{VariantId}";

            return StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["variantId"] = VariantId,
                ["graphId"] = graphId,
                ["objective"] = Objective,
                ["routeId"] = RouteId,
                ["capabilityToolId"] = CapabilityToolId,
                ["maxInputTokens"] = MaxInputTokens,
                ["evaluationMetricId"] = EvaluationMetricId,
            });
        }
    }
}
