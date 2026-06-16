using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.Kernel.Tests;

public sealed class StageRegistryPlanningContextFactoryTests
{
    [Fact]
    public void CreateContext_BindsRegistryToEntryPlanner()
    {
        var context = StageRegistryPlanningContextFactory.CreateContext(new Dictionary<string, object?>());

        Assert.Contains(context.Stages, static stage => stage.Id == BuiltInStageDefinitions.Coding);
        Assert.IsType<SessionCoreLoopEntryPlanner>(context.EntryPlanner);
        Assert.Empty(context.Issues);
    }

    [Fact]
    public void CreateContext_PreservesWarningIssues()
    {
        var context = StageRegistryPlanningContextFactory.CreateContext(new Dictionary<string, object?>
        {
            ["stage_registry"] = new Dictionary<string, object?>
            {
                ["stages"] = new List<object?>
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = "disabled_stage",
                        ["enabled"] = false,
                        ["lifecycle_order"] = 5,
                    },
                },
            },
        });

        Assert.DoesNotContain(context.Stages, static stage => stage.Id == "disabled_stage");
        var issue = Assert.Single(context.Issues);
        Assert.Equal("extension_stage_disabled", issue.Code);
        Assert.Equal(RuntimeStageRegistryIssueSeverity.Warning, issue.Severity);
    }

    [Fact]
    public void CreateContext_WhenRegistryInvalid_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StageRegistryPlanningContextFactory.CreateContext(new Dictionary<string, object?>
            {
                ["stage_registry"] = new Dictionary<string, object?>
                {
                    ["stages"] = new List<object?>
                    {
                        new Dictionary<string, object?>
                        {
                            ["id"] = "missing_order",
                        },
                    },
                },
            }));

        Assert.Contains("extension_stage_lifecycle_order_missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RoutingPlanFactory_PlansEntryAndInjectsObservedState()
    {
        SessionCoreLoopRouteRequest? observedRouteRequest = null;
        var input = SessionOrchestrationInputFactory.Create(
            "thread-001",
            "correlation-001",
            requestedStageId: BuiltInStageDefinitions.Coding);

        var plan = SessionCoreLoopRoutingPlanFactory.Plan(
            new SessionCoreLoopRoutingPlanRequest(
                new Dictionary<string, object?>(),
                input,
                routeRequest =>
                {
                    observedRouteRequest = routeRequest;
                    return new SessionCoreLoopRouteResult(
                        new ModelRouteResolutionResult(
                            "workbench",
                            routeRequest.RouteKind,
                            "provider-001",
                            "model-001",
                            0,
                            "openai_chat_completions"),
                        "route-diagnostics-001");
                },
                workspaceCwd: "D:\\workspace",
                workspaceSandboxMode: "workspace-write",
                workspaceWebSearchMode: "enabled",
                workspaceWindowsSandboxLevel: "Disabled",
                allowLoginShell: true,
                artifactRefs:
                [
                    new ArtifactRef(new ArtifactId("artifact-001"), "result.txt", "text"),
                ],
                memoryMode: "project",
                approvalPolicy: "on-request",
                policySandboxMode: "workspace-write",
                policyWebSearchMode: "enabled",
                defaultModeRequestUserInputEnabled: true,
                startedAt: DateTimeOffset.Parse("2026-06-09T00:00:00Z")));

        Assert.NotNull(observedRouteRequest);
        Assert.Equal(BuiltInStageDefinitions.Coding, plan.OrchestrationStep.Stage.Id);
        Assert.Contains(plan.Stages, static item => item.Id == BuiltInStageDefinitions.Coding);
        Assert.Equal("route-diagnostics-001", plan.ExecutorRuntimeContext.ModelRouteDiagnosticsCorrelationId);
        Assert.Contains(plan.OrchestrationStep.ContextPackage.Segments, static segment => segment.Kind == "workspace_state");
        Assert.Contains(plan.OrchestrationStep.ContextPackage.Segments, static segment => segment.Kind == "artifact_state");
        Assert.Contains(plan.OrchestrationStep.ContextPackage.Segments, static segment => segment.Kind == "memory_state");
        Assert.Contains(plan.OrchestrationStep.ContextPackage.Segments, static segment => segment.Kind == "policy_state");
    }
}
