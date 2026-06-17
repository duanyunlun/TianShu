# 天枢模块接入教程 · Module Integration Tutorial

> 本文面向想为 TianShu 扩展 Provider、Tool、Memory 能力的第三方开发者。
> 如果你只想安装和运行 CLI，请回到 [README](../../README.md) 的快速开始。

TianShu 的模块接入原则是：第三方实现只依赖公开合同项目，通过 Module Plane 暴露能力，再由组合层按治理规则装配进运行时。模块不得反向调用 Kernel、Control Plane 或 Host Gateway，也不得绕过治理信封、人工审批、配置绑定和健康检查。

当前公开模板位于：

| 模块类型 | 模板项目 | 测试项目 |
| --- | --- | --- |
| Provider | `templates/modules/provider/TianShu.Template.ProviderModule` | `templates/modules/provider/TianShu.Template.ProviderModule.Tests` |
| Tool | `templates/modules/tool/TianShu.Template.ToolModule` | `templates/modules/tool/TianShu.Template.ToolModule.Tests` |
| Memory | `templates/modules/memory/TianShu.Template.MemoryModule` | `templates/modules/memory/TianShu.Template.MemoryModule.Tests` |

当前公开示例位于：

| 模块类型 | 示例项目 | 测试项目 | 用途 |
| --- | --- | --- | --- |
| Provider | `samples/modules/provider/TianShu.Samples.Provider.Echo` | `samples/modules/provider/TianShu.Samples.Provider.Echo.Tests` | 演示第三方 provider descriptor、manifest、streaming output 和 usage projection。 |
| Tool | `samples/modules/tool/TianShu.Samples.Tool.WordCount` | `samples/modules/tool/TianShu.Samples.Tool.WordCount.Tests` | 演示只读工具 schema、治理信封、调用和结果投影。 |
| Memory | `samples/modules/memory/TianShu.Samples.Memory.InMemory` | `samples/modules/memory/TianShu.Samples.Memory.InMemory.Tests` | 演示无外部依赖的 retrieve、form、supersede 和降级边界。 |

## 准备工作

1. 安装仓库要求的 .NET SDK。
2. 从模板项目复制一份新模块项目，不要直接修改模板本身。
3. 为新模块保留独立测试项目，至少覆盖 descriptor、manifest、访问校验和最小 smoke 调用。
4. 运行模板验证脚本确认本地 SDK 与合同项目可用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-TianShuModuleTemplates.ps1 -Configuration Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-TianShuModuleSamples.ps1 -Configuration Release
```

也可以单独运行某个模板测试：

```powershell
dotnet test templates/modules/provider/TianShu.Template.ProviderModule.Tests/TianShu.Template.ProviderModule.Tests.csproj --configuration Release --nologo
```

## 统一接入规则

所有模块都要满足以下规则：

- 模块身份必须稳定：`ModuleId`、`ProviderId`、`ToolKey` 等公开 id 一旦发布就按兼容契约维护。
- 模块描述必须完整：descriptor 和 manifest 必须声明能力、权限、side effect、human gate、最低 TianShu 版本、健康检查和实现绑定。
- 缺配置必须 fail-closed：必需配置未绑定时，组合层拒绝装配并输出 `module_load.required_configuration_missing`，不得回退到假实现或默认 secret。
- 第三方模块必须显式允许：非内置模块需要通过加载策略 allow-list 后才能进入装配。
- 原始厂商 payload 只能停在模块内部：跨层输出必须是 TianShu 的类型化合同对象。
- 运行时只消费已注册记录：discovery candidate 只有通过 loading/composition 后，才会成为 live binding。

## 从零写 Provider

Provider 模块负责把模型厂商或自建网关的响应转换成 TianShu 的统一流式事件。

### 所属项目

Provider 模块至少涉及：

- `TianShu.Provider.Abstractions`：`IProviderModule` 接口。
- `TianShu.Contracts.Provider`：provider descriptor、manifest、invocation request、stream event、usage。
- `TianShu.Contracts.Kernel`：运行时输入输出的核心合同类型。
- 你的第三方项目：例如 `TianShu.Modules.MyProvider`。

### 最小代码骨架

```csharp
using System.Runtime.CompilerServices;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Provider;
using TianShu.Provider.Abstractions;

namespace TianShu.Modules.MyProvider;

public sealed class MyProviderModule : IProviderModule
{
    public const string ProviderId = "my.provider";
    public const string WireApi = "my_provider_protocol";

    public ProviderDescriptor Descriptor { get; } = CreateDescriptor();

    public static ProviderModuleManifest CreateManifest()
        => ProviderModuleDescriptorFactory.CreateAccessManifest(
            CreateDescriptor(),
            WireApi,
            routeSetId: "default",
            errorSpecs:
            [
                new ProviderErrorSpec(
                    "my_provider_unavailable",
                    ProviderErrorKind.ProviderUnavailable,
                    retryable: true,
                    safeForUser: true,
                    remediation: "Check provider endpoint and credential.")
            ]);

    public async IAsyncEnumerable<ProviderStreamEvent> InvokeAsync(
        ProviderInvocationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var text = "Replace this with real provider output.";

        await Task.Yield();
        yield return new ProviderTextDeltaEvent(text);
        yield return new ProviderCompletionEvent(new ProviderCompletion(
            text,
            new ProviderUsage(InputTokens: 1, OutputTokens: 1)));
    }

    private static ProviderDescriptor CreateDescriptor()
        => ProviderModuleDescriptorFactory.Create(
            ProviderId,
            "My Provider",
            ProviderProtocolKind.Custom,
            "https://provider.example.invalid/v1",
            credentialEnvironmentVariable: "MY_PROVIDER_API_KEY",
            capabilities: new ProviderCapabilityProfile(SupportsStreaming: true),
            models:
            [
                new ProviderModelDescriptor("my-model", "My Model", "my-provider")
            ],
            wireApi: WireApi);
}
```

### Provider 必须验证什么

- `ProviderDescriptor.ProviderId` 与 `ProviderModuleManifest` 的 provider id 一致，否则校验失败 `provider_access.provider_id_mismatch`。
- manifest 必须有启用的协议绑定，否则失败 `provider_access.protocol_binding_missing`。
- route set 必须存在并包含启用候选，否则失败 `provider_access.route_set_missing` 或 `provider_access.route_set_no_enabled_candidate`。
- endpoint、protocol、wire api 必须与 descriptor/manifest 匹配，否则失败 `provider_access.endpoint_mismatch`。
- completion 必须输出 usage。真实 provider 应优先投影厂商 usage；厂商缺失时可使用估算 usage，但必须保持可观测，不要伪装成真实计量。

## 注册 Tool

Tool 模块把可被模型请求的能力注册给运行时。所有 Tool 调用都必须先经过 ToolDescriptor、GovernanceEnvelope、side effect 和 human gate 校验。

### 所属项目

Tool 模块至少涉及：

- `TianShu.Contracts.Tools`：`ITianShuToolProvider`、`ITianShuToolHandler`、tool descriptor、tool invocation/result。
- `TianShu.Contracts.Primitives`：permission、side effect、audit 等通用治理类型。
- `TianShu.Contracts.Kernel`：`GovernanceEnvelope`。
- 你的第三方项目：例如 `TianShu.Modules.MyTool`。

### 最小代码骨架

```csharp
using System.Text.Json;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.Modules.MyTool;

public sealed class MyToolProvider : ITianShuToolProvider
{
    public const string ModuleId = "my.tool";
    public const string ToolKey = "my.echo";

    public IReadOnlyList<ToolDescriptor> DescribeTools(TianShuToolRegistrationContext context)
        => [MyEchoToolHandler.DescriptorValue];

    public ITianShuToolHandler CreateHandler(string toolKey, TianShuToolActivationContext context)
        => string.Equals(toolKey, ToolKey, StringComparison.Ordinal)
            ? new MyEchoToolHandler()
            : throw new InvalidOperationException($"Unknown tool: {toolKey}");

    public static ToolModuleManifest CreateManifest()
        => new(
            ModuleId,
            "My Tool Module",
            "1.0.0",
            "0.6.0",
            tools:
            [
                new ToolModuleToolBinding(
                    ToolKey,
                    "My Echo",
                    "Echoes input.",
                    inputSchema: MySchemas.EchoInput,
                    outputSchema: MySchemas.EchoOutput,
                    permission: new PermissionDeclaration(["tool.my.echo"], requiresHumanGate: false),
                    sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly, ["runtime"], reversible: true),
                    requiresHumanGate: false)
            ]);

    public static GovernanceEnvelope CreateGovernance()
        => new(
            "my-tool-governance",
            allowedToolIds: [ToolKey],
            allowedModuleIds: [ModuleId],
            maxSideEffectLevel: SideEffectLevel.ReadOnly,
            requiresHumanGate: false);
}

public sealed class MyEchoToolHandler : ITianShuToolHandler
{
    public static ToolDescriptor DescriptorValue { get; } = new(
        MyToolProvider.ToolKey,
        "My Echo",
        "Echoes input.",
        inputSchema: MySchemas.EchoInput,
        outputSchema: MySchemas.EchoOutput,
        permissions: new PermissionDeclaration(["tool.my.echo"], requiresHumanGate: false),
        sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly, ["runtime"], reversible: true),
        audit: new AuditProfile(eventKinds: ["tool.my.echo.invoked"]));

    public ToolDescriptor Descriptor => DescriptorValue;

    public ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ToolInvocationResult(
            request.CallId,
            request.ToolKey,
            streamItems:
            [
                new ToolStreamItem("text", request.Input, isTerminal: true)
            ]));
    }
}

internal static class MySchemas
{
    public static JsonElement EchoInput { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "text" },
        properties = new { text = new { type = "string" } }
    });

    public static JsonElement EchoOutput { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { text = new { type = "string" } }
    });
}
```

### Tool 必须验证什么

- descriptor 不能为空，否则失败 `tool_access.descriptor_missing`。
- manifest binding 必须能找到匹配 descriptor，否则失败 `tool_access.descriptor_not_found`。
- input schema 必须存在，否则失败 `tool_access.schema_missing`。
- manifest 不能弱化 descriptor 的 side effect 或 human gate，否则失败 `tool_access.side_effect_weakened`、`tool_access.human_gate_weakened`。
- GovernanceEnvelope 必须允许 module/tool，并且 side effect 不得超过上限，否则失败 `tool_access.module_not_allowed` 或 `tool_access.governance_denied`。

写文件、执行命令、网络调用、MCP tool 等高风险工具应声明更高 side effect，并默认要求 human gate。不要把写操作包装成 `ReadOnly`。

## 接入 Memory

Memory 模块负责把记忆供应商接入 TianShu 的上下文策略。当前公开边界包含 retrieve、form、supersede 和 compression-reserved。压缩能力在当前阶段只作为预留边界，不能作为可执行 mutation 暴露。

### 所属项目

Memory 模块至少涉及：

- `TianShu.Contracts.Memory`：`IMemoryModule`、provider、space、query、mutation、overlay。
- `TianShu.Contracts.Modules`：module descriptor、health check、implementation binding。
- `TianShu.Contracts.Kernel`：governance、approved context policy。
- `TianShu.Contracts.Primitives`：permission、side effect、trust、lifecycle。
- 你的第三方项目：例如 `TianShu.Modules.MyMemory`。

### 最小代码骨架

```csharp
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Modules.MyMemory;

public sealed class MyMemoryModule : IMemoryModule
{
    public const string ModuleId = "my.memory";
    public const string ProviderId = "my.memory.local";

    public ModuleDescriptor Descriptor { get; } = CreateDescriptor();

    public static MemoryModuleManifest CreateManifest()
        => new(
            ModuleId,
            "My Memory Module",
            "1.0.0",
            "0.6.0",
            providers: [CreateProvider()],
            capabilities:
            [
                Capability("my.memory.retrieve", MemoryModuleCapabilityKind.Retrieve, MemoryProviderCapability.Filter | MemoryProviderCapability.ReadOnlyAccess, SideEffectLevel.ReadOnly, false),
                Capability("my.memory.form", MemoryModuleCapabilityKind.Form, MemoryProviderCapability.Add | MemoryProviderCapability.Extract, SideEffectLevel.ExternalMutation, true),
                Capability("my.memory.supersede", MemoryModuleCapabilityKind.Supersede, MemoryProviderCapability.Supersede, SideEffectLevel.ExternalMutation, true),
                Capability("my.memory.compress", MemoryModuleCapabilityKind.CompressReserved, MemoryProviderCapability.None, SideEffectLevel.ReadOnly, true, executable: false)
            ],
            new MemoryContextPolicyBinding(
                ContextSourceKind.MemoryRecord,
                ContextProjectionMode.ReferenceOnly,
                requireEvidenceRefs: true,
                moduleMaySliceContext: false),
            compressionReservations:
            [
                new MemoryCompressionReservation("my.memory.compress.v1", "Reserved compression interface.")
            ]);

    public ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(new ModuleSmokeCheckResult(ModuleId, true, ModuleHealthStatus.Healthy));

    public ValueTask<MemoryModuleQueryResult> QueryAsync(
        MemoryModuleQueryInvocation invocation,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(new MemoryModuleQueryResult(degradedProviders: ["my.memory.not_implemented"]));

    public ValueTask<MemoryMutationResult> MutateAsync(
        MemoryModuleMutationInvocation invocation,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(new MemoryMutationResult(
            Succeeded: false,
            DegradedReason: "my.memory.not_implemented",
            Effect: MemoryMutationEffect.Degraded));

    private static MemoryProviderDescriptor CreateProvider()
        => new(
            ProviderId,
            "My Memory Provider",
            "1.0.0",
            MemoryProviderCapability.Filter | MemoryProviderCapability.ReadOnlyAccess,
            [MemoryScopeKind.User, MemoryScopeKind.Workspace],
            TrustLevel: MemoryProviderTrustLevel.Workspace,
            DegradationStrategy: MemoryProviderDegradationStrategy.UnsupportedResult);

    private static MemoryModuleCapabilityBinding Capability(
        string capabilityId,
        MemoryModuleCapabilityKind kind,
        MemoryProviderCapability requiredCapabilities,
        SideEffectLevel sideEffectLevel,
        bool requiresHumanGate,
        bool executable = true)
        => new(
            capabilityId,
            kind,
            ProviderId,
            requiredCapabilities,
            new PermissionEnvelope([$"memory.{kind.ToString().ToLowerInvariant()}"], requiresHumanGate),
            new SideEffectProfile(sideEffectLevel, ["memory"], reversible: sideEffectLevel <= SideEffectLevel.ReadOnly),
            requiresHumanGate,
            executable);

    private static ModuleDescriptor CreateDescriptor()
    {
        var manifest = CreateManifest();
        return new ModuleDescriptor(
            ModuleId,
            ModuleKind.MemoryIdentity,
            "My Memory Module",
            "1.0.0",
            capabilities: manifest.Capabilities.Select(static capability => new ModuleCapabilityDescriptor(
                capability.CapabilityId,
                capability.Kind.ToString(),
                permission: capability.Permission,
                sideEffects: capability.SideEffects)).ToArray(),
            permission: new PermissionEnvelope(["memory.my"], requiresHumanGate: true),
            sideEffects: new SideEffectProfile(SideEffectLevel.ExternalMutation, ["memory"], reversible: false),
            trustLevel: ModuleTrustLevel.UserInstalled,
            minimumTianShuVersion: "0.6.0",
            health: new ModuleHealthProbe(ModuleHealthStatus.Healthy),
            implementationBinding: new ModuleImplementationBinding("TianShu.Modules.MyMemory", typeof(MyMemoryModule).FullName));
    }
}
```

### Memory 必须验证什么

- module 必须被 GovernanceEnvelope 允许，否则失败 `memory_access.module_not_allowed`。
- context policy 必须允许 `MemoryRecord`，否则失败 `memory_access.context_policy_disallows_memory`。
- memory context projection 不能未声明，模块不能自行裁切上下文，否则失败 `memory_access.context_projection_unspecified` 或 `memory_access.context_slicing_owned_by_module`。
- provider/capability id 不能重复，否则失败 `memory_access.duplicate_provider` 或 `memory_access.duplicate_capability`。
- retrieve/form/supersede/compress-reserved 必须都有声明，否则失败 `memory_access.retrieve_missing`、`memory_access.form_missing`、`memory_access.supersede_missing`、`memory_access.compression_reserved_missing`。
- compression reservation 必须保持 reserved-only，不能在当前阶段变成 executable，否则失败 `memory_access.compression_reserved_executable`。

## 模块装配流程

模块进入运行时要经过四步：

1. Discovery：扫描内置模块和第三方模块 manifest，生成候选快照。
2. Selection：重复 id、禁用模块、损坏 manifest 会在候选层被标记或剔除。
3. Loading：组合根按 `ModuleLoadingPolicy` 校验 trust、allow-list、required configuration、health、version、implementation binding。
4. Binding：只有 `Registered` 的记录才会写入 provider/tool/memory 绑定表，供 Execution Runtime 调用。

典型装配策略包含：

```csharp
var policy = new ModuleLoadingPolicy(
    currentTianShuVersion: "0.6.0",
    explicitlyAllowedModuleIds: ["my.provider", "my.tool", "my.memory"],
    boundConfigurationKeys:
    [
        "providers.my_provider.api_key",
        "providers.my_provider.base_url"
    ]);
```

第三方模块常见装配结果：

| 状态 | 含义 |
| --- | --- |
| `Registered` | 已通过所有校验，可进入 live binding。 |
| `Rejected` | 配置、信任、版本、descriptor、manifest 等硬性条件不满足。 |
| `Unavailable` | 模块存在但健康检查不通过。 |
| `Skipped` | discovery 未选择，例如禁用或损坏候选。 |

## 故障排查

| 诊断码 | 常见原因 | 修复方向 |
| --- | --- | --- |
| `module_discovery.disabled` | manifest 或配置禁用了模块。 | 确认模块启用开关。 |
| `module_discovery.duplicate_rejected` | 多个模块声明同一 module id。 | 保留一个稳定 id，重命名冲突模块。 |
| `module_load.candidate_not_selected` | discovery 未选中该候选。 | 先看 discovery issue，不要直接排查 runtime binding。 |
| `module_load.descriptor_missing` | candidate 没有 ModuleDescriptor。 | 为模块实现补齐 descriptor。 |
| `module_load.descriptor_manifest_mismatch` | descriptor 与 manifest 的 id 或 kind 不一致。 | 统一 module id、kind。 |
| `module_load.trust_denied` | 模块 trust level 低于加载策略要求。 | 调整 trust 声明或加载策略。 |
| `module_load.third_party_not_allowed` | 第三方模块未进入 allow-list。 | 在加载策略中显式允许 module id。 |
| `module_load.required_configuration_missing` | 必需配置键未绑定。 | 补齐配置并加入 `BoundConfigurationKeys`。 |
| `module_load.health_not_healthy` | 健康检查未通过。 | 修复凭据、端点、依赖服务或模块自身检查逻辑。 |
| `module_load.version_incompatible` | 模块要求的最低 TianShu 版本高于当前版本。 | 升级 TianShu 或降低模块最低版本要求。 |
| `module_load.implementation_binding_missing` | descriptor 没有实现绑定。 | 补齐 assembly/type binding。 |
| `provider_access.protocol_binding_missing` | Provider manifest 缺少启用协议绑定。 | 补齐 protocol binding，并确保 wire api 匹配。 |
| `provider_access.route_set_no_enabled_candidate` | route set 内没有可用模型候选。 | 启用至少一个 model candidate。 |
| `tool_access.schema_missing` | Tool 没有 input schema。 | 为每个 tool binding 声明 JSON schema。 |
| `tool_access.governance_denied` | governance 未允许该 tool 或 side effect 超限。 | 收窄工具声明或扩大治理信封，优先收窄工具声明。 |
| `tool_access.human_gate_weakened` | manifest 关闭了 descriptor 要求的审批。 | 让 manifest 与 descriptor 的 human gate 保持一致或更严格。 |
| `memory_access.context_policy_disallows_memory` | 已批准 context policy 不允许 MemoryRecord。 | 在 context policy 中显式允许 MemoryRecord。 |
| `memory_access.provider_capability_missing` | provider 不具备 capability 要求的能力。 | 调整 provider capability 或 capability binding。 |
| `memory_access.compression_reserved_executable` | 压缩预留能力被错误设为可执行。 | 保持 `CompressReserved` 为 reserved-only。 |

## 发布前检查清单

公开发布第三方模块前，至少完成：

- 模板测试和模块自身测试通过。
- descriptor、manifest、implementation binding、health check 都有测试覆盖。
- Provider usage 投影有真实/估算/缺失的可观测区分。
- Tool 的 side effect 与 human gate 没有弱化。
- Memory 的 context policy 和 compression reservation 没有绕过运行时。
- 文档、示例配置、测试 fixture 不包含 API key、个人路径或私有端点。

## 设计文档

模块边界的完整定义见：

- [模块层设计](../architecture/tianshu-module-plane-design.md)
- [执行运行时设计](../architecture/tianshu-execution-runtime-design.md)
- [契约架构](../architecture/tianshu-contracts-architecture.md)
