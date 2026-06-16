# 天枢模块接入指南 · Module Integration Guide

> 本文面向**想为天枢扩展能力的第三方开发者**:如何接入自定义 Provider、Tool 和其他能力模块。
> 如果你只想运行天枢,请回到 [README](../../README.md) 的「快速开始」。

天枢的最大设计目标之一,是让第三方开发者**在不修改内核的前提下**接入自己的模型、工具与能力。所有能力都通过 **Module Plane(模块层)** 提供,模块只需实现稳定的类型化契约,由组合层装配进运行时——内核、控制层、执行运行时对模块的具体实现一无所知。

## 模块的统一原则

无论哪种模块,都遵守同一组边界:

- **只响应授权调用** — 模块不主动发起编排,只在被授权的 capability call 下执行。
- **不反向依赖上层** — 模块不能调用内核、控制层或宿主。
- **不绕过治理** — 权限、副作用、审计声明由 descriptor 表达,执行运行时强制校验。
- **原始 payload 止于模块边界** — 厂商 wire JSON、字符串命令只允许停留在模块内部,不向上层泄漏,跨层一律用类型化契约。

---

## 一、接入自定义 Provider(模型)

Provider 模块负责把一次模型调用,转成天枢的**流式事件序列**。这是接入「新模型 / 新厂商 / 自建网关」的入口。

### 1. 实现 `IProviderModule`

```csharp
public interface IProviderModule
{
    ProviderDescriptor Descriptor { get; }

    IAsyncEnumerable<ProviderStreamEvent> InvokeAsync(
        ProviderInvocationRequest request,
        CancellationToken cancellationToken);
}
```

- **`Descriptor`** — 声明 provider id、显示名、协议类型、能力(是否支持流式 / 工具)、支持的模型列表。
- **`InvokeAsync`** — 接收 provider-neutral 的 `ProviderInvocationRequest`(已由上层归一化的输入),产出 `ProviderStreamEvent` 序列。

### 2. 产出的关键事件

| 事件 | 含义 |
| --- | --- |
| `ProviderCompletionEvent` | 模型最终回复 + token usage,作为本轮终态 |
| `ProviderToolDirectiveEvent` | 模型请求调用工具(callId + toolId + 参数),驱动 turn loop 的 `model-reason → tool-exec` 回边 |

把厂商的 SSE / HTTP 响应在模块内部解析后,映射成上面这两类事件即可——**厂商协议差异完全封装在模块内**,内核只看到统一的事件流。

### 3. 接入要点

- 缺凭据时**直接 fail-closed**(返回明确 failure),不要静默降级。
- 凭据通过环境变量读取,**绝不**写进 descriptor 或配置文件。
- provider id 要与配置中的 route set / provider instance 对应。

> 内置实现可参考 `TianShu.Provider.*`:OpenAI Responses、Anthropic Messages、OpenAI-compatible Chat Completions 三种协议都是同一接口的不同实现。

---

## 二、接入自定义 Tool(工具)

Tool 模块为模型提供「可被请求调用的能力」(读文件、搜索、执行等)。模型在 `model-reason` 阶段请求工具,执行运行时校验治理边界后调用。

### 1. 实现 `ITianShuToolProvider` 与 `ITianShuTool`

```csharp
public interface ITianShuToolProvider
{
    IReadOnlyList<ToolDescriptor> DescribeTools(TianShuToolRegistrationContext context);
    ITianShuToolHandler CreateHandler(string toolKey, TianShuToolActivationContext context);
}

public interface ITianShuTool
{
    ToolDescriptor Descriptor { get; }
    ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationEnvelope invocation,
        ToolInvocationContext context,
        CancellationToken cancellationToken);
}
```

### 2. `ToolDescriptor` 表达治理边界

descriptor 不只是「工具叫什么」,它**声明工具的治理属性**,这些属性会被执行运行时强制校验:

- **输入 schema** — 模型据此构造调用参数。
- **审批要求(approvalRequirement)** — 是否需要人工 gate(只读工具通常 `None`,写工具通常需要审批)。
- **副作用等级** — `ReadOnly` / `WorkspaceWrite` / `HostMutation` 等,执行时不得超过当前治理信封允许的上限。
- **并发类别** — 是否可并行。

### 3. 接入要点

- 工具的副作用**必须**如实声明——执行运行时按 `Stage ≤ Graph ≤ Governance` 层层收窄校验,谎报副作用会被 fail-closed 拦截。
- 只读工具默认可开放;写 / 执行类工具应声明审批要求,并在实现内做路径沙箱、输出截断等防护。
- 工具要进入某次 turn,必须同时在该次治理的 allow-list 内——**默认不在 allow-list 的工具,模型请求时 fail-closed**。

> 内置工具模块可参考 `TianShu.Tools.*`:FileSystem(只读)、FileSystemMutating(审批写入)、Search、Code、Memory、Artifacts、MCP Resources、Collaboration 等。

---

## 三、其他能力模块

天枢的 Module Plane 还容纳 Provider / Tool 之外的能力,它们都通过各自的类型化契约接入,统一由 `ModuleDescriptor` 声明身份与治理:

| 模块族(`ModuleKind`) | 职责 |
| --- | --- |
| `Provider` | 模型调用 |
| `Tool` | 可被请求的工具能力 |
| `MemoryIdentity` | 记忆与身份 |
| `ArtifactStateProjection` | 工件与状态投影 |
| `Diagnostics` | 诊断与指标 |
| `WorkspaceEnvironment` | 工作区环境 |
| `Configuration` | 配置 |
| `SubAgentOrchestration` | 子代理编排(嵌套受治理 turn) |
| `Custom` | 第三方自定义能力 |

第三方能力应优先归入最贴近的 `ModuleKind`;确实无法归类的,用 `Custom`,并通过 `ModuleDescriptor` 完整声明信任等级、副作用与健康检查。

---

## 四、装配:模块如何进入运行时

模块不自我注册到内核,而是由**组合层(Composition Root)** 装配后注入。以子代理模块为例,运行时构建时通过绑定表注入:

```csharp
// 组合层把模块实现注入执行运行时的绑定注册表
// providers / tools / subAgentModules 都是「id → 实现」的映射
new ExecutionRuntimeStepBindingRegistry(
    providers: new Dictionary<string, IProviderModule> { ["provider.default"] = myProvider },
    tools:     new Dictionary<string, ITianShuTool>   { /* ... */ },
    subAgentModules: /* ... */);
```

这样设计的好处:**内核与执行运行时只依赖契约接口,不依赖任何具体模块项目**——你的模块可以是一个完全独立的程序集,装配时才接入,不需要、也不允许内核反向引用它。

---

## 设计文档

模块边界的完整定义见架构文档:

- [模块层设计](../architecture/tianshu-module-plane-design.md) — Module / Tool 描述、权限、副作用、审计契约
- [执行运行时设计](../architecture/tianshu-execution-runtime-design.md) — runtime step、provider/tool bridge
- [契约架构](../architecture/tianshu-contracts-architecture.md) — 跨层类型化契约边界
