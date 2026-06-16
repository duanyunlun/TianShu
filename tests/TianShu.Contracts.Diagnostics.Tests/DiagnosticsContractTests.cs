using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Diagnostics.Tests;

public sealed class DiagnosticsContractTests
{
    [Fact]
    public void AuditRecord_RejectsBlankCategory()
    {
        Assert.Throws<ArgumentException>(() => new AuditRecord(
            new AuditRecordId("audit-001"),
            " ",
            "message"));
    }

    [Fact]
    public void ExecutionTrace_PreservesAttemptsAndCheckpoints()
    {
        var trace = new ExecutionTrace(
            new ExecutionTraceId("trace-001"),
            new ExecutionId("execution-001"),
            new[]
            {
                new AttemptSummary(new ExecutionId("execution-001"), 1, true, DateTimeOffset.UtcNow),
            },
            recoveryCheckpoints: new[]
            {
                new RecoveryCheckpoint(new ExecutionId("execution-001"), "provider"),
            });

        Assert.Single(trace.Attempts);
        Assert.Single(trace.RecoveryCheckpoints);
    }

    [Fact]
    public void DiagnosticsModuleEvent_ShouldRequireRuntimeStepForRuntimeAndModuleCallEvents()
    {
        Assert.Throws<ArgumentException>(() => new DiagnosticsModuleEvent(
            DiagnosticsModuleEventKind.ExecutionRuntimeStep,
            "diagnostics/runtime/step",
            StructuredValue.FromPlainObject(new { ok = true }),
            new DiagnosticsModuleEventContext(executionId: new ExecutionId("execution-001"))));

        Assert.Throws<ArgumentException>(() => new DiagnosticsModuleEvent(
            DiagnosticsModuleEventKind.ModuleCall,
            "diagnostics/module/call",
            StructuredValue.FromPlainObject(new { ok = true }),
            new DiagnosticsModuleEventContext(moduleId: "memory.identity")));
    }

    [Fact]
    public void DiagnosticsModuleContract_ShouldUseDiagnosticsDescriptorAndEmitResultShape()
    {
        var descriptor = BuiltInModuleDescriptors.Diagnostics();
        var result = new DiagnosticsModuleEmitResult(
            success: true,
            "diagnostics/kernel/trace",
            DiagnosticsModuleEventKind.KernelTrace,
            diagnosticsRef: "diagnostics://kernel/run/event",
            traceRef: "trace://kernel/run/event");

        Assert.Equal(ModuleKind.Diagnostics, descriptor.Kind);
        Assert.Equal("diagnostics", descriptor.ModuleId);
        Assert.Contains(descriptor.Capabilities, capability => capability.CapabilityId == "diagnostics.capability");
        Assert.True(result.Success);
        Assert.Equal(DiagnosticsModuleEventKind.KernelTrace, result.Kind);
        Assert.StartsWith("diagnostics://", result.DiagnosticsRef, StringComparison.Ordinal);
    }
}
