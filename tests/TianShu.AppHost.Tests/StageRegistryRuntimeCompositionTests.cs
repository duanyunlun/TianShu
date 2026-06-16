using TianShu.Contracts.Orchestration;
using TianShu.Kernel;
using TianShu.RuntimeComposition;
using RuntimeStageRegistryIssueSeverity = TianShu.Kernel.RuntimeStageRegistryIssueSeverity;

namespace TianShu.AppHost.Tests;

public sealed class StageRegistryRuntimeCompositionTests
{
    [Fact]
    public void CreateDefaultRegistry_ExposesBuiltinStageLifecycle()
    {
        var registry = StageRegistryRuntimeComposition.CreateDefaultRegistry();

        Assert.True(registry.IsValid);
        Assert.Equal(
            [
                BuiltInStageDefinitions.Default,
                BuiltInStageDefinitions.Planning,
                BuiltInStageDefinitions.Fast,
                BuiltInStageDefinitions.LongContext,
                BuiltInStageDefinitions.Coding,
                BuiltInStageDefinitions.Review,
                BuiltInStageDefinitions.Summarization,
                BuiltInStageDefinitions.MemoryExtraction,
            ],
            registry.Stages.Select(static stage => stage.Id).ToArray());
        Assert.Equal(BuiltInStageDefinitions.Coding, registry.FindStage("CODING")?.Id);
        Assert.Contains(registry.Transitions, static transition =>
            transition.FromStageId == BuiltInStageDefinitions.Coding
            && transition.ToStageId == BuiltInStageDefinitions.Review);
    }

    [Fact]
    public void CreateRegistry_ReportsDuplicateStageIds()
    {
        var registry = StageRegistryRuntimeComposition.CreateRegistry(
        [
            new StageDefinition("coding", "Coding", 1),
            new StageDefinition("CODING", "Duplicate Coding", 2),
        ]);

        var issue = Assert.Single(registry.Issues);
        Assert.Equal("duplicate_stage_id", issue.Code);
        Assert.Equal("CODING", issue.StageId);
        Assert.Single(registry.Stages);
    }

    [Fact]
    public void CreateRegistry_ReportsMissingTransitionTargets()
    {
        var registry = StageRegistryRuntimeComposition.CreateRegistry(
        [
            new StageDefinition("coding", "Coding", 1, allowedNext: ["review"], allowedPrevious: ["planning"]),
        ]);

        Assert.Equal(["transition_target_missing", "transition_source_missing"], registry.Issues.Select(static issue => issue.Code).ToArray());
        Assert.Empty(registry.Transitions);
    }

    [Fact]
    public void CreateRegistryFromConfig_LoadsTrustedExtensionStages()
    {
        var registry = StageRegistryRuntimeComposition.CreateRegistryFromConfig(new Dictionary<string, object?>
        {
            ["stage_registry"] = new Dictionary<string, object?>
            {
                ["stages"] = new Dictionary<string, object?>
                {
                    ["triage"] = new Dictionary<string, object?>
                    {
                        ["display_name"] = "Triage",
                        ["lifecycle_order"] = 5,
                        ["model_route_kind"] = "triage",
                        ["allowed_previous"] = new List<object?> { "default" },
                        ["allowed_next"] = new List<object?> { "planning" },
                        ["context_projection_mode"] = "summary",
                        ["executor_binding"] = "triage.executor",
                    },
                },
            },
        });

        Assert.True(registry.IsValid);
        var stage = Assert.Single(registry.Stages, static item => item.Id == "triage");
        Assert.Equal("Triage", stage.DisplayName);
        Assert.Equal("triage", stage.ModelRouteKind.Value);
        Assert.Equal(StageContextProjectionMode.Summary, stage.ContextProjectionMode);
        Assert.Equal("triage.executor", stage.ExecutorBinding);
        Assert.Contains(registry.Transitions, static transition =>
            transition.FromStageId == "triage"
            && transition.ToStageId == BuiltInStageDefinitions.Planning);
        Assert.Contains(registry.Transitions, static transition =>
            transition.FromStageId == BuiltInStageDefinitions.Default
            && transition.ToStageId == "triage");
    }

    [Fact]
    public void BuildRouteDiagnostic_UsesConfiguredStageRouteKinds()
    {
        var diagnostic = ModelRouteRuntimeComposition.BuildRouteDiagnostic(
            new Dictionary<string, object?>
            {
                ["model_route_set"] = "default",
                ["model_route_sets"] = new Dictionary<string, object?>
                {
                    ["default"] = new Dictionary<string, object?>
                    {
                        ["routes"] = new List<object?>
                        {
                            new Dictionary<string, object?>
                            {
                                ["kind"] = "triage",
                                ["candidates"] = new List<object?>
                                {
                                    new Dictionary<string, object?>
                                    {
                                        ["provider"] = "openai",
                                        ["model"] = "gpt-5",
                                    },
                                },
                            },
                        },
                    },
                },
                ["stage_registry"] = new Dictionary<string, object?>
                {
                    ["stages"] = new Dictionary<string, object?>
                    {
                        ["triage"] = new Dictionary<string, object?>
                        {
                            ["display_name"] = "Triage",
                            ["lifecycle_order"] = 5,
                            ["model_route_kind"] = "triage",
                        },
                    },
                },
            },
            "triage");

        Assert.Equal("triage", diagnostic.ResolvedRouteKind);
        Assert.Null(diagnostic.RouteFallbackReason);
        Assert.Contains("triage", diagnostic.RegisteredRouteKinds);
        Assert.DoesNotContain("triage", diagnostic.UnknownRouteKinds);
        Assert.Equal("gpt-5", diagnostic.PreferredCandidate?.Model);
    }

    [Fact]
    public void KernelStageRegistryRuntimeComposition_DoesNotReadCompatibilityConfigDirectly()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Core",
            "TianShu.Kernel",
            "StageRegistryRuntimeComposition.cs"));

        Assert.DoesNotContain("KernelConfigCompatibilityReaders", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRegistryFromConfig_ReportsDisabledAndInvalidExtensionStages()
    {
        var registry = StageRegistryRuntimeComposition.CreateRegistryFromConfig(new Dictionary<string, object?>
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
                    new Dictionary<string, object?>
                    {
                        ["id"] = "missing_order",
                    },
                },
            },
        });

        Assert.False(registry.IsValid);
        Assert.DoesNotContain(registry.Stages, static stage => stage.Id == "disabled_stage");
        Assert.DoesNotContain(registry.Stages, static stage => stage.Id == "missing_order");
        Assert.Contains(registry.Issues, static issue =>
            issue.Code == "extension_stage_disabled"
            && issue.Severity == RuntimeStageRegistryIssueSeverity.Warning);
        Assert.Contains(registry.Issues, static issue =>
            issue.Code == "extension_stage_lifecycle_order_missing"
            && issue.Severity == RuntimeStageRegistryIssueSeverity.Error);
    }

    [Fact]
    public void StageRegistryPlanningRuntimeComposition_ShouldNotExist_AndKernelFactoryPreservesWarningIssues()
    {
        Assert.False(
            File.Exists(Path.Combine(
                FindRepositoryRoot(),
                "src",
                "Core",
                "TianShu.RuntimeComposition",
                "StageRegistryPlanningRuntimeComposition.cs")),
            "RuntimeComposition must not reintroduce a dedicated Stage Registry planning pure-forwarding bridge.");

        var context = TianShu.Kernel.StageRegistryPlanningContextFactory.CreateContext(new Dictionary<string, object?>
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
    public void CreateRegistryFromConfig_IgnoresCamelCaseStageRegistryTable()
    {
        var registry = StageRegistryRuntimeComposition.CreateRegistryFromConfig(new Dictionary<string, object?>
        {
            ["stageRegistry"] = new Dictionary<string, object?>
            {
                ["stages"] = new Dictionary<string, object?>
                {
                    ["triage"] = new Dictionary<string, object?>
                    {
                        ["display_name"] = "Triage",
                        ["lifecycle_order"] = 5,
                        ["model_route_kind"] = "triage",
                    },
                },
            },
        });

        Assert.True(registry.IsValid);
        Assert.DoesNotContain(registry.Stages, static stage => stage.Id == "triage");
        Assert.Empty(registry.Issues);
    }

    [Fact]
    public void CreateRegistryFromConfig_RejectsCamelCaseStageFields()
    {
        var registry = StageRegistryRuntimeComposition.CreateRegistryFromConfig(new Dictionary<string, object?>
        {
            ["stage_registry"] = new Dictionary<string, object?>
            {
                ["stages"] = new Dictionary<string, object?>
                {
                    ["triage"] = new Dictionary<string, object?>
                    {
                        ["displayName"] = "Triage",
                        ["lifecycleOrder"] = 5,
                        ["modelRouteKind"] = "triage",
                        ["allowedPrevious"] = new List<object?> { "default" },
                    },
                },
            },
        });

        Assert.False(registry.IsValid);
        Assert.DoesNotContain(registry.Stages, static stage => stage.Id == "triage");
        var issue = Assert.Single(registry.Issues);
        Assert.Equal("extension_stage_lifecycle_order_missing", issue.Code);
        Assert.Equal("triage", issue.StageId);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TianShu.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法从测试运行目录定位 TianShu 仓库根目录。");
    }
}
