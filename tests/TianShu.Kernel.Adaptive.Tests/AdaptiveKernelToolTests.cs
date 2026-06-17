using System.Reflection;
using System.Xml.Linq;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;
using TianShu.Kernel.Abstractions;
using TianShu.Kernel.Adaptive;
using TianShu.Kernel.Adaptive.Tools;

namespace TianShu.Kernel.Adaptive.Tests;

public sealed class AdaptiveKernelToolTests
{
    [Fact]
    public async Task AdaptiveOrchestrator_OnlyReturnsProposalSet()
    {
        var intent = CreateIntent();
        var orchestrator = new AdaptiveOrchestrator();
        var result = await orchestrator.ProposeAsync(intent, new KernelRunState(new KernelRunId("run-001"), intent.IntentId), new KernelRunOptions(requireHumanGate: false));

        Assert.IsType<KernelProposalSet>(result);
        Assert.All(result.Proposals, proposal => Assert.IsType<StageGraphProposal>(proposal));
    }

    [Fact]
    public async Task AdaptiveOrchestrator_ProposesMultipleStructuredStageGraphCandidates()
    {
        var intent = CreateIntent();
        var orchestrator = new AdaptiveOrchestrator();
        var result = await orchestrator.ProposeAsync(
            intent,
            new KernelRunState(new KernelRunId("run-001"), intent.IntentId),
            new KernelRunOptions(preferredGraphId: new StageGraphId("graph.acceptance"), requireHumanGate: false));

        var proposals = result.Proposals.Cast<StageGraphProposal>().ToArray();

        Assert.True(proposals.Length >= 3);
        Assert.Equal(proposals.Length, proposals.Select(static proposal => proposal.Graph.GraphId.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.All(proposals, proposal =>
        {
            Assert.NotEmpty(proposal.Graph.Stages);
            Assert.NotEmpty(proposal.Graph.Policies.AllowedKernelToolIds);
            Assert.NotEmpty(proposal.Graph.EvaluationRules.MetricIds);
            Assert.Equal(SideEffectLevel.ReadOnly, proposal.Graph.Policies.MaxSideEffectLevel);
            Assert.StartsWith("graph.acceptance.", proposal.Graph.GraphId.Value, StringComparison.Ordinal);
        });
        Assert.Contains(proposals, proposal => proposal.Graph.Metadata.Source == "candidate.direct");
        Assert.Contains(proposals, proposal => proposal.Graph.Metadata.Source == "candidate.context_guarded");
        Assert.Contains(proposals, proposal => proposal.Graph.Metadata.Source == "candidate.recovery_checked");
        Assert.Contains(proposals, proposal => proposal.Graph.Edges.Any(edge => edge.TransitionKind == StageTransitionKind.Recovery));
    }

    [Fact]
    public async Task DefaultCandidateGenerator_ReturnsOnlyStructuredStageGraphProposals()
    {
        var intent = CreateIntent();
        var generator = new DefaultAdaptiveStageGraphCandidateGenerator();

        var proposals = await generator.GenerateCandidatesAsync(
            intent,
            new KernelRunState(new KernelRunId("run-001"), intent.IntentId),
            new KernelRunOptions(requireHumanGate: false));

        Assert.True(proposals.Count >= 3);
        Assert.All(proposals, proposal =>
        {
            Assert.IsType<StageGraphProposal>(proposal);
            Assert.NotNull(proposal.Graph);
            Assert.NotEmpty(proposal.Graph.Stages);
        });
    }

    [Fact]
    public async Task AdaptiveOrchestrator_PreservesComposeToolInjectionConstructor()
    {
        var intent = CreateIntent();
        var orchestrator = new AdaptiveOrchestrator(new ComposeStageGraphKernelTool());

        var result = await orchestrator.ProposeAsync(
            intent,
            new KernelRunState(new KernelRunId("run-001"), intent.IntentId),
            new KernelRunOptions(requireHumanGate: false));

        Assert.True(result.Proposals.Count >= 3);
    }

    [Fact]
    public void DefaultCatalog_ContainsAllFormalKernelTools()
    {
        var toolNames = KernelToolCatalog.CreateDefaultTools().Select(static tool => tool.ToolName).OrderBy(static value => value, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            new[]
            {
                KernelToolNames.ComposeStageGraph,
                KernelToolNames.EvaluateRun,
                KernelToolNames.PromoteStrategy,
                KernelToolNames.ProposeCheckpoint,
                KernelToolNames.ProposeKernelPolicyChange,
                KernelToolNames.ProposeRecoveryPlan,
                KernelToolNames.ProposeStage,
                KernelToolNames.RequestCapabilityCall,
                KernelToolNames.ReviseStageGraph,
                KernelToolNames.RollbackStrategy,
                KernelToolNames.SelectModelRoute,
                KernelToolNames.SelectToolStrategy,
                KernelToolNames.UpdateContextPolicy,
            }.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            toolNames);
    }

    [Fact]
    public async Task KernelTools_ReturnOnlyProposalOrOperation()
    {
        var intent = CreateIntent();
        var state = new KernelRunState(new KernelRunId("run-001"), intent.IntentId);
        var invocation = new KernelToolInvocation(intent, state, new KernelRunOptions(requireHumanGate: false), StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["stageId"] = "stage-001",
            ["strategyId"] = "strategy-001",
            ["reason"] = "test",
        }));

        foreach (var tool in KernelToolCatalog.CreateDefaultTools())
        {
            var result = await tool.InvokeKernelAsync(invocation);
            Assert.True(result.Proposal is not null ^ result.Operation is not null, tool.ToolName);
        }
    }

    [Fact]
    public async Task PromoteStrategy_DoesNotAutoPromote()
    {
        var intent = CreateIntent();
        var result = await new PromoteStrategyKernelTool().InvokeKernelAsync(new KernelToolInvocation(
            intent,
            new KernelRunState(new KernelRunId("run-001"), intent.IntentId),
            new KernelRunOptions()));

        var proposal = Assert.IsType<StrategyPromotionProposal>(result.Proposal);
        Assert.NotEqual(StrategyLifecycleState.Promoted, proposal.TargetState);
        Assert.True(proposal.RiskProfile.RequiresHumanGate);
    }

    [Fact]
    public async Task ProposeKernelPolicyChange_AlwaysRequiresHumanGate()
    {
        var intent = CreateIntent();
        var result = await new ProposeKernelPolicyChangeKernelTool().InvokeKernelAsync(new KernelToolInvocation(
            intent,
            new KernelRunState(new KernelRunId("run-001"), intent.IntentId),
            new KernelRunOptions()));

        var proposal = Assert.IsType<PolicyChangeProposal>(result.Proposal);
        Assert.True(proposal.RiskProfile.RequiresHumanGate);
        Assert.True(proposal.ProposedPolicySet.RequiresHumanGate);
    }

    [Fact]
    public void AdaptiveAssembly_DoesNotExposeRuntimeStepReturnValues()
    {
        var adaptiveTypes = typeof(AdaptiveOrchestrator).Assembly.GetExportedTypes()
            .Where(static type => type.Namespace?.StartsWith("TianShu.Kernel.Adaptive", StringComparison.Ordinal) == true)
            .ToArray();

        foreach (var type in adaptiveTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                Assert.DoesNotContain(typeof(RuntimeStep), Flatten(method.ReturnType));
                foreach (var parameter in method.GetParameters())
                {
                    Assert.DoesNotContain(typeof(RuntimeStep), Flatten(parameter.ParameterType));
                }
            }
        }
    }

    [Fact]
    public void AdaptiveProject_DoesNotReferenceModulePlaneOrExecutionRuntime()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var projectPath = Path.Combine(repositoryRoot, "src", "Core", "TianShu.Kernel.Adaptive", "TianShu.Kernel.Adaptive.csproj");
        var projectDirectory = Path.GetDirectoryName(projectPath)!;

        var references = XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(Path.Combine(projectDirectory, value!)))
            .ToArray();

        Assert.All(references, reference =>
        {
            Assert.DoesNotContain($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}Tools{Path.DirectorySeparatorChar}", reference, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}Provider{Path.DirectorySeparatorChar}", reference, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}Execution{Path.DirectorySeparatorChar}TianShu.Execution.Runtime", reference, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}Hosting{Path.DirectorySeparatorChar}", reference, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void KernelToolDescriptors_AreKernelToolsWithNoSideEffects()
    {
        foreach (var tool in KernelToolCatalog.CreateDefaultTools().OfType<ITianShuTool>())
        {
            Assert.Equal(ToolKind.Kernel, tool.Descriptor.Kind);
            Assert.Equal(SideEffectLevel.None, tool.Descriptor.SideEffects.Level);
            Assert.True(tool.Descriptor.Audit.Required);
        }
    }

    private static TurnIntent CreateIntent()
        => new(
            new CoreIntentId("intent-001"),
            new KernelSubjectRef(new SessionId("session-001"), new ThreadId("thread-001")),
            new GovernanceEnvelope("governance-001", maxSideEffectLevel: SideEffectLevel.ReadOnly, requiresHumanGate: false),
            "input-001",
            new KernelBudget(tokenBudget: 1_024, timeBudgetMs: 10_000, toolCallBudget: 1));

    private static IReadOnlyList<Type> Flatten(Type type)
    {
        if (type.IsGenericType)
        {
            return type.GetGenericArguments().SelectMany(Flatten).Append(type.GetGenericTypeDefinition()).ToArray();
        }

        return type.HasElementType ? Flatten(type.GetElementType()!) : new[] { type };
    }
}
