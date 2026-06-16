# TianShu Provider Module 设计

## 1. 文档定位

Provider 属于 Module Plane。它只负责模型厂商能力声明、认证引用、协议映射、stream 处理和脱敏诊断，不拥有 Kernel 编排权、ContextPolicy、ModelRoutePolicy、StageGraph 选择权或 Control Plane 治理权。

本文件以 `docs/tianshu-architecture-spec.md` 为总架构基线，作为 Provider Module 当前代码落地与后续验收基线。

## 2. 涉及项目

| 项目 | 当前代码责任 | Provider Module 目标责任 |
| --- | --- | --- |
| `src/Contracts/TianShu.Contracts.Provider` | provider 公共契约。 | 定义 `ProviderDescriptor`、`ProviderInvocationRequest`、`ProviderInvocationContext`、`ProviderStreamEvent`、provider capability、endpoint、model catalog、failure 和 usage contract。 |
| `src/Provider/TianShu.Provider.Abstractions` | provider 抽象层。 | 定义 `IProviderModule` 与 `ProviderModuleDescriptorFactory`，作为可替换 Provider Module 的公共入口。 |
| `src/Provider/TianShu.Provider.OpenAI` | OpenAI provider 实现。 | 暴露 OpenAI `ProviderDescriptor`，并按 `IProviderModule` 目标入口收敛。 |
| `src/Provider/TianShu.Provider.OpenAICompatible` | OpenAI-compatible provider 实现。 | 暴露 OpenAI-compatible `ProviderDescriptor`，并按 `IProviderModule` 目标入口收敛。 |
| `src/Provider/TianShu.Provider.Anthropic` | Anthropic provider 实现。 | 暴露 Anthropic `ProviderDescriptor`，并按 `IProviderModule` 目标入口收敛。 |
| `src/Provider/TianShu.Provider.Google` | Google provider 实现。 | 暴露 Google `ProviderDescriptor`，并按 `IProviderModule` 目标入口收敛。 |
| `src/Execution/TianShu.Execution.Runtime` | RuntimeStep 执行。 | 通过 `ExecutionRuntimeProviderBridge` 只执行 Kernel 已批准的 `ModelInvocationStep`。 |
| `src/Contracts/TianShu.Contracts.Modules` | Module Plane 通用契约。 | Provider descriptor 必须可投影为统一 `ModuleDescriptor`。 |
| `tests/TianShu.Contracts.Provider.Tests` | provider contract 测试。 | 验证 provider descriptor 默认治理、调用请求与 stream contract。 |
| `tests/TianShu.Provider.OpenAI.Tests` | provider 实现测试集合。 | 验证内置 provider descriptor 的 capability、permission、side effect、audit 和 implementation binding。 |
| `tests/TianShu.Execution.Runtime.Tests` | Execution Runtime 测试。 | 验证 provider bridge 只能从 `ModelInvocationStep` 调用 `IProviderModule`。 |

## 3. 当前接口骨架

归属项目：`src/Provider/TianShu.Provider.Abstractions/TianShu.Provider.Abstractions.csproj`

```csharp
public interface IProviderModule
{
    ProviderDescriptor Descriptor { get; }

    IAsyncEnumerable<ProviderStreamEvent> InvokeAsync(
        ProviderInvocationRequest request,
        CancellationToken cancellationToken);
}
```

`IProviderModule` 的调用入口只接受 `ProviderInvocationRequest`。Provider 不应额外接收 `ProviderInvocationContext` 参数；Execution Runtime 必须把来源追踪、权限边界和 side effect 边界写入 `ProviderInvocationRequest.InvocationContext`。

归属项目：`src/Contracts/TianShu.Contracts.Provider/TianShu.Contracts.Provider.csproj`

```csharp
public sealed record ProviderDescriptor
{
    public string ProviderId { get; }
    public string DisplayName { get; }
    public ProviderProtocolKind ProtocolKind { get; }
    public ProviderCapabilityProfile Capabilities { get; }
    public IReadOnlyList<ProviderModelDescriptor> Models { get; }
    public ProviderEndpointDescriptor? Endpoint { get; }
    public PermissionEnvelope Permission { get; }
    public SideEffectProfile SideEffects { get; }
    public MetadataBag Metadata { get; }
}

public sealed record ProviderInvocationRequest
{
    public ExecutionId ExecutionId { get; }
    public string ProviderKey { get; }
    public string Model { get; }
    public ProviderConversationContext Conversation { get; }
    public IReadOnlyList<ProviderInputItem> Inputs { get; }
    public ProviderTurnState? PreviousTurnState { get; }
    public MetadataBag Metadata { get; }
    public ProviderInvocationContext? InvocationContext { get; }
}

public sealed record ProviderInvocationContext
{
    public string RuntimeStepId { get; }
    public string SourceIntentId { get; }
    public string SourceGraphId { get; }
    public string SourceStageId { get; }
    public string SourceKernelOperationId { get; }
    public PermissionEnvelope Permission { get; }
    public SideEffectProfile SideEffect { get; }
    public MetadataBag Metadata { get; }
}
```

`ProviderDescriptor` 默认治理约束：

- `PermissionEnvelope.Scopes` 默认包含 `provider.{providerId}.invoke`。
- `SideEffectProfile.Level` 默认是 `ExternalNetwork`。
- `SideEffectProfile.AffectedResources` 默认包含 `provider:{providerId}` 与 `network`。
- `SideEffectProfile.RequiresAudit` 默认是 `true`。

归属项目：`src/Execution/TianShu.Execution.Runtime/TianShu.Execution.Runtime.csproj`

```csharp
public interface IExecutionRuntimeProviderBridge
{
    Task<RuntimeStepResult> ExecuteAsync(
        ModelInvocationStep step,
        ExecutionRuntimeContext context,
        IProviderModule provider,
        CancellationToken cancellationToken);
}
```

`ExecutionRuntimeProviderBridge` 的职责是：

- 只接受 `ModelInvocationStep` 与 `IProviderModule`。
- 校验 `ModelInvocationStep.ProviderModuleId` 必须等于 `ProviderDescriptor.ProviderId`。
- 只消费 Execution Runtime 已按 `ApprovedContextPolicy` 准备好的 `ProviderInputItem`，不在 Provider bridge 或 Provider Module 内重新裁切上下文。
- 从 `ModelInvocationStep` 生成 `ProviderInvocationRequest.InvocationContext`。
- 将 `ProviderFailureEvent` 转为 blocked `RuntimeStepResult`。
- 生成 provider 调用对应的 diagnostics ref 与 trace ref。

## 4. 内置 descriptor

当前内置 Provider Module descriptor：

| Provider | Descriptor 类型 | 协议 | 默认 endpoint | Secret reference |
| --- | --- | --- | --- | --- |
| OpenAI | `OpenAiProviderModuleDescriptor` | `OpenAiResponses` | `https://api.openai.com` | `OPENAI_API_KEY` |
| OpenAICompatible | `OpenAiCompatibleProviderModuleDescriptor` | `OpenAiChatCompletions` | `https://api.openai.com` | `OPENAI_COMPATIBLE_API_KEY` |
| Anthropic | `AnthropicProviderModuleDescriptor` | `AnthropicMessages` | Anthropic 默认 endpoint | `ANTHROPIC_API_KEY` |
| Google | `GoogleProviderModuleDescriptor` | `GoogleGenerative` | Google Generative 默认 endpoint | `GOOGLE_API_KEY` |

内置 descriptor 必须通过 `ProviderModuleDescriptorFactory.Create` 或等价逻辑声明 endpoint、protocol、capability、model catalog、permission、side effect 与 metadata。后续新增 provider 不得绕过 descriptor 直接进入 Execution Runtime。

内置 provider 同时必须通过 `ProviderModuleDescriptorFactory.CreateModuleDescriptor` 暴露 Module Plane 通用 `ModuleDescriptor`，用于 module discovery、trust level、capability set、health / smoke check 和 architecture dependency guard。

## 5. 调用链路

```text
Control Plane normalizes host operation
  -> Kernel selects ModelRoutePolicy and validates StageGraph
  -> Kernel materializes approved ModelInvocationStep
  -> Execution Runtime validates RuntimeStep boundary
  -> ExecutionRuntimeProviderBridge builds ProviderInvocationRequest
  -> Provider Module streams typed ProviderStreamEvent
  -> Execution Runtime emits RuntimeStepResult, diagnostics ref and trace ref
```

## 6. 边界约束

- Provider Module 不选择 StageGraph。
- Provider Module 不选择 ModelRoutePolicy 或 route strategy。
- Provider Module 不选择 provider/model fallback candidate；模型路由只能由 Kernel `ModelRoutePolicy` 与 Execution Runtime model route bridge 物化。
- Provider Module 不重新裁切上下文；上下文预算、优先级、降权、引用化和 dropped reason 只能由 Kernel `ContextPolicy` 与 Execution Runtime context policy bridge 决定。
- Provider Module 不创建或修改 `GovernanceEnvelope`。
- Provider Module 不直接调用 Kernel、Control Plane 或 Host Gateway。
- Provider Module 不直接读取 secret 明文；配置层只传递 secret reference。
- Provider Module 只能输出脱敏 diagnostics 和 typed stream event。
- Provider Module 对外部网络的副作用必须由 descriptor 与 RuntimeStep 共同声明，并可审计。

## 7. 验收标准

- provider 调用必须能追踪到 `RuntimeStepId`、`SourceIntentId`、`SourceGraphId`、`SourceStageId`、`SourceKernelOperationId`。
- `ProviderInvocationRequest.InvocationContext` 缺失时，provider 实现不得自行补造 Kernel 来源信息。
- provider id 与 descriptor id 不一致时，Execution Runtime 必须 fail closed。
- provider failure 必须通过 `ProviderFailureEvent` 或等价 typed failure 返回，并由 Execution Runtime 转为 blocked result。
- 内置 provider descriptor 必须声明 protocol capability、permission、side effect、audit 与 implementation binding。
- provider 配置只输出 descriptor、endpoint、protocol capability、model catalog 和 secret reference，不参与 Kernel 编排决策。
- provider 输入必须是 provider-neutral `ProviderInputItem`，不得由 provider implementation 重新读取历史或私有状态自行裁切。
