using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime;

public interface IExecutionRuntimeArtifactModuleBridge
{
    Task<RuntimeStepResult> ExecuteArtifactAsync(
        ArtifactStep step,
        ExecutionRuntimeContext context,
        IArtifactStateProjectionModule module,
        ArtifactModuleMutation mutation,
        CancellationToken cancellationToken);

    Task<RuntimeStepResult> ExecuteProjectionQueryAsync(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        IArtifactStateProjectionModule module,
        ArtifactProjectionModuleQuery query,
        CancellationToken cancellationToken);
}

public sealed class ExecutionRuntimeArtifactModuleBridge : IExecutionRuntimeArtifactModuleBridge
{
    public async Task<RuntimeStepResult> ExecuteArtifactAsync(
        ArtifactStep step,
        ExecutionRuntimeContext context,
        IArtifactStateProjectionModule module,
        ArtifactModuleMutation mutation,
        CancellationToken cancellationToken)
    {
        var failure = ValidateModule(step, context, module)
                      ?? ValidateArtifactOperation(step, mutation);
        if (failure is not null)
        {
            return CreateBlockedResult(step, context, failure);
        }

        var result = await module.ExecuteArtifactAsync(
                new ArtifactModuleMutationInvocation(
                    step,
                    mutation,
                    CreateInvocationContext(step, context)),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Success
            ? CreateSucceededResult(
                step,
                context,
                new Dictionary<string, object?>
                {
                    ["runtimeBoundary"] = "execution.runtime.artifact_module_bridge",
                    ["moduleId"] = module.Descriptor.ModuleId,
                    ["operationName"] = result.OperationName,
                    ["artifactId"] = result.Record?.Artifact.Id.Value,
                    ["artifactState"] = result.Record?.Artifact.State.ToString(),
                    ["version"] = result.Record?.Version,
                })
            : CreateBlockedResult(
                step,
                context,
                new ExecutionFailure("artifact_module_mutation_failed", result.DegradedReason ?? "Artifact Module mutation failed."));
    }

    public async Task<RuntimeStepResult> ExecuteProjectionQueryAsync(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        IArtifactStateProjectionModule module,
        ArtifactProjectionModuleQuery query,
        CancellationToken cancellationToken)
    {
        var failure = ValidateModule(step, context, module);
        if (failure is not null)
        {
            return CreateBlockedResult(step, context, failure);
        }

        var result = await module.QueryProjectionAsync(
                new ArtifactProjectionModuleQueryInvocation(
                    query,
                    CreateInvocationContext(step, context)),
                cancellationToken)
            .ConfigureAwait(false);

        return CreateSucceededResult(
            step,
            context,
            new Dictionary<string, object?>
            {
                ["runtimeBoundary"] = "execution.runtime.artifact_projection_bridge",
                ["moduleId"] = module.Descriptor.ModuleId,
                ["hasSnapshot"] = result.Snapshot is not null,
                ["scopeKind"] = result.Snapshot?.ScopeKind.ToString(),
                ["scopeKey"] = result.Snapshot?.ScopeKey,
                ["degradedSources"] = result.DegradedSources,
            });
    }

    private static ExecutionFailure? ValidateModule(
        RuntimeStep step,
        ExecutionRuntimeContext context,
        IArtifactStateProjectionModule module)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(module);

        var governanceFailure = TianShuExecutionRuntime.ValidateStep(step, context);
        if (governanceFailure is not null)
        {
            return governanceFailure;
        }

        if (module.Descriptor.Kind != ModuleKind.ArtifactStateProjection)
        {
            return new ExecutionFailure("artifact_module_kind_mismatch", "RuntimeStep 指向的模块不是 Artifact / State / Projection Module。");
        }

        if (!module.Descriptor.IsAllowedBy(context.Governance))
        {
            return new ExecutionFailure("artifact_module_governance_denied", "Artifact / State / Projection Module descriptor 超出 GovernanceEnvelope。");
        }

        return null;
    }

    private static ExecutionFailure? ValidateArtifactOperation(ArtifactStep step, ArtifactModuleMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        return string.Equals(step.ArtifactOperation, mutation.OperationName, StringComparison.OrdinalIgnoreCase)
            ? null
            : new ExecutionFailure("artifact_operation_mismatch", "ArtifactStep 的 artifactOperation 与模块 mutation 不一致。");
    }

    private static ArtifactModuleInvocationContext CreateInvocationContext(
        RuntimeStep step,
        ExecutionRuntimeContext context)
        => new(
            step.StepId,
            step.SourceIntentId,
            step.SourceGraphId,
            step.SourceStageId,
            step.SourceKernelOperationId,
            context.KernelRunId,
            context.ExecutionId,
            step.Permission,
            step.SideEffect,
            step.Metadata);

    private static RuntimeStepResult CreateSucceededResult(
        RuntimeStep step,
        ExecutionRuntimeContext context,
        IReadOnlyDictionary<string, object?> output)
        => new(
            step.StepId,
            step.StepKind,
            RuntimeStepResultStatus.Succeeded,
            StructuredValue.FromPlainObject(output),
            diagnosticsRef: $"diagnostics://execution/{context.ExecutionId.Value}/{step.StepId}/artifact",
            traceRef: $"trace://execution/{context.ExecutionId.Value}/{step.StepId}/artifact");

    private static RuntimeStepResult CreateBlockedResult(
        RuntimeStep step,
        ExecutionRuntimeContext context,
        ExecutionFailure failure)
        => new(
            step.StepId,
            step.StepKind,
            RuntimeStepResultStatus.Blocked,
            failure: failure,
            diagnosticsRef: $"diagnostics://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}",
            traceRef: $"trace://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}");
}
