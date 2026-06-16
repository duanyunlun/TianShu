using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime;

/// <summary>
/// Execution Runtime 到 Diagnostics Module 的受控桥接入口。
/// Controlled bridge from Execution Runtime to the Diagnostics Module.
/// </summary>
public interface IExecutionRuntimeDiagnosticsModuleBridge
{
    Task<RuntimeStepResult> ExecuteAsync(
        DiagnosticStep step,
        ExecutionRuntimeContext context,
        IDiagnosticsModule module,
        DiagnosticsModuleEvent diagnosticEvent,
        CancellationToken cancellationToken);
}

/// <summary>
/// 通过 Kernel 已批准的 DiagnosticStep 调用 Diagnostics Module。
/// Invokes the Diagnostics Module through a Kernel-approved DiagnosticStep.
/// </summary>
public sealed class ExecutionRuntimeDiagnosticsModuleBridge : IExecutionRuntimeDiagnosticsModuleBridge
{
    public async Task<RuntimeStepResult> ExecuteAsync(
        DiagnosticStep step,
        ExecutionRuntimeContext context,
        IDiagnosticsModule module,
        DiagnosticsModuleEvent diagnosticEvent,
        CancellationToken cancellationToken)
    {
        var failure = Validate(step, context, module, diagnosticEvent);
        if (failure is not null)
        {
            return CreateBlockedResult(step, context, failure);
        }

        var result = await module.EmitAsync(diagnosticEvent, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return CreateBlockedResult(
                step,
                context,
                new ExecutionFailure("diagnostics_module_emit_failed", result.DegradedReason ?? "Diagnostics Module emit failed."));
        }

        return new RuntimeStepResult(
            step.StepId,
            RuntimeStepKind.Diagnostic,
            RuntimeStepResultStatus.Succeeded,
            StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["runtimeBoundary"] = "execution.runtime.diagnostics_module_bridge",
                ["moduleId"] = module.Descriptor.ModuleId,
                ["eventName"] = result.EventName,
                ["eventKind"] = result.Kind.ToString(),
            }),
            diagnosticsRef: result.DiagnosticsRef,
            traceRef: result.TraceRef);
    }

    private static ExecutionFailure? Validate(
        DiagnosticStep step,
        ExecutionRuntimeContext context,
        IDiagnosticsModule module,
        DiagnosticsModuleEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        var governanceFailure = TianShuExecutionRuntime.ValidateStep(step, context);
        if (governanceFailure is not null)
        {
            return governanceFailure;
        }

        if (module.Descriptor.Kind != ModuleKind.Diagnostics)
        {
            return new ExecutionFailure("diagnostics_module_kind_mismatch", "RuntimeStep 指向的模块不是 Diagnostics Module。");
        }

        if (!module.Descriptor.IsAllowedBy(context.Governance))
        {
            return new ExecutionFailure("diagnostics_module_governance_denied", "Diagnostics Module descriptor 超出 GovernanceEnvelope。");
        }

        if (!IsDiagnosticKindCompatible(step.DiagnosticKind, diagnosticEvent.Kind))
        {
            return new ExecutionFailure("diagnostics_event_kind_mismatch", "DiagnosticStep 的 diagnosticKind 与 DiagnosticsModuleEvent kind 不一致。");
        }

        if ((diagnosticEvent.Kind is DiagnosticsModuleEventKind.ExecutionRuntimeStep or DiagnosticsModuleEventKind.ModuleCall)
            && !string.Equals(diagnosticEvent.Context.RuntimeStepId, step.StepId, StringComparison.Ordinal))
        {
            return new ExecutionFailure("diagnostics_runtime_step_mismatch", "DiagnosticsModuleEvent 的 RuntimeStepId 与 DiagnosticStep 不一致。");
        }

        return null;
    }

    private static bool IsDiagnosticKindCompatible(string diagnosticKind, DiagnosticsModuleEventKind eventKind)
    {
        var normalized = Normalize(diagnosticKind);
        return eventKind switch
        {
            DiagnosticsModuleEventKind.KernelTrace => normalized is "kernel" or "kerneltrace",
            DiagnosticsModuleEventKind.ExecutionRuntimeStep => normalized is "runtime" or "executionruntime" or "executionruntimestep",
            DiagnosticsModuleEventKind.ModuleCall => normalized is "module" or "modulecall",
            DiagnosticsModuleEventKind.ValidationRejection => normalized is "validation" or "validationrejection",
            _ => false,
        };
    }

    private static string Normalize(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static RuntimeStepResult CreateBlockedResult(
        DiagnosticStep step,
        ExecutionRuntimeContext context,
        ExecutionFailure failure)
        => new(
            step.StepId,
            RuntimeStepKind.Diagnostic,
            RuntimeStepResultStatus.Blocked,
            failure: failure,
            diagnosticsRef: $"diagnostics://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}",
            traceRef: $"trace://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}");
}
