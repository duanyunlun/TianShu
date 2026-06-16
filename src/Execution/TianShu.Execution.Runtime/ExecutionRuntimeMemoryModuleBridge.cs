using TianShu.Contracts.Execution;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime;

public interface IExecutionRuntimeMemoryModuleBridge
{
    Task<RuntimeStepResult> ExecuteQueryAsync(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        IMemoryModule module,
        MemoryModuleQuery query,
        MemoryOperationContext operationContext,
        CancellationToken cancellationToken);

    Task<RuntimeStepResult> ExecuteMutationAsync(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        IMemoryModule module,
        MemoryModuleMutation mutation,
        MemoryOperationContext operationContext,
        CancellationToken cancellationToken);
}

public sealed class ExecutionRuntimeMemoryModuleBridge : IExecutionRuntimeMemoryModuleBridge
{
    public async Task<RuntimeStepResult> ExecuteQueryAsync(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        IMemoryModule module,
        MemoryModuleQuery query,
        MemoryOperationContext operationContext,
        CancellationToken cancellationToken)
    {
        var failure = Validate(step, context, module);
        if (failure is not null)
        {
            return CreateBlockedResult(step, context, failure);
        }

        var result = await module.QueryAsync(
                new MemoryModuleQueryInvocation(query, CreateInvocationContext(step, operationContext)),
                cancellationToken)
            .ConfigureAwait(false);

        return CreateSucceededResult(
            step,
            context,
            new Dictionary<string, object?>
            {
                ["runtimeBoundary"] = "execution.runtime.memory_module_bridge",
                ["moduleId"] = module.Descriptor.ModuleId,
                ["capabilityId"] = step.CapabilityId,
                ["providerCount"] = result.Providers.Count,
                ["spaceCount"] = result.Spaces.Count,
                ["recordCount"] = result.Records?.TotalCount,
                ["reviewCount"] = result.Reviews?.TotalCount,
                ["degradedProviders"] = result.DegradedProviders,
            });
    }

    public async Task<RuntimeStepResult> ExecuteMutationAsync(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        IMemoryModule module,
        MemoryModuleMutation mutation,
        MemoryOperationContext operationContext,
        CancellationToken cancellationToken)
    {
        var failure = Validate(step, context, module);
        if (failure is not null)
        {
            return CreateBlockedResult(step, context, failure);
        }

        var result = await module.MutateAsync(
                new MemoryModuleMutationInvocation(mutation, CreateInvocationContext(step, operationContext)),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Success
            ? CreateSucceededResult(
                step,
                context,
                new Dictionary<string, object?>
                {
                    ["runtimeBoundary"] = "execution.runtime.memory_module_bridge",
                    ["moduleId"] = module.Descriptor.ModuleId,
                    ["capabilityId"] = step.CapabilityId,
                    ["effect"] = result.Effect.ToString(),
                    ["recordId"] = result.RecordId?.Value,
                    ["lifecycleStatus"] = result.LifecycleStatus?.ToString(),
                })
            : CreateBlockedResult(
                step,
                context,
                new ExecutionFailure("memory_module_mutation_failed", result.DegradedReason ?? "Memory Module mutation failed."));
    }

    private static ExecutionFailure? Validate(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        IMemoryModule module)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(module);

        var governanceFailure = TianShuExecutionRuntime.ValidateStep(step, context);
        if (governanceFailure is not null)
        {
            return governanceFailure;
        }

        if (module.Descriptor.Kind != ModuleKind.MemoryIdentity)
        {
            return new ExecutionFailure("memory_module_kind_mismatch", "ModuleCapabilityStep 指向的模块不是 Memory / Identity Module。");
        }

        if (!string.Equals(step.ModuleId, module.Descriptor.ModuleId, StringComparison.Ordinal))
        {
            return new ExecutionFailure("memory_module_descriptor_mismatch", "ModuleCapabilityStep 的 module 标识与 ModuleDescriptor 不一致。");
        }

        if (!module.Descriptor.IsAllowedBy(context.Governance))
        {
            return new ExecutionFailure("memory_module_governance_denied", "Memory / Identity Module descriptor 超出 GovernanceEnvelope。");
        }

        return null;
    }

    private static MemoryModuleInvocationContext CreateInvocationContext(
        ModuleCapabilityStep step,
        MemoryOperationContext operationContext)
        => new(
            step.StepId,
            step.SourceIntentId.Value,
            step.SourceGraphId.Value,
            step.SourceStageId.Value,
            step.SourceKernelOperationId.Value,
            step.Permission,
            step.SideEffect,
            operationContext,
            step.Metadata);

    private static RuntimeStepResult CreateSucceededResult(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        IReadOnlyDictionary<string, object?> output)
        => new(
            step.StepId,
            RuntimeStepKind.ModuleCapability,
            RuntimeStepResultStatus.Succeeded,
            StructuredValue.FromPlainObject(output),
            diagnosticsRef: $"diagnostics://execution/{context.ExecutionId.Value}/{step.StepId}/memory",
            traceRef: $"trace://execution/{context.ExecutionId.Value}/{step.StepId}/memory");

    private static RuntimeStepResult CreateBlockedResult(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        ExecutionFailure failure)
        => new(
            step.StepId,
            RuntimeStepKind.ModuleCapability,
            RuntimeStepResultStatus.Blocked,
            failure: failure,
            diagnosticsRef: $"diagnostics://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}",
            traceRef: $"trace://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}");
}
