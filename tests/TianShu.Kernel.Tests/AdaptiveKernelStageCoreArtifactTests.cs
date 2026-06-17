using System.Text.Json;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Kernel.Abstractions;
using TianShu.Kernel.Interpretation;
using TianShu.Kernel.Validation;

namespace TianShu.Kernel.Tests;

public sealed class AdaptiveKernelStageCoreArtifactTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task C0CalibratedJsonFixture_ShouldMapToRealStageGraphAndProduceExecutionPlan()
    {
        var candidate = LoadCandidateFixture();
        var graph = CreateStageGraph(candidate);
        var intent = CreateIntentFor(graph);
        var validator = new KernelValidator();
        var interpreter = new StageGraphInterpreter();

        var validation = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));
        var plan = await interpreter.InterpretAsync(
            graph,
            new KernelInterpreterContext(
                intent,
                new KernelRunState(new KernelRunId("run-c0-stage-core"), intent.IntentId, selectedGraphId: graph.GraphId),
                new KernelRunOptions(runId: new KernelRunId("run-c0-stage-core"), enableAdaptive: false, requireHumanGate: false)));

        Assert.True(validation.IsApproved, string.Join(Environment.NewLine, validation.Issues.Select(static issue => $"{issue.Code}: {issue.Message}")));
        Assert.Equal(new StageGraphId("graph-c0-001"), graph.GraphId);
        Assert.Equal(CoreIntentKind.Turn, graph.IntentKind);
        Assert.Equal(new StageId("stage-1"), graph.EntryStageId);
        Assert.Equal(2, graph.Stages.Count);
        Assert.Single(graph.Edges);
        Assert.All(graph.Stages, stage => Assert.NotEqual("core_loop", stage.Kind));

        Assert.Equal(graph.GraphId, plan.SourceGraphId);
        Assert.Equal(intent.IntentId, plan.SourceIntentId);
        Assert.Equal(graph.Stages.Count, plan.Steps.Count);
        Assert.All(plan.Steps, step =>
        {
            var moduleStep = Assert.IsType<ModuleCapabilityStep>(step);
            var sourceStage = Assert.Single(graph.Stages, stage => stage.StageId == moduleStep.SourceStageId);

            Assert.Equal(intent.IntentId, moduleStep.SourceIntentId);
            Assert.Equal(graph.GraphId, moduleStep.SourceGraphId);
            Assert.Equal(sourceStage.Kind, moduleStep.CapabilityId);
            Assert.Equal(sourceStage.SideEffectLevel, moduleStep.SideEffect.Level);
            Assert.Equal(sourceStage.OutputContract, moduleStep.ExpectedOutputContract);
            Assert.Equal(sourceStage.Budget, moduleStep.Budget);
            Assert.True(moduleStep.TracePolicy.Enabled);
            Assert.True(moduleStep.TracePolicy.RequireDiagnosticsRef);
            Assert.True(moduleStep.TracePolicy.RequireRuntimeTraceRef);
            Assert.False(moduleStep.Permission.RequiresHumanGate);
            Assert.Equal(sourceStage.AllowedCapabilityToolIds, moduleStep.Permission.Scopes);
        });
    }

    [Fact]
    public async Task C0MappedStageGraph_ShouldFailClosedWhenGraphPolicyDoesNotAllowStageCapability()
    {
        var candidate = LoadCandidateFixture();
        var graph = CreateStageGraph(candidate);
        var restrictedGraph = CreateGraphWithPolicies(
            graph,
            new GraphPolicySet(
                PolicyEnforcementMode.AllowListed,
                allowedCapabilityToolIds: ["model.invoke.initial"],
                maxSideEffectLevel: SideEffectLevel.None,
                requiresHumanGate: false));
        var validator = new KernelValidator();
        var result = await validator.ValidateGraphAsync(restrictedGraph, new KernelValidationContext(CreateIntentFor(graph), graph: restrictedGraph));

        Assert.False(result.IsApproved);
        Assert.Contains(result.Issues, static issue => issue.Code == "kernel.stage.capability_tool_not_allowed");
    }

    [Fact]
    public async Task C0MappedStageGraph_ShouldFailClosedWhenHumanGateIsNotGranted()
    {
        var candidate = LoadCandidateFixture();
        var graph = CreateStageGraph(candidate);
        var gatedGraph = CreateGraphWithPolicies(
            graph,
            new GraphPolicySet(
                PolicyEnforcementMode.AllowListed,
                allowedCapabilityToolIds: graph.Policies.AllowedCapabilityToolIds,
                maxSideEffectLevel: SideEffectLevel.None,
                requiresHumanGate: true));
        var validator = new KernelValidator();
        var result = await validator.ValidateGraphAsync(gatedGraph, new KernelValidationContext(CreateIntentFor(graph), graph: gatedGraph));

        Assert.False(result.IsApproved);
        Assert.Contains(result.Issues, static issue => issue.Code == "kernel.graph.human_gate_not_granted");
    }

    [Fact]
    public async Task P31_2_OldStageGraphFixtureWithAdditiveFields_ShouldMapAndValidate()
    {
        var json = File.ReadAllText(FixturePath())
            .Replace(
                "\"version\": \"1\"",
                "\"version\": \"1.1.0\", \"futureAdditiveMetadata\": { \"ignoredBy\": \"v1-loader\" }",
                StringComparison.Ordinal);
        var candidate = DeserializeCandidate(json, "p31.2-old-stagegraph-additive");
        var graph = CreateStageGraph(candidate);
        var intent = CreateIntentFor(graph);

        var validation = await new KernelValidator().ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));

        Assert.True(validation.IsApproved, string.Join(Environment.NewLine, validation.Issues.Select(static issue => $"{issue.Code}: {issue.Message}")));
        Assert.Equal("1.1.0", graph.Version);
        Assert.Equal(new StageGraphId("graph-c0-001"), graph.GraphId);
    }

    [Fact]
    public void P31_2_UnknownMajorStageGraphFixture_ShouldFailClosedBeforeMapping()
    {
        var json = File.ReadAllText(FixturePath())
            .Replace("\"version\": \"1\"", "\"version\": \"2.0.0\"", StringComparison.Ordinal);
        var candidate = DeserializeCandidate(json, "p31.2-future-stagegraph");

        var exception = Assert.Throws<InvalidDataException>(() => CreateStageGraph(candidate));

        Assert.Contains("stage_graph_fixture.version_incompatible", exception.Message, StringComparison.Ordinal);
    }

    private static C0StageGraphCandidate LoadCandidateFixture()
        => DeserializeCandidate(File.ReadAllText(FixturePath()), FixturePath());

    private static string FixturePath()
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "adaptive-kernel", "c0-calibrated-stagegraph.json");

    private static C0StageGraphCandidate DeserializeCandidate(string json, string source)
    {
        return JsonSerializer.Deserialize<C0StageGraphCandidate>(json, JsonOptions)
            ?? throw new InvalidDataException($"Cannot deserialize C0 StageGraph fixture: {source}");
    }

    private static StageGraph CreateStageGraph(C0StageGraphCandidate candidate)
    {
        var graphId = Required(candidate.GraphId, nameof(candidate.GraphId));
        var version = Required(candidate.Version, nameof(candidate.Version));
        AssertSupportedFixtureVersion(version);
        var intentKind = ParseEnum<CoreIntentKind>(candidate.IntentKind, nameof(candidate.IntentKind));
        var entryStageId = new StageId(Required(candidate.EntryStageId, nameof(candidate.EntryStageId)));
        var policies = candidate.Policies ?? throw new InvalidDataException("C0 candidate missing policies.");
        var budget = candidate.Budgets ?? throw new InvalidDataException("C0 candidate missing budgets.");
        var stages = RequiredArray(candidate.Stages, nameof(candidate.Stages))
            .Select(CreateStage)
            .ToArray();
        var edges = RequiredArray(candidate.Edges, nameof(candidate.Edges))
            .Select(static (edge, index) => new StageEdge(
                new StageEdgeId($"edge-c0-{index + 1:D2}"),
                new StageId(Required(edge.FromStageId, nameof(edge.FromStageId))),
                new StageId(Required(edge.ToStageId, nameof(edge.ToStageId))),
                new TransitionCondition("on_success"),
                new TransitionGuard(),
                ParseEnum<StageTransitionKind>(edge.TransitionKind, nameof(edge.TransitionKind))))
            .ToArray();

        return new StageGraph(
            new StageGraphId(graphId),
            version,
            intentKind,
            entryStageId,
            stages,
            edges,
            new GraphPolicySet(
                PolicyEnforcementMode.AllowListed,
                allowedCapabilityToolIds: RequiredArray(policies.AllowedCapabilityToolIds, nameof(policies.AllowedCapabilityToolIds)),
                maxSideEffectLevel: ParseEnum<SideEffectLevel>(policies.MaxSideEffectLevel, nameof(policies.MaxSideEffectLevel)),
                requiresHumanGate: policies.RequiresHumanGate),
            new KernelBudget(
                tokenBudget: budget.TokenBudget,
                timeBudgetMs: budget.TimeBudgetMs,
                toolCallBudget: budget.ToolCallBudget),
            new CheckpointRules(enabled: true, requiredStageIds: stages.Select(static stage => stage.StageId).ToArray()),
            new RecoveryRules(enabled: true, maxRecoveryAttempts: 1),
            new EvaluationRules(enabled: true, metricIds: ["evaluator.c0.stage_core"]),
            new StageGraphMetadata("adaptive-kernel-acceptance", "c0-calibrated-v2-fixture"));
    }

    private static StageNode CreateStage(C0StageCandidate stage)
    {
        var stageId = Required(stage.StageId, nameof(stage.StageId));
        var kind = Required(stage.Kind, nameof(stage.Kind));
        var capabilityToolIds = RequiredArray(stage.CapabilityToolIds, nameof(stage.CapabilityToolIds));
        var budget = stage.Budget ?? throw new InvalidDataException($"C0 stage {stageId} missing budget.");

        return new StageNode(
            new StageId(stageId),
            kind,
            Required(stage.Objective, nameof(stage.Objective)),
            new ContractRef($"contract.{kind}.input", "1"),
            new ContractRef($"contract.{kind}.output", "1"),
            Array.Empty<string>(),
            capabilityToolIds,
            new ModelRoutePolicy(routeCandidateIds: ["route.c0.acceptance"], preferredRouteId: "route.c0.acceptance"),
            new ContextPolicy(maxInputTokens: Math.Max(1, budget.TokenBudget), allowedSourceKinds: ["CurrentUserInput", "ConversationHistory", "ToolEvidence"]),
            ParseEnum<SideEffectLevel>(stage.SideEffectLevel, nameof(stage.SideEffectLevel)),
            new KernelBudget(tokenBudget: budget.TokenBudget, timeBudgetMs: budget.TimeBudgetMs, toolCallBudget: budget.ToolCallBudget),
            new SuccessCriteria([$"{kind}.completed"]),
            new FailureHandlerRef("handler.c0.stage_core.recover", mayRecover: true));
    }

    private static TurnIntent CreateIntentFor(StageGraph graph)
        => new(
            new CoreIntentId("intent-c0-stage-core"),
            new KernelSubjectRef(new SessionId("session-c0-stage-core"), new ThreadId("thread-c0-stage-core"), turnId: new TurnId("turn-c0-stage-core")),
            new GovernanceEnvelope(
                "governance-c0-stage-core",
                allowedToolIds: graph.Policies.AllowedCapabilityToolIds,
                maxSideEffectLevel: graph.Policies.MaxSideEffectLevel,
                requiresHumanGate: false),
            "c0-stage-core-fixture",
            graph.Budgets);

    private static StageGraph CreateGraphWithPolicies(StageGraph graph, GraphPolicySet policies)
        => new(
            graph.GraphId,
            graph.Version,
            graph.IntentKind,
            graph.EntryStageId,
            graph.Stages,
            graph.Edges,
            policies,
            graph.Budgets,
            graph.CheckpointRules,
            graph.RecoveryRules,
            graph.EvaluationRules,
            graph.Metadata);

    private static TEnum ParseEnum<TEnum>(string? value, string fieldName)
        where TEnum : struct
    {
        var text = Required(value, fieldName);
        if (!Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed))
        {
            throw new InvalidDataException($"Invalid {fieldName}: {text}");
        }

        return parsed;
    }

    private static void AssertSupportedFixtureVersion(string version)
    {
        if (!Version.TryParse(version, out var parsed)
            && int.TryParse(version, out var majorOnly))
        {
            parsed = new Version(majorOnly, 0);
        }

        if (parsed is null)
        {
            throw new InvalidDataException($"stage_graph_fixture.version_invalid: {version}");
        }

        if (parsed.Major != 1)
        {
            throw new InvalidDataException($"stage_graph_fixture.version_incompatible: {version}");
        }
    }

    private static string Required(string? value, string fieldName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"C0 candidate missing {fieldName}.")
            : value.Trim();

    private static IReadOnlyList<T> RequiredArray<T>(IReadOnlyList<T>? values, string fieldName)
        => values is null || values.Count == 0
            ? throw new InvalidDataException($"C0 candidate missing {fieldName}.")
            : values;

    private sealed record C0StageGraphCandidate
    {
        public string? GraphId { get; init; }

        public string? Version { get; init; }

        public string? IntentKind { get; init; }

        public string? EntryStageId { get; init; }

        public IReadOnlyList<C0StageCandidate>? Stages { get; init; }

        public IReadOnlyList<C0EdgeCandidate>? Edges { get; init; }

        public C0PolicyCandidate? Policies { get; init; }

        public C0BudgetCandidate? Budgets { get; init; }
    }

    private sealed record C0StageCandidate
    {
        public string? StageId { get; init; }

        public string? Kind { get; init; }

        public string? Objective { get; init; }

        public IReadOnlyList<string>? CapabilityToolIds { get; init; }

        public string? SideEffectLevel { get; init; }

        public C0BudgetCandidate? Budget { get; init; }
    }

    private sealed record C0EdgeCandidate
    {
        public string? FromStageId { get; init; }

        public string? ToStageId { get; init; }

        public string? TransitionKind { get; init; }
    }

    private sealed record C0PolicyCandidate
    {
        public IReadOnlyList<string>? AllowedCapabilityToolIds { get; init; }

        public string? MaxSideEffectLevel { get; init; }

        public bool RequiresHumanGate { get; init; }
    }

    private sealed record C0BudgetCandidate
    {
        public int TokenBudget { get; init; }

        public long TimeBudgetMs { get; init; }

        public int ToolCallBudget { get; init; }
    }
}
