# TianShu Memory / Identity Module 设计

## 1. 文档定位

Memory / Identity 属于 Module Plane。它提供身份、记忆、overlay、候选、取代链、反馈、引用和审计能力，但不拥有 Kernel 编排权、ContextPolicy、StageGraph、ModelRoutePolicy 或 GovernanceEnvelope。

本文件以 `docs/tianshu-architecture-spec.md` 与 `docs/architecture/tianshu-module-plane-design.md` 为基线，作为 Memory / Identity Module 当前代码落地与后续验收基线。

## 2. 涉及项目

| 项目 | 当前代码责任 | Memory / Identity Module 责任 |
| --- | --- | --- |
| `src/Contracts/TianShu.Contracts.Memory` | 记忆对象、query、command、result。 | 定义 `IMemoryModule`、`MemoryModuleInvocationContext`、query/mutation invocation 与结果投影。 |
| `src/Contracts/TianShu.Contracts.Identity` | 身份对象与 query。 | 保持 identity typed contract，不拥有记忆写入策略。 |
| `src/Core/TianShu.IdentityMemory` | 默认本地记忆与身份实现。 | 通过 `MemoryServiceModuleAdapter` 将现有 `IMemoryService` 包裹为 `IMemoryModule`。 |
| `src/Tools/TianShu.Tools.Memory` | 记忆工具 provider / handler。 | 通过 `TianShuToolHandlerAdapter` 对齐为 `ITianShuTool` 可执行 surface。 |
| `src/Contracts/TianShu.Contracts.Modules` | Module Plane 通用契约。 | Memory / Identity Module 必须可投影为 `ModuleDescriptor` 与 health / smoke check surface。 |
| `tests/TianShu.Contracts.Memory.Tests` | Memory contract 测试。 | 验证 module surface、governance context、禁止完整推理轨迹写入。 |
| `tests/TianShu.IdentityMemory.Tests` | Memory implementation 测试。 | 验证 adapter query/mutation 路由和 Memory tool adapter。 |

## 3. 接口骨架归属

归属项目：`src/Contracts/TianShu.Contracts.Memory/TianShu.Contracts.Memory.csproj`

```csharp
public interface IMemoryModule : IModuleHealthCheck
{
    ValueTask<MemoryModuleQueryResult> QueryAsync(
        MemoryModuleQueryInvocation invocation,
        CancellationToken cancellationToken);

    ValueTask<MemoryMutationResult> MutateAsync(
        MemoryModuleMutationInvocation invocation,
        CancellationToken cancellationToken);
}

public sealed record MemoryModuleInvocationContext
{
    public string RuntimeStepId { get; }
    public string SourceIntentId { get; }
    public string SourceGraphId { get; }
    public string SourceStageId { get; }
    public string SourceKernelOperationId { get; }
    public PermissionEnvelope Permission { get; }
    public SideEffectProfile SideEffect { get; }
    public MemoryOperationContext OperationContext { get; }
    public MetadataBag Metadata { get; }
}
```

Memory query invocation 使用 typed query union：

- `ListMemoryProvidersModuleQuery`
- `ListMemorySpacesModuleQuery`
- `ResolveMemoryOverlayModuleQuery`
- `FilterMemoryModuleQuery`
- `ListMemoryReviewsModuleQuery`
- `ExportMemoryModuleQuery`

Memory mutation invocation 使用 typed mutation union：

- `AddMemoryModuleMutation`
- `ImportMemoryModuleMutation`
- `BindMemoryProviderModuleMutation`
- `ForgetMemoryModuleMutation`
- `DeleteMemoryModuleMutation`
- `SupersedeMemoryModuleMutation`
- `ApproveMemoryReviewModuleMutation`
- `RecordMemoryFeedbackModuleMutation`
- `RecordMemoryCitationModuleMutation`

## 4. 当前实现

归属项目：`src/Core/TianShu.IdentityMemory/TianShu.IdentityMemory.csproj`

```csharp
public sealed class MemoryServiceModuleAdapter : IMemoryModule
{
    public ModuleDescriptor Descriptor { get; }

    public ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken);

    public ValueTask<MemoryModuleQueryResult> QueryAsync(
        MemoryModuleQueryInvocation invocation,
        CancellationToken cancellationToken);

    public ValueTask<MemoryMutationResult> MutateAsync(
        MemoryModuleMutationInvocation invocation,
        CancellationToken cancellationToken);
}
```

当前 adapter 复用 `IMemoryService`：

- `FilterMemoryModuleQuery` 调用 `IMemoryService.FilterAsync`。
- `ListMemoryReviewsModuleQuery` 调用 `IMemoryService.ListReviewsAsync`。
- `ExportMemoryModuleQuery` 调用 `IMemoryService.ExportAsync`。
- `SupersedeMemoryModuleMutation` 调用 `IMemoryService.SupersedeAsync`。
- `ForgetMemoryModuleMutation` 调用 `IMemoryService.ForgetAsync`。
- `DeleteMemoryModuleMutation` 调用 `IMemoryService.DeleteAsync`。
- `RecordMemoryFeedbackModuleMutation` / `RecordMemoryCitationModuleMutation` 只记录受控证据，不直接覆盖长期事实。

`ResolveMemoryOverlayModuleQuery` 在 `IMemoryService` adapter 中返回 degraded projection；需要完整 identity runtime context 时，由后续 `ITianShuIdentityMemoryPlane` adapter 扩展。

## 5. 治理与审计

- Memory query 必须通过 `ModuleCapabilityStep` 或已批准的 tool invocation 进入。
- Memory mutation 必须携带 `MemoryModuleInvocationContext`，其中包括 RuntimeStep 来源、Kernel operation 来源、permission 和 side effect。
- `AddMemory` 表达新增事实，不得用于正式纠错覆盖；正式纠错必须使用 `SupersedeMemory`，并保留 supersede link。
- `ForgetMemory`、`DeleteMemory`、`SupersedeMemory` 必须由底层 provider / store 写入 audit record。
- `MemoryModuleMutationInvocation` 拒绝明显的完整模型思考链字段或来源片段，例如 `chain_of_thought`、`reasoning_trace`、`full_model_thoughts`。
- Memory Module 不保存完整模型思考链；只允许保存用户确认事实、工具证据、artifact 引用、反馈和可审计摘要。

## 6. Tool 对齐

`src/Tools/TianShu.Tools.Memory` 当前提供 `MemoryToolProvider` 与 handler。新架构下必须通过 `TianShuToolHandlerAdapter` 投影为 `ITianShuTool`，再由 Execution Runtime 的 tool bridge 执行。

Memory tool 规则：

- `memory_search` 和 `memory_explain_overlay` 是 read-only 能力。
- `memory_feedback` 是受治理写入能力，只记录反馈 / 审计证据，不直接覆盖长期事实。
- Memory tool descriptor 必须声明 permission、side effect、audit 和 implementation binding。

## 7. 验收标准

- `IMemoryModule` 暴露 query / mutation / health check 统一入口。
- Memory query 和 mutation 能关联 `RuntimeStepId`、`SourceIntentId`、`SourceGraphId`、`SourceStageId`、`SourceKernelOperationId`。
- supersede / forget / delete 不得静默覆盖旧事实，必须进入审计链路。
- Memory Module 不保存完整模型思考链。
- `TianShu.Tools.Memory` 能通过 `TianShuToolHandlerAdapter` 对齐为 `ITianShuTool`。
- Memory contract 不引用 `TianShu.IdentityMemory`、Execution Runtime、AppHost 或 provider 实现。
