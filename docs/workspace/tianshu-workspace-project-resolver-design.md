# TianShu Workspace / Environment Module 设计

## 1. 文档定位

Workspace / Environment Module 属于 Module Plane。它把 workspace resolver 对齐为只读 environment capability：读取 workspace marker、manifest policy 和 trust policy，输出可追踪的 workspace facts。它不选择 StageGraph、不生成 KernelOperation、不构造 RuntimeStep、不执行 shell，也不写文件。

## 2. 当前项目

| 项目 | 当前用途 | 新基线下的职责 |
| --- | --- | --- |
| `src/Contracts/TianShu.Contracts.Environment` | 环境契约。 | 定义 `IWorkspaceModule`、`WorkspaceResolutionRequest`、`WorkspaceResolutionResult`、`WorkspaceFact`、`WorkspaceFactSource` 与 `WorkspaceModuleInvocationContext`。 |
| `src/Contracts/TianShu.Contracts.Modules` | Module Plane 通用契约。 | `BuiltInModuleDescriptors.WorkspaceEnvironment` 声明 `workspace.environment` module descriptor。 |
| `src/Core/TianShu.Configuration` | resolver manifest 配置。 | 读取 `modules/workspace/resolvers/*/resolver.toml` 并输出 `WorkspaceResolverEffectivePolicy`。 |
| `src/Core/TianShu.RuntimeComposition` | module 组合实现。 | `WorkspaceResolverRuntimeComposition.CreateWorkspaceModule` 创建内置只读 `IWorkspaceModule`。 |
| `src/Execution/TianShu.Execution.Runtime` | RuntimeStep 执行边界。 | `ExecutionRuntimeWorkspaceModuleBridge` 只通过 `ModuleCapabilityStep` 调用 `IWorkspaceModule`。 |

## 3. 契约骨架

归属项目：`src/Contracts/TianShu.Contracts.Environment`。

```csharp
public interface IWorkspaceModule : IModuleHealthCheck
{
    ValueTask<WorkspaceResolutionResult> ResolveAsync(
        WorkspaceResolutionRequest request,
        WorkspaceModuleInvocationContext context,
        CancellationToken cancellationToken);
}

public sealed record WorkspaceResolutionRequest(
    string WorkspacePath,
    IReadOnlyList<string> RootMarkers,
    IReadOnlyList<string> IgnoreGlobs,
    bool FailClosedWhenUntrusted);

public sealed record WorkspaceResolutionResult(
    WorkspaceResolutionStatus Status,
    IReadOnlyList<WorkspaceFact> Facts,
    IReadOnlyList<WorkspaceFactSource> Sources,
    IReadOnlyList<string> DiagnosticsRefs,
    IReadOnlyList<string> Issues);
```

`WorkspaceFact` 必须能投影为 `ContextSourceCandidate`，其中 `SourceKind = ContextSourceKind.WorkspaceFact`，并携带 `workspace-fact://{factId}` evidence ref。Diagnostics 只接收 facts、source refs、issue code 和 diagnostics ref，不接收 resolver 私有状态。

## 4. 规则

- resolver 只能输出 workspace facts，不输出 Kernel decision。
- resolver 只允许 `SideEffectLevel.ReadOnly` 或更低副作用；写入 workspace 的调用必须被拒绝。
- resolver 调用必须携带 RuntimeStep 来源：runtime step、intent、graph、stage、kernel operation。
- resolver 必须携带 `module.workspace.environment` 或 `module.workspace.environment.resolve` scope。
- resolver 不执行 shell，不调用 mutating filesystem tool，不创建、修改或删除 workspace 文件。
- marker 缺失或 trust policy 需要 prompt 时，结果降级为 `DegradedReadOnly`，并输出 read-only notice fact。
- workspace 路径不存在、不可信 policy 要求 fail closed、权限缺失或副作用超限时，结果为 `Rejected`。
- `ExecutionRuntimeWorkspaceModuleBridge` 必须先执行 RuntimeStep governance 校验，再检查 module descriptor、module id、module kind 和只读副作用。

## 5. 验收标准

- `IWorkspaceModule` 位于 `TianShu.Contracts.Environment`，并继承 `IModuleHealthCheck`。
- 内置 workspace module descriptor 为 `workspace.environment`、`ModuleKind.WorkspaceEnvironment`、`SideEffectLevel.ReadOnly`。
- workspace facts 可追踪到 `WorkspaceFactSource`，并能进入 `ContextPolicy`。
- workspace resolution 输出 diagnostics refs 和 issue code，供 Diagnostics Module 记录。
- 损坏、不可信或缺权限 resolver 不进入正式执行输入。
- 测试覆盖契约、只读 resolver、source tracking、read-only degraded、fail closed 和 RuntimeStep bridge。
