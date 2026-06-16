using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;
using TianShu.Execution.Runtime;

namespace TianShu.Execution.Runtime.Tests;

public sealed class ModelRouteExecutionTests
{
    [Fact]
    public async Task MaterializeModelInvocationStepAsync_ShouldCreateModelStepFromApprovedRoute()
    {
        var bridge = new ExecutionRuntimeModelRouteBridge();
        var candidate = new ModelRouteCandidateBinding(
            "candidate-openai-gpt5",
            "provider.openai",
            "openai",
            "gpt-5",
            protocol: "responses",
            endpointRef: "endpoint://provider/openai",
            secretRef: "secret://env/OPENAI_API_KEY");
        var request = CreateRequest(new ModelRoutePolicy(
            routeCandidateIds: new[] { candidate.CandidateId },
            preferredRouteId: candidate.CandidateId,
            policyId: "model.route.policy.runtime",
            routeKind: "coding",
            candidates: new[] { candidate }));

        var result = await bridge.MaterializeModelInvocationStepAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Step);
        Assert.Equal(RuntimeStepKind.ModelInvocation, result.Step!.StepKind);
        Assert.Equal(SourceIntentId, result.Step.SourceIntentId);
        Assert.Equal(SourceGraphId, result.Step.SourceGraphId);
        Assert.Equal(SourceStageId, result.Step.SourceStageId);
        Assert.Equal(SourceKernelOperationId, result.Step.SourceKernelOperationId);
        Assert.Equal("provider.openai", result.Step.ProviderModuleId);
        Assert.Equal("openai", result.Step.InputEnvelope.ProviderKey);
        Assert.Equal("gpt-5", result.Step.InputEnvelope.Model);
        Assert.Same(request.Inputs, result.Step.InputEnvelope.Inputs);
        Assert.Equal("candidate-openai-gpt5", result.Report?.SelectedCandidateId);
        Assert.Equal("endpoint://provider/openai", result.Report?.EndpointRef);
        Assert.True(result.Step.InputEnvelope.Metadata.TryGetValue("modelRoute.protocol", out var protocol));
        Assert.Equal("responses", protocol.GetString());
    }

    [Fact]
    public async Task MaterializeModelInvocationStepAsync_ShouldFailClosedWhenCandidateMissing()
    {
        var bridge = new ExecutionRuntimeModelRouteBridge();
        var request = CreateRequest(new ModelRoutePolicy(
            routeCandidateIds: new[] { "candidate-missing" },
            preferredRouteId: "candidate-missing",
            policyId: "model.route.policy.missing",
            routeKind: "coding"));

        var result = await bridge.MaterializeModelInvocationStepAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Step);
        Assert.Equal("model_route_missing_candidate", result.Failure?.Code);
    }

    [Fact]
    public async Task MaterializeModelInvocationStepAsync_ShouldKeepRouteDiagnosticsWithoutSecretValues()
    {
        var bridge = new ExecutionRuntimeModelRouteBridge();
        var candidate = new ModelRouteCandidateBinding(
            "candidate-openai-gpt5",
            "provider.openai",
            "openai",
            "gpt-5",
            protocol: "responses",
            endpointRef: "endpoint://provider/openai",
            secretRef: "secret://env/OPENAI_API_KEY",
            metadata: new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["safe"] = StructuredValue.FromString("diagnostic"),
            }));
        var policyMetadata = new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["diagnosticsCorrelationId"] = StructuredValue.FromString("model-route-diag-001"),
        });
        var request = CreateRequest(new ModelRoutePolicy(
            routeCandidateIds: new[] { candidate.CandidateId },
            preferredRouteId: candidate.CandidateId,
            metadata: policyMetadata,
            policyId: "model.route.policy.diagnostics",
            routeKind: "coding",
            candidates: new[] { candidate }));

        var result = await bridge.MaterializeModelInvocationStepAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("model-route-diag-001", result.Report?.DiagnosticsCorrelationId);
        Assert.DoesNotContain("SECRET_VALUE", result.Step!.InputEnvelope.Metadata.ToString(), StringComparison.Ordinal);
        Assert.False(result.Step.InputEnvelope.Metadata.TryGetValue("modelRoute.secretRef", out _));
        Assert.True(result.Step.InputEnvelope.Metadata.TryGetValue("modelRoute.diagnosticsCorrelationId", out var correlation));
        Assert.Equal("model-route-diag-001", correlation.GetString());
    }

    private static ExecutionRuntimeModelRouteRequest CreateRequest(ModelRoutePolicy policy)
        => new(
            "model-route-step",
            new ExecutionId("execution-model-route"),
            new ApprovedModelRoutePolicy(
                policy,
                SourceIntentId,
                SourceGraphId,
                SourceStageId,
                SourceKernelOperationId,
                validationRefs: new[] { "trace://validation/model-route" }),
            new ProviderConversationContext(),
            new ProviderInputItem[] { new TextProviderInputItem("hello") },
            Permission,
            new SideEffectProfile(SideEffectLevel.ExternalNetwork, affectedResources: new[] { "provider" }, requiresAudit: true),
            Budget,
            OutputContract,
            TracePolicy);

    private static readonly CoreIntentId SourceIntentId = new("intent-model-route");
    private static readonly StageGraphId SourceGraphId = new("graph-model-route");
    private static readonly StageId SourceStageId = new("stage-model-route");
    private static readonly KernelOperationId SourceKernelOperationId = new("operation-model-route");
    private static readonly PermissionEnvelope Permission = new(
        scopes: new[] { "provider.invoke" },
        grants: new[] { "test" },
        requiresHumanGate: false,
        reason: "model route test");
    private static readonly KernelBudget Budget = new(tokenBudget: 1_000, timeBudgetMs: 1_000, costBudget: 1, retryBudget: 1, toolCallBudget: 1);
    private static readonly ContractRef OutputContract = new("provider.output", "v1");
    private static readonly TracePolicy TracePolicy = new();
}
