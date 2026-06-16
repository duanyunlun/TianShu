# TianShu Model Route 设计

## 1. 文档定位

Model Route 位于 Kernel StageGraph 和 Execution Runtime 模型调用之间。Kernel 负责生成并批准 `ModelRoutePolicy`，Execution Runtime 只消费 `ApprovedModelRoutePolicy` 并物化 `ModelInvocationStep`。Provider Module 只响应已解析的 `ProviderInvocationRequest`，不选择 route strategy、provider、model 或治理边界。

本文件以 `docs/tianshu-architecture-spec.md` 为总架构基线，作为 Model Route 当前代码落地和后续验收基线。

## 2. 当前项目代码

| 项目 | 当前职责 |
| --- | --- |
| `src/Contracts/TianShu.Contracts.Kernel` | 定义 `ModelRoutePolicy`、`ModelRouteCandidateBinding`、`ApprovedModelRoutePolicy`、`ModelRoutePolicyApplicationReport`。 |
| `src/Contracts/TianShu.Contracts.Execution` | 定义 `ModelInvocationStep`，承载 Kernel 来源、权限、副作用、预算、trace policy 和 `ProviderInvocationRequest`。 |
| `src/Execution/TianShu.Execution.Runtime` | 定义 `ExecutionRuntimeModelRouteBridge`，只从已批准 route policy 物化 `ModelInvocationStep`，并通过 `ExecutionRuntimeProviderBridge` 调用 Provider Module。 |
| `src/Contracts/TianShu.Contracts.Provider` | 定义 `ProviderInvocationRequest`、`ProviderInvocationContext`、`ProviderInputItem`、`ProviderStreamEvent`。 |
| `src/Provider/TianShu.Provider.Abstractions` | 定义 `IProviderModule`，只接受 `ProviderInvocationRequest`。 |
| `src/Presentations/TianShu.Cli` | `/model-route route --json` 输出 Host projection，不输出 provider secret、raw endpoint secret 或 runtime 私有对象。 |

## 3. 正式契约骨架

归属项目：`src/Contracts/TianShu.Contracts.Kernel`

```csharp
public sealed record ModelRoutePolicy
{
    public string PolicyId { get; }
    public string? RouteKind { get; }
    public IReadOnlyList<string> RouteCandidateIds { get; }
    public IReadOnlyList<ModelRouteCandidateBinding> Candidates { get; }
    public string? PreferredRouteId { get; }
    public string? FallbackRouteId { get; }
    public bool FailClosedWhenMissingCandidate { get; }
    public MetadataBag Metadata { get; }
}

public sealed record ModelRouteCandidateBinding
{
    public string CandidateId { get; }
    public string ProviderModuleId { get; }
    public string ProviderKey { get; }
    public string Model { get; }
    public int CandidateIndex { get; }
    public string? Protocol { get; }
    public string? EndpointRef { get; }
    public string? SecretRef { get; }
    public IReadOnlyList<string> Capabilities { get; }
    public string? UnavailableReason { get; }
    public MetadataBag Metadata { get; }
}

public sealed record ApprovedModelRoutePolicy
{
    public ModelRoutePolicy Policy { get; }
    public CoreIntentId SourceIntentId { get; }
    public StageGraphId SourceGraphId { get; }
    public StageId SourceStageId { get; }
    public KernelOperationId SourceKernelOperationId { get; }
    public DateTimeOffset ApprovedAt { get; }
    public IReadOnlyList<string> ValidationRefs { get; }
}
```

归属项目：`src/Execution/TianShu.Execution.Runtime`

```csharp
public interface IExecutionRuntimeModelRouteBridge
{
    Task<ExecutionRuntimeModelRouteResult> MaterializeModelInvocationStepAsync(
        ExecutionRuntimeModelRouteRequest request,
        CancellationToken cancellationToken);
}
```

## 4. 执行规则

- Kernel validator 必须拒绝 fail-closed 且没有任何 route candidate 的 `ModelRoutePolicy`。
- Kernel validator 必须拒绝首选 candidate 不在已批准候选列表中的 `ModelRoutePolicy`。
- Execution Runtime 只能用 `ApprovedModelRoutePolicy` 物化 `ModelInvocationStep`。
- Execution Runtime 输出的 provider request 必须只包含 provider key、model、provider-neutral input、conversation context 和安全 metadata。
- route diagnostics 可以保留 provider、model、protocol、candidate index、endpoint ref、diagnostics correlation id，但不得包含 secret 明文。
- 缺失 route candidate 或候选不可用必须 fail closed，不得回退到根级 `model`、旧 provider 字段或 Provider Module 私有选择逻辑。

## 5. Provider 边界

Provider Module 的正式入口是：

```text
ApprovedModelRoutePolicy
  -> ExecutionRuntimeModelRouteBridge
  -> ModelInvocationStep
  -> ExecutionRuntimeProviderBridge
  -> ProviderInvocationRequest
  -> IProviderModule.InvokeAsync
```

Provider implementation 可以做协议映射、认证引用解析、stream 处理和脱敏诊断；不得读取配置重新选择模型，不得自行选择 fallback candidate，不得修改 `ModelRoutePolicy`，不得生成 `StageGraph` 或 `RuntimeStep`。

## 6. Host Projection

`/model-route route --json` 输出必须是 Host Gateway projection 形态的结构化结果，projection kind 为 `host.model_route.diagnostic`。该 projection 只包含 route set、route kind、registered route coverage、preferred candidate、fallback candidates 和安全 candidate diagnostics。

CLI 不得把 `/model-route` 诊断写成对话内容，也不得输出 provider secret、secret value、raw runtime object 或 Kernel 可变内部对象。

## 7. 验收标准

- `ModelRoutePolicy`、`ModelRouteCandidateBinding`、`ApprovedModelRoutePolicy` 位于 `TianShu.Contracts.Kernel` 并有 contract tests。
- Kernel validator 覆盖缺失 candidate 和非法 preferred candidate fail-closed。
- Execution Runtime 通过 `ExecutionRuntimeModelRouteBridge` 物化 `ModelInvocationStep`，并有 provider invocation 测试。
- Provider Module 只响应已解析 `ProviderInvocationRequest`。
- CLI `/model-route route --json` 输出 Host projection，并有不泄露 secret 的测试。
