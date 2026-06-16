# TianShu Diagnostics / Trace 设计

## 1. 文档定位

Diagnostics / Trace 属于 Module Plane，负责把 Kernel、Execution Runtime、Module 调用和 validation rejection 的事实记录为可审计、可脱敏、可引用的诊断事件。Diagnostics Module 只记录事实、摘要和引用，不拥有编排权，不生成 RuntimeStep，不决定 ModelRoutePolicy、ContextPolicy 或 GovernanceEnvelope。

本文件以 `docs/tianshu-architecture-spec.md` 为总架构基线，作为 Diagnostics / Trace 模块的代码落地和验收基线。

## 2. 当前项目代码

| 项目 | 当前职责 |
| --- | --- |
| `src/Contracts/TianShu.Contracts.Diagnostics` | 定义 `IDiagnosticsModule`、`DiagnosticsModuleEvent`、诊断事件包络、诊断采集策略、artifact writer、trace / attempt / checkpoint 等诊断契约。 |
| `src/Core/TianShu.Diagnostics` | 提供默认 Diagnostics Module adapter、redactor、collection policy、operation scope、event sink、artifact writer 和 provider diagnostics 统计实现。 |
| `src/Execution/TianShu.Execution.Runtime` | 提供 `ExecutionRuntimeDiagnosticsModuleBridge`，将 Kernel 已批准的 `DiagnosticStep` 交给 `IDiagnosticsModule` 执行。 |
| `src/Contracts/TianShu.Contracts.Execution` | 定义 `DiagnosticStep`、`RuntimeStepResult`、`TracePolicy` 与 Execution Runtime 上下文。 |
| `src/Contracts/TianShu.Contracts.Modules` | 定义 `ModuleDescriptor`、`ModuleKind.Diagnostics`、`BuiltInModuleDescriptors.Diagnostics` 与 `IModuleHealthCheck`。 |
| `src/Hosting/TianShu.AppHost.Tools.Runtime.Diagnostics` | 将统一诊断事件投影到现有 turn log、runtime notification 和 trace 查询面。 |

## 3. 正式契约骨架

归属项目：`src/Contracts/TianShu.Contracts.Diagnostics`

```csharp
public interface IDiagnosticsModule : IModuleHealthCheck
{
    ValueTask<DiagnosticsModuleEmitResult> EmitAsync(
        DiagnosticsModuleEvent diagnosticEvent,
        CancellationToken cancellationToken);
}

public enum DiagnosticsModuleEventKind
{
    KernelTrace = 1,
    ExecutionRuntimeStep = 2,
    ModuleCall = 3,
    ValidationRejection = 4,
}

public sealed record DiagnosticsModuleEventContext
{
    public KernelRunId? KernelRunId { get; }
    public ExecutionId? ExecutionId { get; }
    public string? RuntimeStepId { get; }
    public CoreIntentId? SourceIntentId { get; }
    public StageGraphId? SourceGraphId { get; }
    public StageId? SourceStageId { get; }
    public KernelOperationId? SourceKernelOperationId { get; }
    public string? ModuleId { get; }
    public string? CapabilityId { get; }
    public MetadataBag Metadata { get; }
}

public sealed record DiagnosticsModuleEvent
{
    public DiagnosticsModuleEventKind Kind { get; }
    public string EventName { get; }
    public StructuredValue Payload { get; }
    public DiagnosticsModuleEventContext Context { get; }
    public string? RejectionCode { get; }
    public string? FailureMessage { get; }
    public bool IsRetryable { get; }
    public DateTimeOffset Timestamp { get; }
    public MetadataBag Metadata { get; }
}

public sealed record DiagnosticsModuleEmitResult
{
    public bool Success { get; }
    public string EventName { get; }
    public DiagnosticsModuleEventKind Kind { get; }
    public string? DiagnosticsRef { get; }
    public string? TraceRef { get; }
    public string? DegradedReason { get; }
}
```

归属项目：`src/Core/TianShu.Diagnostics`

```csharp
public sealed class DiagnosticsModuleAdapter : IDiagnosticsModule
{
    public ModuleDescriptor Descriptor { get; }

    public ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken);

    public ValueTask<DiagnosticsModuleEmitResult> EmitAsync(
        DiagnosticsModuleEvent diagnosticEvent,
        CancellationToken cancellationToken);
}
```

归属项目：`src/Execution/TianShu.Execution.Runtime`

```csharp
public interface IExecutionRuntimeDiagnosticsModuleBridge
{
    Task<RuntimeStepResult> ExecuteAsync(
        DiagnosticStep step,
        ExecutionRuntimeContext context,
        IDiagnosticsModule module,
        DiagnosticsModuleEvent diagnosticEvent,
        CancellationToken cancellationToken);
}
```

## 4. 事件边界

- `KernelTrace`：记录 Kernel proposal、validation、execution、checkpoint、evaluation 的事实摘要和引用。
- `ExecutionRuntimeStep`：记录 RuntimeStep 执行、结果、错误、cost、latency 和 trace / replay reference；必须携带 `RuntimeStepId`。
- `ModuleCall`：记录 Module capability 调用、module id、capability id、权限和副作用摘要；必须携带 `RuntimeStepId`。
- `ValidationRejection`：记录 validator / policy 拒绝原因、rejection code、可恢复性和关联 Kernel source ids。

`DiagnosticsModuleEvent` 只能承载 typed `StructuredValue` 与 `MetadataBag`。禁止把 raw JSON 字符串、完整 provider payload、完整模型思考链或未脱敏 headers 当成正式诊断 payload。

## 5. 执行规则

- Diagnostics Module 必须通过 `BuiltInModuleDescriptors.Diagnostics` 或等价 `ModuleDescriptor` 声明 `ModuleKind.Diagnostics`。
- Execution Runtime 只能通过 `DiagnosticStep` 和 `ExecutionRuntimeDiagnosticsModuleBridge` 调用 Diagnostics Module。
- bridge 必须 fail closed：module kind 不匹配、`DiagnosticStep.DiagnosticKind` 与 event kind 不匹配、RuntimeStep id 不一致时返回 blocked result。
- `DiagnosticsModuleAdapter` 必须先统一脱敏 payload、metadata 和 failure message，再写入 `IDiagnosticEventSink`。
- 输出必须包含 `diagnosticsRef` 与 `traceRef`；Execution Runtime step 和 Module call 的 ref 必须能追溯到 `ExecutionId` 与 `RuntimeStepId`。

## 6. 默认脱敏规则

默认 redactor 必须拦截：

- authorization、token、secret、password、cookie。
- API key，包括 `api_key`、`api-key`、`apikey`。
- raw header、headers、http headers、set-cookie。
- Windows / Unix 绝对路径中暴露的完整本地路径。

Host Gateway 和外部查询面只能暴露脱敏摘要、计数、状态和 ref，不得暴露完整敏感 payload。

## 7. 验收标准

- `IDiagnosticsModule`、`DiagnosticsModuleEvent`、`DiagnosticsModuleEmitResult` 存在且由 contracts 测试覆盖。
- `DiagnosticsModuleAdapter` 能写入四类事件，并在 sink 前完成敏感 key、raw header、API key 和完整本地路径脱敏。
- `ExecutionRuntimeDiagnosticsModuleBridge` 能通过 `DiagnosticStep` 调用 Diagnostics Module，并对 descriptor / event kind / RuntimeStep id mismatch fail closed。
- 所有外部副作用事件必须能追踪到 RuntimeStep；没有 RuntimeStep 的 Module call 或 Execution Runtime step 必须被拒绝。
- 没有 trace / diagnostics ref 的 strategy 不得晋升。
