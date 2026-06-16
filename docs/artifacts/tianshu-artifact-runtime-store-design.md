# TianShu Artifact / State / Projection Module 设计

## 1. 文档定位

Artifact / State / Projection 属于 Module Plane。它提供 artifact 当前态、artifact projection、只读 projection snapshot 和 checkpoint materialization 能力，但不拥有 Kernel 编排权、StageGraph 解释权、ContextPolicy、ModelRoutePolicy 或业务主状态机。

本文件以 `docs/tianshu-architecture-spec.md` 与 `docs/architecture/tianshu-module-plane-design.md` 为基线，作为 Artifact / State / Projection Module 当前代码落地与后续验收基线。

## 2. 当前项目

| 项目 | 当前代码责任 | 新基线下的职责 |
| --- | --- | --- |
| `src/Contracts/TianShu.Contracts.Artifacts` | artifact metadata、content、command、query。 | 保持 artifact typed command / model，不反向引用 projection contract。 |
| `src/Contracts/TianShu.Contracts.Projections` | projection payload、subscription、projection event。 | 定义 `IArtifactStateProjectionModule`、artifact module invocation、projection query result、checkpoint materialization request。 |
| `src/Core/TianShu.ArtifactStore` | 默认 artifact metadata / content store。 | 通过 `ArtifactStateProjectionModuleAdapter` 将 `IArtifactStore` 与 projection runtime stores 包裹为 module。 |
| `src/Core/TianShu.ProjectionStores` | projection snapshot、trace、replay checkpoint store。 | 暴露 `IProjectionSnapshotSource` 只读 projection 来源；写入仍由物化流程和 runtime store 内部使用。 |
| `src/Hosting/TianShu.AppHost.State` | AppHost thread、rollout、agent job、guard 等宿主状态实现。 | 只保留宿主状态持久化和映射，不拥有 Kernel state machine、StageGraph interpreter 或 validator。 |
| `src/Execution/TianShu.Execution.Runtime` | RuntimeStep 执行边界。 | 通过 `ExecutionRuntimeArtifactModuleBridge` 执行 `ArtifactStep` 和 projection module query。 |

## 3. 模块契约

归属项目：`src/Contracts/TianShu.Contracts.Projections/TianShu.Contracts.Projections.csproj`

```csharp
public interface IArtifactStateProjectionModule : IModuleHealthCheck
{
    ValueTask<ArtifactModuleMutationResult> ExecuteArtifactAsync(
        ArtifactModuleMutationInvocation invocation,
        CancellationToken cancellationToken);

    ValueTask<ArtifactProjectionModuleQueryResult> QueryProjectionAsync(
        ArtifactProjectionModuleQueryInvocation invocation,
        CancellationToken cancellationToken);

    ValueTask<ArtifactCheckpointMaterializationResult> MaterializeCheckpointAsync(
        ArtifactCheckpointMaterializationInvocation invocation,
        CancellationToken cancellationToken);
}
```

`ArtifactModuleMutationInvocation` 必须保留：

- 原始 `ArtifactStep`。
- `ArtifactModuleMutation` typed union。
- `ArtifactModuleInvocationContext`，包含 `RuntimeStepId`、`SourceIntentId`、`SourceGraphId`、`SourceStageId`、`SourceKernelOperationId`、`KernelRunId`、`ExecutionId`、permission、side effect、metadata。

当前 mutation union：

- `PublishArtifactModuleMutation`
- `PromoteArtifactModuleMutation`
- `AttachArtifactToTaskModuleMutation`

## 4. Projection Source

归属项目：`src/Core/TianShu.ProjectionStores/TianShu.ProjectionStores.csproj`

```csharp
public interface IProjectionSnapshotSource
{
    Task<ProjectionSnapshotRecord?> GetAsync(
        ProjectionSnapshotKey key,
        CancellationToken cancellationToken);
}

public interface IProjectionSnapshotStore : IProjectionSnapshotSource
{
    Task<ProjectionSnapshotRecord> UpsertAsync(...);
    Task<ProjectionSnapshotRecord> ResetAsync(...);
}
```

上层 module query 只能依赖 `IProjectionSnapshotSource` 的读能力。Module 返回 `ProjectionSnapshotView`，不得向上暴露 `ProjectionSnapshotRecord` 作为正式对外结果。

## 5. Checkpoint Materialization

`ArtifactCheckpointMaterializationRequest` 必须同时携带：

- `KernelRunId`
- `StageGraphId`
- `StageId`
- `ExecutionId`
- `ExecutionTraceId`
- `RecoveryCheckpoint`

`RecoveryCheckpoint.ExecutionId` 必须与 request 的 `ExecutionId` 一致。Module 实现可将 checkpoint 写入 `IProjectionRuntimeStores.RecordRecoveryCheckpointAsync`，并返回可追踪的 `checkpointRef`。

## 6. Execution Runtime Bridge

归属项目：`src/Execution/TianShu.Execution.Runtime/TianShu.Execution.Runtime.csproj`

```csharp
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
```

Bridge 必须 fail closed：

- module kind 必须是 `ModuleKind.ArtifactStateProjection`。
- `ArtifactStep.ArtifactOperation` 必须与 mutation operation name 一致。
- 结果必须包含 diagnostics ref 与 trace ref。

当前 `ArtifactStep` 尚未携带 `ModuleId`，因此本阶段不做 module id 精确匹配；后续若修订 RuntimeStep contract，应在 `ArtifactStep` 中加入目标 module id。

## 7. AppHost.State 边界

`src/Hosting/TianShu.AppHost.State` 只允许保存宿主状态、线程记录、rollout 记录、agent job 记录和 guard 状态。它不得引入：

- `IStableKernelCore`
- `IAdaptiveOrchestrator`
- `IStageGraphInterpreter`
- `IKernelValidator`
- `KernelRunStateMachine`
- `StageGraphInterpreter`

## 8. 验收标准

- `IArtifactStateProjectionModule` 暴露 artifact mutation、projection query、checkpoint materialization 三类能力。
- artifact publish / promote / attach 通过 `ArtifactStep` 与 `ArtifactStateProjectionModuleAdapter` 执行。
- projection query 只依赖 `IProjectionSnapshotSource`，对外只返回 `ProjectionSnapshotView`。
- checkpoint materialization 与 Kernel run、StageGraph、Stage、Execution 关联。
- AppHost.State 不拥有 Kernel state machine / interpreter / validator 语义。
- 增加 contract、ArtifactStore、ProjectionStores、Execution Runtime、AppHost.State 边界测试。
