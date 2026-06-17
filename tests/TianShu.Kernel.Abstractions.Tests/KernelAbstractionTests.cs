using System.Reflection;
using System.Xml.Linq;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Kernel.Abstractions;

namespace TianShu.Kernel.Abstractions.Tests;

public sealed class KernelAbstractionTests
{
    [Fact]
    public void AbstractionsExposeKernelBoundaryInterfaces()
    {
        var boundaryTypes = new[]
        {
            typeof(IStableKernelCore),
            typeof(IAdaptiveOrchestrator),
            typeof(IAdaptiveStageGraphCandidateGenerator),
            typeof(IAdaptiveCandidateValidationService),
            typeof(IAdaptiveCandidateTrialService),
            typeof(IKernelValidator),
            typeof(IStageGraphInterpreter),
            typeof(IKernelTraceStore),
            typeof(IKernelEvaluator),
            typeof(IKernelCrossReviewExperimentService),
            typeof(IKernelObjectiveAnchorCalibrationService),
            typeof(IKernelStrategyEvaluationAggregationService),
            typeof(IStrategyRegistry),
        };

        Assert.All(boundaryTypes, type => Assert.True(type.IsInterface));
    }

    [Fact]
    public void AbstractionsReuseKernelAndExecutionContracts()
    {
        var stableCoreMethods = typeof(IStableKernelCore).GetMethods();
        var runMethod = Assert.Single(stableCoreMethods, method => method.Name == nameof(IStableKernelCore.RunAsync));
        var reviewPlanMethod = Assert.Single(stableCoreMethods, method => method.Name == nameof(IStableKernelCore.ReviewExecutionPlanAsync));
        var interpreterMethod = Assert.Single(typeof(IStageGraphInterpreter).GetMethods(), method => method.Name == nameof(IStageGraphInterpreter.InterpretAsync));
        var validatorStepMethod = Assert.Single(typeof(IKernelValidator).GetMethods(), method => method.Name == nameof(IKernelValidator.ValidateRuntimeStepAsync));
        var saveCandidateMethod = Assert.Single(typeof(IStrategyRegistry).GetMethods(), method => method.Name == nameof(IStrategyRegistry.SaveCandidateAsync));
        var auditRecordsMethod = Assert.Single(typeof(IStrategyRegistry).GetMethods(), method => method.Name == nameof(IStrategyRegistry.ListAuditRecordsAsync));

        Assert.Equal(typeof(Task<KernelRunResult>), runMethod.ReturnType);
        Assert.Contains(reviewPlanMethod.GetParameters(), parameter => parameter.ParameterType == typeof(ExecutionPlan));
        Assert.Equal(typeof(Task<ExecutionPlan>), interpreterMethod.ReturnType);
        Assert.Contains(validatorStepMethod.GetParameters(), parameter => parameter.ParameterType == typeof(RuntimeStep));
        Assert.Equal(typeof(Task<StrategyRecord>), saveCandidateMethod.ReturnType);
        Assert.Contains(saveCandidateMethod.GetParameters(), parameter => parameter.ParameterType == typeof(IReadOnlyList<StrategyTransitionEvidence>));
        Assert.Equal(typeof(Task<IReadOnlyList<StrategyLifecycleAuditRecord>>), auditRecordsMethod.ReturnType);
    }

    [Fact]
    public void ValidationDefaultsDoNotApprove()
    {
        var result = new KernelValidationResult(KernelValidationDecision.Unspecified);
        var issue = new KernelValidationIssue("kernel.validation.missing_graph", "缺少 StageGraph。");

        Assert.False(result.IsApproved);
        Assert.Equal(KernelValidationIssueSeverity.Error, issue.Severity);
    }

    [Fact]
    public void AbstractionsAssembly_DoesNotReferenceImplementationAssemblies()
    {
        var referencedTianShuAssemblies = typeof(IStableKernelCore)
            .Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .Where(static name => name is not null && name.StartsWith("TianShu.", StringComparison.Ordinal))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "TianShu.Contracts.Execution",
                "TianShu.Contracts.Kernel",
                "TianShu.Contracts.Primitives",
            },
            referencedTianShuAssemblies);
    }

    [Fact]
    public void AbstractionsProject_ReferencesOnlyContractsProjects()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var projectPath = Path.Combine(repositoryRoot, "src", "Core", "TianShu.Kernel.Abstractions", "TianShu.Kernel.Abstractions.csproj");
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var contractsRoot = Path.Combine(repositoryRoot, "src", "Contracts");

        var references = XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(Path.Combine(projectDirectory, value!)))
            .ToArray();

        Assert.NotEmpty(references);
        Assert.All(references, reference => Assert.StartsWith(contractsRoot, reference, StringComparison.OrdinalIgnoreCase));
    }
}
