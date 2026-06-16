using TianShu.Contracts.Environment;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime;

public interface IExecutionRuntimeWorkspaceModuleBridge
{
    Task<RuntimeStepResult> ResolveAsync(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        IWorkspaceModule module,
        WorkspaceResolutionRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Execution Runtime 到 Workspace / Environment Module 的只读桥接入口。
/// Read-only bridge from Execution Runtime to the Workspace / Environment Module.
/// </summary>
public sealed class ExecutionRuntimeWorkspaceModuleBridge : IExecutionRuntimeWorkspaceModuleBridge
{
    public async Task<RuntimeStepResult> ResolveAsync(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        IWorkspaceModule module,
        WorkspaceResolutionRequest request,
        CancellationToken cancellationToken)
    {
        var failure = Validate(step, context, module);
        if (failure is not null)
        {
            return CreateBlockedResult(step, context, failure);
        }

        var result = await module.ResolveAsync(
                request,
                new WorkspaceModuleInvocationContext(
                    step.StepId,
                    step.SourceIntentId.Value,
                    step.SourceGraphId.Value,
                    step.SourceStageId.Value,
                    step.SourceKernelOperationId.Value,
                    step.Permission,
                    step.SideEffect,
                    step.Metadata),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Status == WorkspaceResolutionStatus.Rejected)
        {
            return CreateBlockedResult(
                step,
                context,
                new ExecutionFailure(
                    result.Issues.FirstOrDefault() ?? "workspace_resolution_rejected",
                    "Workspace / Environment Module rejected the resolution request."));
        }

        return new RuntimeStepResult(
            step.StepId,
            RuntimeStepKind.ModuleCapability,
            RuntimeStepResultStatus.Succeeded,
            StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["runtimeBoundary"] = "execution.runtime.workspace_module_bridge",
                ["moduleId"] = module.Descriptor.ModuleId,
                ["capabilityId"] = step.CapabilityId,
                ["status"] = result.Status.ToString(),
                ["factCount"] = result.Facts.Count,
                ["sourceCount"] = result.Sources.Count,
                ["diagnosticsRefs"] = result.DiagnosticsRefs,
                ["issues"] = result.Issues,
            }),
            diagnosticsRef: result.DiagnosticsRefs.FirstOrDefault()
                            ?? $"diagnostics://execution/{context.ExecutionId.Value}/{step.StepId}/workspace",
            traceRef: $"trace://execution/{context.ExecutionId.Value}/{step.StepId}/workspace");
    }

    private static ExecutionFailure? Validate(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        IWorkspaceModule module)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(module);

        var governanceFailure = TianShuExecutionRuntime.ValidateStep(step, context);
        if (governanceFailure is not null)
        {
            return governanceFailure;
        }

        if (module.Descriptor.Kind != ModuleKind.WorkspaceEnvironment)
        {
            return new ExecutionFailure("workspace_module_kind_mismatch", "ModuleCapabilityStep 指向的模块不是 Workspace / Environment Module。");
        }

        if (!string.Equals(step.ModuleId, module.Descriptor.ModuleId, StringComparison.Ordinal))
        {
            return new ExecutionFailure("workspace_module_descriptor_mismatch", "ModuleCapabilityStep 的 module 标识与 ModuleDescriptor 不一致。");
        }

        if (!module.Descriptor.IsAllowedBy(context.Governance))
        {
            return new ExecutionFailure("workspace_module_governance_denied", "Workspace / Environment Module descriptor 超出 GovernanceEnvelope。");
        }

        if (step.SideEffect.Level is SideEffectLevel.Unspecified or > SideEffectLevel.ReadOnly)
        {
            return new ExecutionFailure("workspace_module_step_not_read_only", "Workspace / Environment Module 只能通过只读 RuntimeStep 调用。");
        }

        return null;
    }

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
