# TianShu Execution Runtime 设计

## 1. 文档定位

Execution Runtime 负责执行 Kernel 批准的 `ExecutionPlan` / `RuntimeStep`。它是流水线执行层，不拥有智能编排权。

## 2. 当前项目

| 项目 | 当前用途 | 新基线下的职责 |
| --- | --- | --- |
| `src/Execution/TianShu.Execution.Protocol` | 执行协议。 | 承载 execution wire / protocol contracts。 |
| `src/Execution/TianShu.Execution.Runtime` | 执行运行时。 | 执行 RuntimeStep、调度 provider/tool/module、生成结果和记录。 |
| `src/Hosting/TianShu.AppHost.Tools.Runtime` | 现有工具运行时实现。 | 作为迁移输入，未来被 Execution Runtime bridge 调用。 |
| `src/Provider/TianShu.Provider.Abstractions` | provider 抽象。 | Provider Module 能力调用边界。 |

## 3. 接口骨架归属

归属项目：接口契约位于 `src/Contracts/TianShu.Contracts.Execution`，执行实现位于 `src/Execution/TianShu.Execution.Runtime`。

```csharp
public interface IExecutionRuntime
{
    Task<ExecutionRunResult> ExecuteAsync(
        ExecutionPlan plan,
        ExecutionRuntimeContext context,
        CancellationToken cancellationToken);

    Task<RuntimeStepResult> ExecuteStepAsync(
        RuntimeStep step,
        ExecutionRuntimeContext context,
        CancellationToken cancellationToken);
}

public interface IExecutionRuntimeToolBridge
{
    Task<RuntimeStepResult> ExecuteAsync(
        ToolInvocationStep step,
        ExecutionRuntimeContext context,
        ITianShuTool tool,
        CancellationToken cancellationToken);
}

public interface IExecutionRuntimeProviderBridge
{
    Task<RuntimeStepResult> ExecuteAsync(
        ModelInvocationStep step,
        ExecutionRuntimeContext context,
        IProviderModule provider,
        CancellationToken cancellationToken);
}
```

## 4. 执行规则

- 缺少 `SourceGraphId`、`SourceStageId`、`SourceKernelOperationId` 或 `PermissionEnvelope` 的 step 必须拒绝。
- RuntimeStep 的 side effect 不得超过 governance envelope。
- RuntimeStep 的 side effect 不得为 `Unspecified`，governance envelope 的 side effect ceiling 也不得为 `Unspecified`。
- ExecutionPlan 中 step 的 `SourceIntentId` 与 `SourceGraphId` 必须和 plan 来源一致。
- Model 调用必须通过 `ModelInvocationStep` 和 `IExecutionRuntimeProviderBridge` 执行，bridge 必须校验 step 的 provider id 与 descriptor 一致。
- Tool 调用必须通过 `ToolInvocationStep` 和 `IExecutionRuntimeToolBridge` 执行，bridge 必须校验 step 的 tool id 与 descriptor 一致。
- Provider bridge 必须把 `ProviderToolDirectiveEvent` 投影为结构化 `toolRequests[]`，并带 `tool_requests_available` 信号；无工具请求的 completion 投影为最终回复信号。
- Tool bridge 必须把 `ToolInvocationResult` 投影为结构化 `toolResults[]`，并带 `tool.results.materialized` 信号；该输出是下一轮 `model-reason` 的 `ToolResultProviderInputItem` 证据来源。
- Provider / Tool bridge 的成功输出必须携带 `runtimePlanId / stepId / stepKind / sourceIntentId / sourceGraphId / sourceStageId / sourceKernelOperationId`，用于从 runtime result 和 metrics 重建 graph -> stage -> step -> model/tool call -> result 关系。
- Execution Runtime 可以调用 Module Plane，但必须经过 module/tool descriptor、permission、budget、audit 检查。
- Execution Runtime 必须产出 result、event、trace ref、diagnostics ref。
- `KernelRuntimeReplayProjector` 对普通固定 `RunAsync` 必须严格要求静态 plan 中的每个 step 都有 runtime result；对反应式 `RunReactiveAsync` 必须以实际 `RuntimeResult.StepResults` 为准复盘，因为工具 step 会按模型输出的 `toolRequests[]` 动态物化，不一定存在于静态 plan 骨架中。
- Replay summary 必须区分 stage-level path 与 step-level summaries：`StagePath` 表示 Kernel 实际确认的 stage 路径；`Steps` 表示 RuntimeStep 级投影。`stage.finalize` 可以对应 `StateCommitStep` 与 `DiagnosticStep` 两条 step summary，但不能据此解释为 finalize stage 被执行两次。
- RuntimeStep 分型边界固定为：provider 调用只能是 `ModelInvocationStep`；工具调用只能是 `ToolInvocationStep`；memory / workspace query 是 `ModuleCapabilityStep`；artifact publish / promote / attach 是 `ArtifactStep`；diagnostics emit 是 `DiagnosticStep`；interrupt / resume / host 控制入口是 `HostInteractionStep`。

## 5. 验收标准

- Execution Runtime 不生成 StageGraph。
- Execution Runtime 不调用 Adaptive Orchestrator。
- Execution Runtime 不晋升 strategy。
- 所有外部副作用可追踪到 KernelOperation。
- registered provider/tool runtime path 验收必须能跑通 `prepare-context -> model-reason -> tool-exec -> model-reason -> finalize`，并从 replay summary 中看到 stage-level path、RuntimeStep summaries、runtime trace ref、provider metrics 与 tool metrics；该验收不等同外部 provider 凭据 live 验收。
