# TianShu Module Plane 设计

## 1. 文档定位

Module Plane 承载可装配、可替换、可第三方实现的能力模块。Module 不是普通底层依赖；每个 Module 必须能声明能力、配置 schema、权限、副作用、审计、信任等级和健康状态。

本文件以 `docs/tianshu-architecture-spec.md` 为总架构基线，作为 Module Plane 当前代码落地与后续验收基线。

Module SDK 的 v1.0 稳定承诺以 `docs/architecture/tianshu-core-api-stability-design.md` 为准。第三方模块只能依赖公开 contracts / abstractions、manifest、descriptor、health、family invocation 和 usage / metrics projection；RuntimeComposition、Execution Runtime bridge、Host Gateway、Control Plane、Kernel 实现和产品宿主私有 DTO 不属于 SDK 稳定面。

## 2. 涉及项目

| 模块家族 | 当前涉及项目 | 当前接入方式 |
| --- | --- | --- |
| 通用 Module 契约 | `src/Contracts/TianShu.Contracts.Modules` | 定义 `ModuleDescriptor`、`ModuleCapabilityDescriptor`、`ModuleHealthProbe`、`ModuleSmokeCheckResult`、`IModuleHealthCheck`。 |
| Provider Module | `src/Provider/TianShu.Provider.Abstractions`、`TianShu.Provider.OpenAI`、`TianShu.Provider.OpenAICompatible`、`TianShu.Provider.Anthropic`、`TianShu.Provider.Google` | `ProviderModuleDescriptorFactory.CreateModuleDescriptor` 将 `ProviderDescriptor` 投影为 `ModuleDescriptor`。 |
| Tool Module | `src/Contracts/TianShu.Contracts.Tools`、`src/Tools/TianShu.Tools.*` | `ToolDescriptor.ToModuleDescriptor` 将工具能力投影为 `ModuleDescriptor`。 |
| Memory / Identity Module | `src/Core/TianShu.IdentityMemory`、`src/Contracts/TianShu.Contracts.Memory`、`src/Contracts/TianShu.Contracts.Identity` | `IMemoryModule` 定义统一入口，`MemoryServiceModuleAdapter` 包裹当前 `IMemoryService`，并使用 `BuiltInModuleDescriptors.MemoryIdentity`。 |
| Artifact / State / Projection Module | `src/Contracts/TianShu.Contracts.Projections`、`src/Core/TianShu.ArtifactStore`、`src/Core/TianShu.ProjectionStores`、`src/Hosting/TianShu.AppHost.State` | `IArtifactStateProjectionModule` 定义统一入口，`ArtifactStateProjectionModuleAdapter` 包裹当前 `IArtifactStore` 与 projection stores，并使用 `BuiltInModuleDescriptors.ArtifactStateProjection`。 |
| Diagnostics Module | `src/Core/TianShu.Diagnostics`、`src/Contracts/TianShu.Contracts.Diagnostics`、`src/Execution/TianShu.Execution.Runtime` | `IDiagnosticsModule` 定义统一入口，`DiagnosticsModuleAdapter` 包裹当前诊断 sink / redactor，`ExecutionRuntimeDiagnosticsModuleBridge` 通过 `DiagnosticStep` 调用，并使用 `BuiltInModuleDescriptors.Diagnostics`。 |
| Workspace / Environment Module | `src/Contracts/TianShu.Contracts.Environment`、`src/Core/TianShu.RuntimeComposition`、`src/Execution/TianShu.Execution.Runtime` | `IWorkspaceModule` 定义统一入口，`BuiltInWorkspaceEnvironmentModule` 输出只读 workspace facts，`ExecutionRuntimeWorkspaceModuleBridge` 通过 `ModuleCapabilityStep` 调用，并使用 `BuiltInModuleDescriptors.WorkspaceEnvironment`。 |
| Configuration Module | `src/Core/TianShu.Configuration`、`src/Hosting/TianShu.AppHost.Configuration` | `BuiltInModuleDescriptors.Configuration` 提供当前目标 descriptor。 |
| Remote Continuity Module | `src/Contracts/TianShu.Contracts.Remote`、`src/Modules/TianShu.Remote.Local` 或等价可替换实现 | v0.8 以 `docs/architecture/tianshu-remote-continuity-design.md` 为设计基线；当前本地示例默认不开放监听，显式配对后才允许提供 transport、event stream 和 remote command ingress。 |

## 3. Module SDK 正式边界

Module SDK 不是单个运行时项目，而是一组公开 contracts、family abstractions、descriptor 投影和装配边界。第三方模块只能依赖公开 contracts / abstractions，不得依赖 `TianShu.RuntimeComposition`、`TianShu.Execution.Runtime`、Host Gateway、Control Plane 或 Kernel 实现。

| SDK 家族 | 公开合同项目 | 可选抽象 / helper 项目 | 实现项目归属 | 运行时装配边界 |
| --- | --- | --- | --- | --- |
| 通用 Module | `src/Contracts/TianShu.Contracts.Modules`、`src/Contracts/TianShu.Contracts.Primitives`、`src/Contracts/TianShu.Contracts.Governance`、`src/Contracts/TianShu.Contracts.Kernel` 中的治理值对象 | 无独立运行时 helper；descriptor factory 可由各 family 提供 | 内置和第三方模块均必须投影 `ModuleDescriptor` | Module discovery 只读取 descriptor、manifest、health；Execution Runtime 只通过已批准 `RuntimeStep` 调用 module capability。 |
| Provider | `src/Contracts/TianShu.Contracts.Provider`、`src/Contracts/TianShu.Contracts.Modules` | `src/Provider/TianShu.Provider.Abstractions` 暴露 `IProviderModule`、bootstrap、wire-api component binding helper | `src/Provider/TianShu.Provider.*`；第三方实现建议位于独立包或未来 `src/Modules/Provider.*` 示例 | `ExecutionRuntimeProviderBridge` 只从 `ModelInvocationStep` 调用 `IProviderModule`；provider module 只处理协议、transport、stream event、usage 和脱敏 diagnostics。 |
| Tool | `src/Contracts/TianShu.Contracts.Tools`、`src/Contracts/TianShu.Contracts.Modules` | 当前 tool provider interface 位于 contracts；后续模板可提供 registration helper | `src/Tools/TianShu.Tools.*`；第三方实现建议独立包或未来 `src/Modules/Tools.*` 示例 | `ExecutionRuntimeToolBridge` 只从 `ToolInvocationStep` 调用 `ITianShuToolHandler`；tool descriptor 的 allow-list、副作用和 human gate 必须二次校验。 |
| Memory / Identity | `src/Contracts/TianShu.Contracts.Memory`、`src/Contracts/TianShu.Contracts.Identity`、`src/Contracts/TianShu.Contracts.Modules` | 当前 `IMemoryModule` 位于 memory contracts | `src/Core/TianShu.IdentityMemory`；第三方 memory module 后续以独立包接入 | `ExecutionRuntimeMemoryModuleBridge` 只从 `ModuleCapabilityStep` 调用 memory capability；ContextPolicy 决定何时检索、形成、取代或压缩，memory module 不反向改变 StageGraph。 |
| Diagnostics | `src/Contracts/TianShu.Contracts.Diagnostics`、`src/Contracts/TianShu.Contracts.Modules` | 当前 `IDiagnosticsModule` 位于 diagnostics contracts | `src/Core/TianShu.Diagnostics`；第三方 sink / redactor 后续以 module 形式接入 | `ExecutionRuntimeDiagnosticsModuleBridge` 只从 `DiagnosticStep` 或受控 runtime event 调用 diagnostics module；写入前必须脱敏，不暴露 secret、raw provider request 或本机私有路径。 |
| Projection / Artifact / State | `src/Contracts/TianShu.Contracts.Projections`、`src/Contracts/TianShu.Contracts.Artifacts`、`src/Contracts/TianShu.Contracts.Modules` | 当前 projection / artifact module contracts 位于对应 contracts | `src/Core/TianShu.ArtifactStore`、`src/Core/TianShu.ProjectionStores`、`src/Hosting/TianShu.AppHost.State` | Runtime 只通过 `ArtifactStep`、`StateCommitStep` 或受控 projection materializer 写入；Host Gateway 只能消费 typed projection，不暴露 RuntimeStep、StageGraph 或 raw module payload。 |
| Remote Continuity | `src/Contracts/TianShu.Contracts.Remote`，并复用 `Host`、`Projections`、`Governance`、`Modules` contracts | remote transport abstraction / pairing helper 已归属 contracts | `src/Modules/TianShu.Remote.Local` 或独立包；不得作为默认开启的内置模块启用 | Remote Module 只提供 transport、pairing、event delivery 和 command ingress adapter；任何 remote command 都必须进入 Host Gateway / Control Plane，不允许直接写 runtime state 或 workspace。 |

### 3.1 SDK 依赖方向

```text
Third-party Module
  -> TianShu.Contracts.*
  -> optional family abstractions
  -/-> TianShu.Execution.Runtime
  -/-> TianShu.RuntimeComposition
  -/-> TianShu.HostGateway / ControlPlane / Kernel implementation
```

Module SDK 的公开稳定面只包含：

- descriptor / manifest / health / smoke check contracts；
- family-specific invocation contracts，例如 `ProviderInvocationRequest`、`ToolInvocationRequest`、memory query / command、diagnostics event、projection event；
- permission、side effect、audit、trust level、configuration schema 引用；
- typed result、failure、diagnostics ref 和 metrics / usage 投影。

下列内容不是 Module SDK 公开稳定面：

- `StageGraph` 解释器实现；
- `RuntimeComposition` 的组合根和默认绑定；
- `ExecutionRuntime*Bridge` 的内部调度细节；
- provider wire payload 的完整 raw JSON；
- AppHost、CLI、VSIX 或 ConfigGUI 的私有 DTO；
- 本地安装路径、用户配置明文、API key 或 token。

### 3.2 公开包结构

公开 Module SDK 包按“基础 contracts、family contracts、可选 abstractions / helper、模板、示例、测试夹具”分层。包名、程序集名和项目名必须默认一致；第三方模块不得引用 implementation package 来获得 SDK 类型。

| 包层级 | 项目 / 包名 | 当前状态 | 公开内容 | 不允许包含 |
| --- | --- | --- | --- | --- |
| 基础值对象 | `src/Contracts/TianShu.Contracts.Primitives` | 已存在 | identifier、metadata、structured value、problem details 等跨合同基础类型。 | runtime service、host DTO、provider wire payload。 |
| 核心治理值对象 | `src/Contracts/TianShu.Contracts.Kernel`、`src/Contracts/TianShu.Contracts.Governance` | 已存在 | `GovernanceEnvelope`、permission、side effect、budget / approval 引用等跨 SDK 判断输入。 | Kernel 编排器实现、StageGraph interpreter 实现。 |
| 通用模块 SDK | `src/Contracts/TianShu.Contracts.Modules` | 已存在 | `ModuleDescriptor`、`ModuleCapabilityDescriptor`、`ModuleHealthProbe`、`ModuleSmokeCheckResult`、`IModuleHealthCheck`。 | discovery 实现、assembly loader、Execution Runtime bridge。 |
| Provider SDK | `src/Contracts/TianShu.Contracts.Provider` + `src/Provider/TianShu.Provider.Abstractions` | 已存在 | provider descriptor、provider-neutral invocation / stream contracts、`IProviderModule`、wire-api component bootstrap helper。 | 内置 provider endpoint 默认配置、API key 明文、HTTP client 单例实现。 |
| Tool SDK | `src/Contracts/TianShu.Contracts.Tools` | 已存在 | tool descriptor、schema、permission、handler/provider interface、tool result / failure。 | Shell / filesystem / MCP 等具体工具实现。 |
| Memory SDK | `src/Contracts/TianShu.Contracts.Memory` + `src/Contracts/TianShu.Contracts.Identity` | 已存在 | memory query、formation、identity、`IMemoryModule` 和结果契约。 | 本地 memory store、embedding provider、长期策略实现。 |
| Diagnostics SDK | `src/Contracts/TianShu.Contracts.Diagnostics` | 已存在 | diagnostics event、redaction contract、diagnostics module surface 和 refs。 | 文件 sink、具体 redactor 实现、raw secret payload。 |
| Projection / Artifact SDK | `src/Contracts/TianShu.Contracts.Projections` + `src/Contracts/TianShu.Contracts.Artifacts` | 已存在 | projection event、artifact ref、state projection、artifact module surface。 | projection store 实现、artifact 文件系统实现。 |
| Remote SDK | `src/Contracts/TianShu.Contracts.Remote` | P28.2 已落地 thread snapshot；P28.3 已落地 event cursor / subscription / envelope；P28.4 已落地 remote command envelope / payload / scope / idempotency / audit；P28.5 已落地 command ingress；P28.6 已落地 transport / pairing / short-lived token / revocation / activation 合同。 | remote snapshot、event cursor、remote command、command ingress、pairing grant、session token descriptor、revocation record、transport descriptor、remote module activation。 | 默认公网 listener、raw token/secret、云中继实现、移动端 UI、内核内置移动端状态机。 |

公共 abstractions 只在“contracts 无法表达生命周期 helper、component bootstrap 或 family-specific activation”时保留独立项目。当前正式归属如下：

| Abstraction | 归属 | 规则 |
| --- | --- | --- |
| `IProviderModule`、provider bootstrap / component resolver helper | `src/Provider/TianShu.Provider.Abstractions` | 作为 Provider SDK 的可选 helper；不得引用具体 provider 实现。 |
| `ITianShuToolProvider`、`ITianShuToolHandler` | `src/Contracts/TianShu.Contracts.Tools` | Tool SDK 的公共入口，不另建 `Tools.Abstractions`，除非未来出现非合同生命周期 helper。 |
| `IMemoryModule` | `src/Contracts/TianShu.Contracts.Memory` | Memory SDK 的公共入口；memory store / policy 不进入 SDK。 |
| `IDiagnosticsModule` | `src/Contracts/TianShu.Contracts.Diagnostics` | Diagnostics SDK 的公共入口；sink / redactor 实现不进入 SDK。 |
| `IArtifactStateProjectionModule` | `src/Contracts/TianShu.Contracts.Projections` / `src/Contracts/TianShu.Contracts.Artifacts` | Projection / Artifact SDK 的公共入口；store / materializer 不进入 SDK。 |
| Remote transport abstraction | `src/Contracts/TianShu.Contracts.Remote` | 只作为 Remote Continuity Module 的可替换 transport 合同；默认不进入 product surface，必须显式启用、显式配对、短期 token admission 和 scope 校验。 |

### 3.3 模板、示例和测试夹具归属

模板、示例和测试夹具不属于 runtime product surface，但必须作为 v0.6 SDK 验收资产。目标归属如下：

| 资产 | 目标路径 / 项目 | 验收用途 |
| --- | --- | --- |
| Provider 模板 | `templates/modules/provider/TianShu.Template.ProviderModule` + `templates/modules/provider/TianShu.Template.ProviderModule.Tests` | 提供最小 `IProviderModule`、provider descriptor、manifest、streaming stub、usage projection 和 contract test。 |
| Tool 模板 | `templates/modules/tool/TianShu.Template.ToolModule` + `templates/modules/tool/TianShu.Template.ToolModule.Tests` | 提供最小 `ITianShuToolProvider`、tool descriptor、schema、governance envelope、handler invocation 和 projection test。 |
| Memory 模板 | `templates/modules/memory/TianShu.Template.MemoryModule` + `templates/modules/memory/TianShu.Template.MemoryModule.Tests` | 提供最小 `IMemoryModule`、health check、retrieve / form / supersede / compression-reserved manifest、context policy 对接测试。 |
| Provider 示例 | `samples/modules/provider/TianShu.Samples.Provider.Echo` + `samples/modules/provider/TianShu.Samples.Provider.Echo.Tests` | 演示第三方 provider manifest、descriptor、streaming output、usage projection 和失败规范。 |
| Tool 示例 | `samples/modules/tool/TianShu.Samples.Tool.WordCount` + `samples/modules/tool/TianShu.Samples.Tool.WordCount.Tests` | 演示只读工具、schema、side effect、human gate 和 tool result projection。 |
| Memory 示例 | `samples/modules/memory/TianShu.Samples.Memory.InMemory` + `samples/modules/memory/TianShu.Samples.Memory.InMemory.Tests` | 演示无外部依赖 memory retrieve / formation / supersede 和降级行为。 |
| SDK 测试夹具 | 当前集中在 `tests/TianShu.Contracts.Modules.Tests/ModuleIntegrationMatrixTests.cs`，后续可抽出 `tests/TianShu.ModuleSdk.Fixtures` | 提供内置模块、第三方 fake 模块、损坏 manifest、重复 id、缺配置、禁用、健康失败 fixtures。 |
| SDK 集成测试 | 当前集中在 `tests/TianShu.Contracts.Modules.Tests/ModuleIntegrationMatrixTests.cs`，后续可扩展为 `tests/TianShu.ModuleSdk.Tests` | 验证 discovery、admission、configuration binding、composition fail-closed 和 public package dependency guard；模板 build/test 由 `tools/Build-TianShuModuleTemplates.ps1` 覆盖。 |

模板和示例必须只引用公开 SDK 包；测试夹具可以引用测试 helper，但不得成为产品装配依赖。所有模板与示例生成物必须能在没有私有仓库路径、没有用户 API key、没有本机安装三件套的环境中 restore / build / test。模板独立验证入口为 `tools/Build-TianShuModuleTemplates.ps1`，示例独立验证入口为 `tools/Build-TianShuModuleSamples.ps1`。两者均串行执行测试并关闭 shared compilation，避免多个模板或示例测试同时写入相同 contracts `obj` 输出。v0.6 module release gate 必须同时运行模板和示例验证，除非显式传入跳过参数。

## 4. 第三方模块生命周期与治理

第三方模块的生命周期必须是显式状态机，不允许“发现即执行”。v0.6 的正式状态如下：

```text
discovered
  -> manifest_validated
  -> descriptor_projected
  -> config_bound
  -> health_probed
  -> admitted
  -> registered
  -> active
  -> degraded | disabled | unavailable | uninstalled
```

| 状态 | 进入条件 | 失败处理 |
| --- | --- | --- |
| `discovered` | 模块位于允许的发现根目录或内置模块清单中。 | 发现根目录越界、路径不可读或来源未知时忽略并记录 diagnostics。 |
| `manifest_validated` | manifest schema、module id、kind、version、capability、side effect、required config、runtime dependency 通过验证。 | manifest 损坏或缺关键字段时 fail closed；不得尝试加载实现程序集。 |
| `descriptor_projected` | family descriptor 成功投影为统一 `ModuleDescriptor`。 | descriptor 与 manifest 冲突时拒绝该模块。 |
| `config_bound` | 必需配置项、secret reference、endpoint、runtime dependency 引用均已绑定。 | 缺配置时装配计划必须 `Rejected` 并输出 `module_load.required_configuration_missing`；doctor 给出修复建议；不得回退到假实现或默认 secret。 |
| `health_probed` | `IModuleHealthCheck` 或 family 等价健康检查已运行。 | `Unknown` 不等于 healthy；健康检查失败进入 `degraded` 或 `unavailable`。 |
| `admitted` | trust level、版本兼容、治理策略、禁用列表和重复 id 策略全部允许。 | 任一策略拒绝时进入 `disabled` 或 `unavailable`，并输出结构化原因。 |
| `registered` | 装配层只注册 descriptor、factory 和受控 invocation binding。 | 注册失败不得影响无关模块；必须保留失败 diagnostics。 |
| `active` | 当前 turn 的 Execution Runtime 可通过已批准 `RuntimeStep` 调用。 | active 不代表常驻执行；每次调用仍需重新过 RuntimeStep / descriptor / governance 校验。 |
| `degraded` | 模块部分能力不可用，但声明了可降级能力。 | 只暴露 healthy capability 子集，并投影 downgrade reason。 |
| `disabled` | 用户、配置、policy 或安全门禁显式禁用。 | 不发现为可调用能力；保留配置但不加载实现。 |
| `unavailable` | 缺依赖、健康检查失败、运行时探测失败或模块当前不可达。缺配置和版本不兼容在装配计划中表现为 `Rejected`。 | 不可被 Kernel 路由为可执行能力；doctor 必须可见。 |
| `uninstalled` | 模块文件或包已移除，或卸载记录已生效。 | 下一轮 discovery 不得引用旧实现；历史 trace 只保留 module id/version/source snapshot。 |

### 4.1 信任边界

`ModuleTrustLevel` 是治理输入，不是安全证明。装配层必须按来源和用户配置计算 trust level：

| Trust level | 来源 | 默认执行策略 |
| --- | --- | --- |
| `BuiltIn` | 随 TianShu 发布并经过 CI / release gate 的模块。 | 可参与默认发现；仍需 descriptor、health、governance 校验。 |
| `WorkspaceTrusted` | 当前 workspace 明确允许的模块。 | 只在该 workspace 有效；高副作用能力仍需 human gate。 |
| `UserInstalled` | 用户级目录安装并被配置允许的模块。 | 默认不可提升副作用上限；必须有 health 和 version 记录。 |
| `ThirdParty` | 第三方包、第三方目录或未随发布包验证的模块。 | 默认只可发现，不可自动执行；需要显式 allow-list。 |
| `Untrusted` / `Unspecified` | 来源未知、签名/校验缺失、manifest 不完整或策略无法判断。 | fail closed，不加载实现，不进入 provider/tool surface。 |

模块不能通过自身 manifest 提升 trust level；trust level 只能由装配层依据安装来源、用户配置、签名/校验和组织策略产生。第三方模块也不能扩大 `GovernanceEnvelope`、修改 allowed tool/module 集合、降低 human gate 或降低 side effect level。

### 4.2 版本兼容

每个可发现模块必须至少声明以下版本信息：

- `moduleVersion`：模块自身版本。
- `sdkContractVersion`：依赖的 Module SDK 合同版本。
- `minimumTianShuVersion`：最低 TianShu 版本。
- `capabilitySchemaVersion`：能力输入/输出 schema 版本。
- `runtimeDependencies`：运行时依赖名称、版本范围和是否必需。

兼容规则：

- `minimumTianShuVersion` 高于当前 TianShu 时，模块进入 `unavailable`。
- `sdkContractVersion` 主版本不兼容时，模块进入 `unavailable`；次版本缺能力时可进入 `degraded`。
- `capabilitySchemaVersion` 与 Runtime bridge 不兼容时，只禁用对应 capability，不应禁用同模块其他兼容 capability。
- 内置模块可以随 TianShu 版本迁移；第三方模块必须通过兼容测试或 manifest 声明才能进入 active。
- 历史 trace、turn log 和 replay 只依赖 module id/version/capability id，不依赖当前已安装实现。

### 4.3 健康检查

健康检查用于判断模块是否可被路由，不用于自动修复环境。标准输出只能是 `ModuleSmokeCheckResult`、`ModuleHealthProbe` 和 diagnostics refs。

健康检查至少分三类：

- descriptor health：manifest、descriptor、capability、schema、permission、side effect 是否自洽。
- dependency health：必需配置、secret reference、外部命令、运行时库、网络 endpoint 是否可用。
- invocation smoke：使用无副作用或最小副作用输入验证 family invocation path 是否可达。

`Unknown` 不能被当成 healthy。Provider 的网络探测失败不得回退到 fake provider；Tool 的依赖缺失不得暴露为可调用工具；Diagnostics sink 不可用时可以降级到最小内存诊断，但必须投影 downgrade reason；Projection / Artifact store 不可用时不得伪造 state commit 成功。

### 4.4 卸载、禁用与运行中快照

禁用和卸载必须可审计：

- 禁用记录以 `moduleId + source + reason + timestamp` 标识，可以来自用户配置、workspace policy、doctor 修复建议或安全策略。
- 禁用不删除用户配置、secret reference 或模块数据；卸载也不得默认删除模块产生的历史 trace、artifact 或 memory。
- 当前 turn 已解析的 module snapshot 在该 turn 内保持稳定；禁用或卸载在下一轮 discovery / admission 生效。
- 若运行中调用发现模块已不可用，Execution Runtime 必须将该 RuntimeStep 标记为 blocked / failed，并输出 failure code，不得静默跳过。
- 重新安装同 id 模块必须重新经过 manifest validation、health probe、version compatibility 和 governance admission。

### 4.5 失败降级

降级必须显式、可诊断、可回放，不能伪造成功：

| 模块家族 | 可接受降级 | 禁止降级 |
| --- | --- | --- |
| Provider | 当前 provider 不可用时返回 typed failure，由 Kernel / route policy 决定是否选择其他已授权 provider。 | Runtime 或 provider module 自行切到 fake provider、旧 provider、未授权模型或未配置 endpoint。 |
| Tool | 禁用单个 capability，返回 blocked / failed tool result。 | 将高副作用工具降级为低副作用声明继续执行。 |
| Memory | 只读检索不可用时可退化为 no-memory context，并记录 reason。 | 静默丢失记忆写入、伪造检索命中或绕过 ContextPolicy。 |
| Diagnostics | 写 sink 不可用时可用最小内存 sink 或 host-visible warning。 | 丢弃失败而不投影 diagnostics unavailable。 |
| Projection / Artifact / State | 只读 projection 不可用时返回 unavailable；artifact 写失败时 step failed。 | 伪造 state commit、artifact id 或 replay checkpoint。 |
| Remote | 默认 disabled；启用后 transport 不可用时只关闭远程入口。 | 自动开放公网监听、绕过 pairing 或直接写 runtime state。 |

### 4.6 治理原则

- Module invocation 的唯一执行入口是 Execution Runtime 执行 Kernel 已批准的 `RuntimeStep`。
- Provider 必须经 `ModelInvocationStep`，Tool 必须经 `ToolInvocationStep`，Memory / Remote / SubAgent 等非工具能力必须经 `ModuleCapabilityStep` 或其专项 RuntimeStep。
- 每次调用必须同时校验 RuntimeStep、ModuleDescriptor / ToolDescriptor / ProviderDescriptor、`GovernanceEnvelope`、side effect level、human gate 和 budget。
- Module 只能返回 typed result、event、failure、diagnostics ref、artifact ref 或 projection event；不得直接修改 Kernel state。
- 第三方模块的 raw payload、私有配置和 secret 只能停留在模块实现边界内，公共 diagnostics 必须脱敏。
- 任何缺 descriptor、缺 health、缺配置、版本不兼容、trust 不足或治理不允许的模块都必须 fail closed。

### 4.7 发现契约

Module discovery 的正式合同归属 `src/Contracts/TianShu.Contracts.Modules`，本地 `module.toml` 扫描实现归属 `src/Core/TianShu.Configuration`。发现阶段只允许读取目录和 manifest，产出 `ModuleDiscoverySnapshot`；不得加载程序集、实例化模块、执行 health probe 或注册 capability。

发现根分为：

| 来源 | source kind | 默认 trust | 说明 |
| --- | --- | --- | --- |
| 内置模块 | `BuiltIn` | `BuiltIn` | 随 TianShu 发布，来自 `BuiltInModuleDescriptors` 或发布包内置 manifest。 |
| workspace 明确允许目录 | `Workspace` | `WorkspaceTrusted` | 只对当前 workspace 有效，必须由配置或策略显式启用。 |
| 用户级模块目录 | `UserHome` | `UserInstalled` | 默认位于 TianShu home 的 `modules/` 下，允许发现但仍需 admission。 |
| 第三方目录 | `ThirdPartyDirectory` | `ThirdParty` | 可发现，不可仅凭 manifest 自动执行，必须显式 allow-list。 |
| Remote Continuity Module | `RemoteReserved` 或后续正式 `Remote` kind | `Unspecified` 或策略计算值 | 不得默认发现、默认加载或开放公网监听；必须显式配置、配对和 scope 限定。 |

通用 manifest 的最低必需字段为：

```toml
id = "provider.example"
kind = "Provider"
display_name = "Example Provider"
version = "1.0.0"
enabled = true
priority = 0
min_tianshu_version = "0.6.0"
sdk_contract_version = "0.6.0"
capabilities = ["provider:chat"]
diagnostics = ["module:discovery"]

[implementation]
project = "TianShu.Samples.Provider"
type = "TianShu.Samples.Provider.Module"
package_id = "provider.example"
```

发现结果必须按以下规则收敛：

- `enabled = false` 或外部 disabled list 命中的模块进入 `Disabled`，保留诊断但不进入后续加载。
- `kind = Unspecified`、缺少 `id`、缺少 `version` 或 TOML 损坏的 manifest 进入 `ManifestInvalid` 或 parse issue，后续不得加载实现。
- 重复 `moduleId` 时，内置模块优先于 workspace / 用户级 / 第三方模块；其后按 source kind、trust level、`priority` 升序、manifest path 稳定排序。未选中的候选进入 `DuplicateRejected`。
- `priority` 只在同一稳定排序链内生效，不能让第三方模块覆盖内置模块，也不能提升 trust level。
- `ModuleDiscoverySnapshot.SelectedCandidates` 只表示“发现阶段选中”，不等于 admitted、registered 或 active。

`TianShu.Configuration.TianShuModuleManifestDiscovery` 是 v0.6 的本地发现器：它扫描 `modules/**/module.toml` 并合并内置 descriptor 候选。Provider / Tool 现有家族私有 `provider.toml`、`tool.toml` 仍作为家族配置 manifest 存在；后续 P26.6 / P26.7 / P26.8 必须把它们投影到通用 discovery / descriptor 链路，而不是让私有 manifest 绕过 Module discovery。

### 4.8 加载与装配契约

Module loading / composition 的正式合同归属 `src/Contracts/TianShu.Contracts.Modules`。它消费 discovery 产出的 `ModuleDiscoverySnapshot`，输出 `ModuleAssemblyPlan`；产品运行时可以把该计划映射到自己的 DI container、`ExecutionRuntimeStepBindingRegistry` 或 family-specific bridge，但不得绕过计划中的 fail-closed 结论。

装配阶段的职责边界如下：

| 阶段 | 输入 | 输出 | 禁止事项 |
| --- | --- | --- | --- |
| admission gate | `ModuleDiscoveryCandidate`、`ModuleDescriptor`、`ModuleLoadingPolicy` | `ModuleLoadRecord` | 不得实例化缺 descriptor、未知 trust、未知 health、缺必需配置、禁用、版本不兼容或未授权第三方模块。 |
| isolation planning | selected candidate + source kind | `ModuleIsolationBoundary` | 不得让第三方模块进入内置 shared boundary；不得在未显式启用和配对时激活 remote boundary。 |
| service declaration | `ModuleImplementationBinding`、capability set | `ModuleServiceRegistration[]` | 不暴露具体 DI 容器、宿主私有类型或运行时内部 binding 类型给第三方模块。 |
| registration plan | validated load record | `ModuleAssemblyPlan.RegisteredRecords` | `Registered` 之前不得进入 Execution Runtime live binding；失败模块不得伪装为空实现。 |
| diagnostics | 每个拒绝、跳过、不可用原因 | `ModuleLoadDiagnostic[]` | 不得泄露 secret、raw provider request、用户私有路径或第三方模块私有 payload。 |

默认装配策略必须 fail closed：

- `ModuleDiscoveryCandidateStatus.Selected` 以外的候选只能 `Skipped`。
- 缺少 `ModuleDescriptor`、descriptor 与 manifest 的 `moduleId/kind` 不一致、缺少 `ModuleImplementationBinding` 时必须 `Rejected`。
- `ModuleTrustLevel.Unspecified`、`Untrusted` 必须 `Rejected`。
- `ThirdParty` 默认必须出现在 `ModuleLoadingPolicy.ExplicitlyAllowedModuleIds` 中；`priority` 或 manifest 自声明不能替代 allow-list。
- `ModuleDescriptor.RequiredConfiguration` 中 `Required=true` 的配置键必须出现在 `ModuleLoadingPolicy.BoundConfigurationKeys` 中；缺失时必须 `Rejected`，诊断码为 `module_load.required_configuration_missing`。
- `ModuleHealthStatus.Unknown`、`Disabled`、`Unavailable` 必须 `Unavailable`，不能被当成 healthy。
- `minimumTianShuVersion` 高于当前版本或版本字段非法时必须 `Rejected`。
- `RemoteReserved` / `Remote` 在未显式启用、未配对、缺 scope 或缺 health 时必须 `Rejected`，不得默认监听网络或接收远端命令。

隔离边界的默认映射为：

| source kind | isolation kind | 默认含义 |
| --- | --- | --- |
| `BuiltIn` | `BuiltInShared` | 随 TianShu 发布的内置模块可共享产品进程边界，但仍需 descriptor / health / governance 校验。 |
| `Workspace` / `UserHome` / `ThirdPartyDirectory` / `Package` | `DirectoryAssemblyLoadContext` | 第三方或用户安装模块默认按目录隔离，并要求可回收边界；具体 AssemblyLoadContext 由 runtime 实现。 |
| `RemoteReserved` / `Remote` | `RemoteBoundary` | 默认不激活；只有显式启用、配对、scope 和 health 全部通过后才可注册 transport。 |

`DefaultModuleCompositionRoot` 是 v0.6 的合同级默认 planner：它只做门禁、隔离边界投影和服务注册计划，不实际加载 DLL、不创建 DI container、不调用 provider/tool/memory 实例。P26.7 / P26.8 / P26.9 的 family 接入必须先通过该计划或等价更严格的装配门禁，再进入各自 live bridge。

### 4.9 Provider 模块公开接入路径

Provider Module 的公开接入合同归属 `src/Contracts/TianShu.Contracts.Provider`，helper / descriptor factory 归属 `src/Provider/TianShu.Provider.Abstractions`。Provider 公开接入路径必须把以下内容一次性对齐：

| 项 | 正式类型 | 作用 |
| --- | --- | --- |
| Provider manifest | `ProviderModuleManifest` | 声明 provider id、version、minimum TianShu version、endpoint、protocol binding、model route set、error specs 和 diagnostics tags。不得包含 API key 明文。 |
| Protocol binding | `ProviderProtocolBinding` | 声明 wire API 与 `ProviderProtocolKind`、streaming/tools/reasoning/json schema/websocket 能力的绑定关系。 |
| Model route set | `ProviderModelRouteSet`、`ProviderModelRouteCandidate` | 声明该 provider 可参与的模型候选；Kernel 只能从已批准 route set 物化 `ModelInvocationStep`。 |
| Usage projection | `ProviderUsageProjection` | 区分真实 provider usage、缺失 usage、估算 usage；不得把 estimated usage 当成真实成本依据。 |
| Metrics projection | `ProviderMetricsProjection`、`ProviderCostProjection` | 投影 provider/model/wireApi/latency/attempt/usage/cost/diagnostics refs，供 Execution Runtime 映射到 runtime metrics。 |
| Error spec | `ProviderErrorSpec`、`ProviderErrorKind` | 统一 authentication、rate limit、invalid request、transport、timeout、protocol violation 等错误分类、retryable 和 user-safe 口径。 |

Provider access 必须经过 `ProviderModuleAccessValidator.Validate(manifest, descriptor, routeSetId)`：

- manifest 与 `ProviderDescriptor` 的 `providerId`、protocol kind、endpoint provider id 必须一致。
- 必须存在 enabled `ProviderProtocolBinding`，且其 `ProviderProtocolKind` 与 descriptor 一致。
- 必须存在指定 `ProviderModelRouteSet`，且至少一个 enabled candidate 使用当前 provider id 和匹配 wire API。
- 不通过校验时不得生成 `ProviderModuleAccessDescriptor`，也不得进入 Runtime provider binding。
- `ProviderModuleAccessDescriptor` 只表达 validated access；实际 HTTP/SSE/WebSocket transport 仍属于 provider module 实现边界。

usage / cost 的规则固定为：

- Provider 返回 `ProviderUsage` 时，`ProviderUsageProjection.Available=true` 且 `Estimated=false`。
- Provider 未返回 usage 时，必须投影 `Available=false` 与 `provider_usage_missing`，不得静默置零。
- cost 只有在 usage 真实可用、price model version、currency 和 cost 同时存在时才能 `Available=true`。
- estimated token 可以作为 diagnostics，但不得进入 provider 真实 usage 或 promotion cost。

`ProviderModuleDescriptorFactory.CreateAccessManifest` 是内置 provider 当前的公开 manifest 工厂；它从 `ProviderDescriptor` 生成 protocol binding、默认 route set、error specs 和 diagnostics tags。第三方 Provider 模板后续必须生成等价 manifest 并通过同一 validator。

### 4.10 Tool 模块公开接入路径

Tool Module 的公开接入合同归属 `src/Contracts/TianShu.Contracts.Tools`。Tool 公开接入路径必须把以下内容一次性对齐：

| 项 | 正式类型 | 作用 |
| --- | --- | --- |
| Tool manifest | `ToolModuleManifest` | 声明 module id、version、minimum TianShu version、工具绑定和 diagnostics tags。不得包含宿主私有 handler、用户输入或 secret。 |
| Tool binding | `ToolModuleToolBinding` | 声明 tool key、schema、permission、side effect、approval requirement、concurrency、implementation binding 和 human gate。 |
| Tool schema | `JsonElement` input/output schema、`JsonSchemaRef`、`ToolCustomInputDefinition` | 声明模型可见的结构化或 freeform 输入/输出面；缺 input schema 必须 fail closed。 |
| Governance envelope | `GovernanceEnvelope` | Runtime 传入的当前治理信封；必须同时允许 module id、tool id、副作用上限和 human gate。 |
| Result projection | `ToolModuleResultProjection`、`ToolModuleResultStatus` | 把 `ToolInvocationResult` 投影为 succeeded / failed / blocked / cancelled / approval-required / timeout，并保留 structured output、content items、failure 和 diagnostics refs。 |
| Validator | `ToolModuleAccessValidator` | 在 Runtime binding 前执行 manifest、descriptor、schema、governance、side effect 和 human gate 校验。 |

Tool access 必须经过 `ToolModuleAccessValidator.Validate(manifest, descriptors, governance)`：

- `governance.AllowedModuleIds` 必须包含 manifest module id。
- 每个 enabled binding 必须能找到同 key 的 `ToolDescriptor`，重复 binding 或重复 descriptor 必须 fail closed。
- binding 与 descriptor 的 `ToolKind` 必须一致。
- binding 或 descriptor 必须提供 input schema、schema ref 或 custom input definition；否则该工具不得进入 provider tool surface。
- binding 不得弱化 descriptor 声明的 side effect level；`SideEffectLevel.Unspecified` 必须 fail closed。
- descriptor 要求 human gate 或 `ToolApprovalRequirement.Required` 时，binding 必须继续声明 human gate，不得关闭。
- `ToolDescriptor.IsAllowedBy(GovernanceEnvelope)` 必须通过；这会二次校验 tool allow-list、副作用上限和 human gate。
- 不通过校验时不得生成 `ToolModuleAccessDescriptor`，也不得进入 `ExecutionRuntimeToolBridge` 或 provider tool surface。

Tool result projection 的规则固定为：

- handler 正常返回的 `ToolInvocationResult` 通过 `ToolInvocationResultProjector` 形成模型可消费输出，再包装为 `ToolModuleResultProjection.Status=Succeeded` 或 `Failed`。
- governance、human gate、approval、timeout、cancel 等 Runtime 级拒绝不得伪造成 handler 成功；必须使用 `ProjectBlockedResult` 或等价结构化结果，并设置 `Blocked` / `ApprovalRequired` / `Timeout` / `Cancelled`。
- projection 可以暴露 tool key、call id、failure code、user-safe message、output text、structured output、content items 和 diagnostics refs；不得暴露 secret、完整敏感路径、raw approval payload 或宿主私有状态。
- 第三方 Tool 模板必须生成等价 manifest，并通过同一 validator 后才能注册为 active tool binding。

### 4.11 Memory 模块公开接入路径

Memory Module 的公开接入合同归属 `src/Contracts/TianShu.Contracts.Memory`，内置实现归属 `src/Core/TianShu.IdentityMemory`。Memory 公开接入路径必须把以下内容一次性对齐：

| 项 | 正式类型 | 作用 |
| --- | --- | --- |
| Memory manifest | `MemoryModuleManifest` | 声明 module id、version、minimum TianShu version、provider descriptor、capability binding、ContextPolicy binding、compression reservation 和 diagnostics tags。 |
| Provider descriptor | `MemoryProviderDescriptor` | 声明 provider id、capability flags、supported scopes、trust、degradation strategy、network/credential requirement 和 feature flags。 |
| Capability binding | `MemoryModuleCapabilityBinding`、`MemoryModuleCapabilityKind` | 声明 retrieve、form、supersede、compress reserved 的 provider capability、permission、side effect、human gate 和 executable 边界。 |
| ContextPolicy binding | `MemoryContextPolicyBinding` | 声明 Memory 只能投影 `ContextSourceKind.MemoryRecord`，不能自行裁切上下文；裁切、降级和丢弃由已批准 `ApprovedContextPolicy` 与 Execution Runtime bridge 负责。 |
| Compression reservation | `MemoryCompressionReservation` | P27 前只作为压缩接口预留，必须保持 reserved-only，不得作为 executable memory capability 暴露。 |
| Context candidate projection | `MemoryContextCandidateProjection` | 将 `MemoryQueryResult` 中的 `FactMemoryRecord` 投影为 provider-neutral `ContextSourceCandidate`，供 ContextPolicy bridge 后续裁切。 |
| Validator | `MemoryModuleAccessValidator` | 在 Runtime binding 前执行 manifest、governance、provider capability、ContextPolicy 和压缩预留校验。 |

Memory access 必须经过 `MemoryModuleAccessValidator.Validate(manifest, governance, approvedContextPolicy)`：

- `governance.AllowedModuleIds` 必须包含 manifest module id。
- provider id 不得重复，`MemoryProviderTrustLevel.Unknown` 和 `MemoryProviderDegradationStrategy.Unknown` 必须 fail closed。
- manifest 必须声明 enabled retrieve、form、supersede 和 compress reserved 能力；缺任何一类都不得注册为 active Memory module。
- enabled capability 必须引用存在的 provider，且 provider capability flags 必须覆盖 capability 所需能力。
- retrieve 必须包含 `Filter` 或 `ReadOnlyAccess`，副作用不得高于 `ReadOnly`。
- form 必须包含 `Add` 或 `Extract`；supersede 必须包含 `Supersede`。
- capability 的 `SideEffectLevel.Unspecified`、超过治理信封上限、或要求 human gate 但 governance 未提供 human gate 时必须 fail closed。
- `MemoryContextPolicyBinding.SourceKind` 必须是 `MemoryRecord`，projection mode 不能为 `Unspecified`，且不得设置 `ModuleMaySliceContext=true`。
- 已批准 `ContextPolicy` 要求 evidence ref 时，Memory binding 不得关闭 evidence requirement。
- 已批准 `ContextPolicy` 必须允许 `ContextSourceKind.MemoryRecord`，否则 Memory 结果不得进入上下文候选。
- compression reservation 必须存在且 `ReservedOnly=true`；`CompressReserved` capability 在 P27 前必须 `Executable=false`。

Memory result / context projection 的规则固定为：

- `MemoryQueryResult` 可以投影为 `MemoryContextCandidateProjection`，其中每条 `FactMemoryRecord` 只成为 `ContextSourceCandidate`。
- Memory Module 不得自行执行 token budget 裁切、低置信降级、去重或最终 inclusion/drop；这些只属于 `ApprovedContextPolicy` 与 Execution Runtime context bridge。
- 缺 evidence ref 的记录可以被投影为候选，但必须保留空 evidence ref，让 context bridge 按 policy fail closed 或 drop。
- `AddMemory` 只表达新增事实；正式纠错必须使用 `SupersedeMemory` 并保留 `MemorySupersedeLink`。
- Memory Module 不得保存完整模型思考链或内部推理轨迹，只允许保存用户确认事实、工具证据、artifact 引用、反馈、引用记录和可审计摘要。

## 5. 接口骨架归属

归属项目：`src/Contracts/TianShu.Contracts.Modules/TianShu.Contracts.Modules.csproj`

```csharp
public sealed record ModuleDescriptor
{
    public string ModuleId { get; }
    public ModuleKind Kind { get; }
    public string DisplayName { get; }
    public string Version { get; }
    public IReadOnlyList<ModuleCapabilityDescriptor> Capabilities { get; }
    public ModuleSchemaRef? ConfigurationSchema { get; }
    public IReadOnlyList<ModuleConfigurationRequirement> RequiredConfiguration { get; }
    public IReadOnlyList<ModuleRuntimeDependency> RuntimeDependencies { get; }
    public string MinimumTianShuVersion { get; }
    public PermissionEnvelope Permission { get; }
    public SideEffectProfile SideEffects { get; }
    public ModuleAuditProfile Audit { get; }
    public ModuleTrustLevel TrustLevel { get; }
    public ModuleHealthProbe Health { get; }
    public ModuleImplementationBinding? ImplementationBinding { get; }
    public MetadataBag Metadata { get; }
}

public sealed record ModuleConfigurationRequirement
{
    public string Key { get; }
    public string DisplayName { get; }
    public ModuleSchemaRef? ValueSchema { get; }
    public bool Required { get; }
    public bool Secret { get; }
    public string? Description { get; }
}

public sealed record ModuleRuntimeDependency
{
    public string DependencyId { get; }
    public string DisplayName { get; }
    public ModuleRuntimeDependencyKind Kind { get; }
    public string? VersionRange { get; }
    public bool Required { get; }
    public string? Description { get; }
}

public sealed record ModuleCapabilityDescriptor
{
    public string CapabilityId { get; }
    public string DisplayName { get; }
    public ModuleSchemaRef? InputSchema { get; }
    public ModuleSchemaRef? OutputSchema { get; }
    public PermissionEnvelope Permission { get; }
    public SideEffectProfile SideEffects { get; }
    public MetadataBag Metadata { get; }
}

public sealed record ModuleDiscoveryRoot
{
    public string RootId { get; }
    public string Path { get; }
    public ModuleDiscoverySourceKind SourceKind { get; }
    public ModuleTrustLevel TrustLevel { get; }
    public int Priority { get; }
    public bool Enabled { get; }
}

public sealed record ModuleManifestProjection
{
    public string ModuleId { get; }
    public ModuleKind Kind { get; }
    public string DisplayName { get; }
    public string Version { get; }
    public ModuleManifestSource Source { get; }
    public bool Enabled { get; }
    public int Priority { get; }
    public string? SdkContractVersion { get; }
    public string? MinimumTianShuVersion { get; }
    public IReadOnlyList<string> Capabilities { get; }
    public IReadOnlyList<string> Diagnostics { get; }
    public ModuleImplementationBinding? ImplementationBinding { get; }
}

public sealed record ModuleDiscoveryCandidate
{
    public ModuleManifestProjection Manifest { get; }
    public ModuleDescriptor? Descriptor { get; }
    public ModuleDiscoveryCandidateStatus Status { get; }
    public string? StatusReason { get; }
}

public sealed record ModuleDiscoverySnapshot
{
    public IReadOnlyList<ModuleDiscoveryRoot> Roots { get; }
    public IReadOnlyList<ModuleDiscoveryCandidate> Candidates { get; }
    public IReadOnlyList<ModuleDiscoveryIssue> Issues { get; }
    public IReadOnlyList<ModuleDiscoveryCandidate> SelectedCandidates { get; }
}

public sealed record ModuleLoadingPolicy
{
    public string CurrentTianShuVersion { get; }
    public IReadOnlySet<string> ExplicitlyAllowedModuleIds { get; }
    public bool RequireExplicitAllowForThirdParty { get; }
    public IReadOnlySet<string> BoundConfigurationKeys { get; }
}

public sealed record ModuleIsolationBoundary
{
    public ModuleIsolationKind Kind { get; }
    public string BoundaryId { get; }
    public string? SourcePath { get; }
    public bool Collectible { get; }
}

public sealed record ModuleServiceRegistration
{
    public string ServiceId { get; }
    public string ImplementationId { get; }
    public ModuleServiceLifetime Lifetime { get; }
    public IReadOnlyList<string> CapabilityIds { get; }
}

public sealed record ModuleLoadRecord
{
    public ModuleDiscoveryCandidate Candidate { get; }
    public ModuleLoadStatus Status { get; }
    public ModuleIsolationBoundary? IsolationBoundary { get; }
    public IReadOnlyList<ModuleServiceRegistration> ServiceRegistrations { get; }
    public IReadOnlyList<ModuleLoadDiagnostic> Diagnostics { get; }
}

public sealed record ModuleAssemblyPlan
{
    public IReadOnlyList<ModuleLoadRecord> Records { get; }
    public IReadOnlyList<ModuleLoadRecord> RegisteredRecords { get; }
    public IReadOnlyList<ModuleLoadDiagnostic> Diagnostics { get; }
}

public interface IModuleCompositionRoot
{
    ValueTask<ModuleAssemblyPlan> ComposeAsync(ModuleCompositionRootContext context, CancellationToken cancellationToken);
}

public sealed record ProviderModuleManifest
{
    public string ProviderId { get; }
    public string DisplayName { get; }
    public string Version { get; }
    public string MinimumTianShuVersion { get; }
    public IReadOnlyList<ProviderProtocolBinding> ProtocolBindings { get; }
    public IReadOnlyList<ProviderModelRouteSet> ModelRouteSets { get; }
    public ProviderEndpointDescriptor Endpoint { get; }
    public IReadOnlyList<ProviderErrorSpec> ErrorSpecs { get; }
    public IReadOnlyList<string> Diagnostics { get; }
}

public sealed record ProviderProtocolBinding
{
    public string WireApi { get; }
    public ProviderProtocolKind ProtocolKind { get; }
    public ProviderCapabilityProfile Capabilities { get; }
    public bool Enabled { get; }
}

public sealed record ProviderModelRouteSet
{
    public string RouteSetId { get; }
    public IReadOnlyList<ProviderModelRouteCandidate> Candidates { get; }
    public string? DefaultModel { get; }
}

public sealed record ProviderUsageProjection
{
    public bool Available { get; }
    public bool Estimated { get; }
    public long? InputTokens { get; }
    public long? OutputTokens { get; }
    public long? ReasoningTokens { get; }
    public long? TotalTokens { get; }
    public string Source { get; }
    public string? MissingReason { get; }
}

public sealed record ProviderMetricsProjection
{
    public string ProviderId { get; }
    public string Model { get; }
    public string WireApi { get; }
    public ProviderUsageProjection Usage { get; }
    public ProviderCostProjection Cost { get; }
    public TimeSpan Latency { get; }
    public int AttemptIndex { get; }
    public IReadOnlyList<string> DiagnosticsRefs { get; }
}

public sealed record ToolModuleManifest
{
    public string ModuleId { get; }
    public string DisplayName { get; }
    public string Version { get; }
    public string MinimumTianShuVersion { get; }
    public IReadOnlyList<ToolModuleToolBinding> Tools { get; }
    public IReadOnlyList<string> Diagnostics { get; }
}

public sealed record ToolModuleToolBinding
{
    public string ToolKey { get; }
    public ToolKind Kind { get; }
    public JsonElement? InputSchema { get; }
    public JsonSchemaRef? InputSchemaRef { get; }
    public PermissionDeclaration Permission { get; }
    public SideEffectProfile SideEffects { get; }
    public ToolApprovalRequirement ApprovalRequirement { get; }
    public ToolConcurrencyClass ConcurrencyClass { get; }
    public bool RequiresHumanGate { get; }
    public bool Enabled { get; }
}

public sealed record ToolModuleAccessDescriptor
{
    public ToolModuleManifest Manifest { get; }
    public IReadOnlyList<ToolDescriptor> Tools { get; }
    public GovernanceEnvelope Governance { get; }
}

public sealed record ToolModuleResultProjection
{
    public CallId CallId { get; }
    public string ToolKey { get; }
    public ToolModuleResultStatus Status { get; }
    public bool Success { get; }
    public string OutputText { get; }
    public JsonElement StructuredOutput { get; }
    public ToolInvocationFailure? Failure { get; }
    public IReadOnlyList<ToolOutputContentItem> OutputContentItems { get; }
    public IReadOnlyList<string> DiagnosticsRefs { get; }
}

public sealed record MemoryModuleManifest
{
    public string ModuleId { get; }
    public string DisplayName { get; }
    public string Version { get; }
    public string MinimumTianShuVersion { get; }
    public IReadOnlyList<MemoryProviderDescriptor> Providers { get; }
    public IReadOnlyList<MemoryModuleCapabilityBinding> Capabilities { get; }
    public MemoryContextPolicyBinding ContextPolicyBinding { get; }
    public IReadOnlyList<MemoryCompressionReservation> CompressionReservations { get; }
    public IReadOnlyList<string> Diagnostics { get; }
}

public sealed record MemoryModuleCapabilityBinding
{
    public string CapabilityId { get; }
    public MemoryModuleCapabilityKind Kind { get; }
    public string ProviderId { get; }
    public MemoryProviderCapability RequiredCapabilities { get; }
    public PermissionEnvelope Permission { get; }
    public SideEffectProfile SideEffects { get; }
    public bool RequiresHumanGate { get; }
    public bool Executable { get; }
    public bool Enabled { get; }
}

public sealed record MemoryContextPolicyBinding
{
    public ContextSourceKind SourceKind { get; }
    public ContextProjectionMode ProjectionMode { get; }
    public bool RequireEvidenceRefs { get; }
    public bool ModuleMaySliceContext { get; }
}

public sealed record MemoryModuleAccessDescriptor
{
    public MemoryModuleManifest Manifest { get; }
    public GovernanceEnvelope Governance { get; }
    public ApprovedContextPolicy ContextPolicy { get; }
}

public sealed record MemoryContextCandidateProjection
{
    public string PolicyId { get; }
    public IReadOnlyList<ContextSourceCandidate> Candidates { get; }
    public IReadOnlyList<string> DiagnosticsRefs { get; }
}

public interface IModuleHealthCheck
{
    ModuleDescriptor Descriptor { get; }

    ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken);
}
```

`ModuleDescriptor` 的默认治理必须 fail closed：

- `ModuleTrustLevel.Unspecified` 不能被装配层当成可信模块。
- `SideEffectLevel.Unspecified` 不能被 Execution Runtime 当成可执行副作用。
- `MinimumTianShuVersion`、必需配置和运行时依赖必须由 discovery / admission 参与校验。
- 默认 `PermissionEnvelope.RequiresHumanGate` 为 `true`。
- 默认 `ModuleHealthStatus.Unknown` 不能被当成 healthy。

## 6. 当前接入规则

- Provider Module 必须先声明 `ProviderDescriptor`，再投影为 `ModuleDescriptor`。
- Tool Module 必须先声明 `ToolDescriptor`，再投影为 `ModuleDescriptor`。
- Configuration 在专项改造完成前，先以 `BuiltInModuleDescriptors` 作为目标 descriptor 基线；已完成专项改造的 Memory、Artifact、Diagnostics、Workspace 必须提供对应 module surface 与 Runtime bridge。
- Diagnostics Module 必须通过 `IDiagnosticsModule.EmitAsync` 接收 typed `DiagnosticsModuleEvent`，并在写入 sink 前统一脱敏 payload、metadata 和 failure message。
- Module implementation binding 只能标记项目、类型或包标识，不暴露外部 SDK 私有类型。
- Module capability 的 permission、side effect 和 audit 必须与对应 provider/tool/module 专项 descriptor 保持一致。
- Module health / smoke check 只返回 typed `ModuleSmokeCheckResult` 与 diagnostics ref，不直接修复或变更运行时状态。
- Remote Continuity Module 的正式设计归属 `docs/architecture/tianshu-remote-continuity-design.md`；不得由 CLI 默认发现、默认加载或默认开放公网监听，启用后也只能作为状态/控制 transport 进入 Host Gateway / Control Plane。Remote Module 激活必须通过 `RemoteModuleActivationContext` 校验 transport、pairing、short-lived token、device identity 和 scope。

## 7. 边界约束

- Module 不反向引用 Experience、Host Gateway、Control Plane、Kernel 或 Execution Runtime 具体实现。
- Module 不选择 StageGraph。
- Module 不生成 RuntimeStep。
- Module 不决定 ModelRoutePolicy、ContextPolicy 或 GovernanceEnvelope。
- Module 调用必须由 Execution Runtime 执行 Kernel 已批准的 `RuntimeStep`。
- 高副作用 Module 必须声明 side effect、audit，并由 policy 决定是否需要 human gate。

## 8. 验收标准

- 所有 AI 可使用能力都能映射为 `ITianShuTool` 或 `ModuleCapabilityDescriptor`。
- Provider、Tool、Memory、Artifact、Diagnostics、Workspace、Configuration 均有可发现的 `ModuleDescriptor`。
- Provider、Tool、Memory、Diagnostics、Projection 和 Remote Continuity 扩展点均能在文档中定位到公开合同项目、实现项目归属和运行时装配边界。
- 第三方模块生命周期、trust level、版本兼容、健康检查、禁用/卸载、失败降级和治理原则在本文中有明确验收口径。
- Module descriptor 能投影 trust level、capability set、configuration schema、permission、side effect、audit 和 health。
- Module 项目依赖测试禁止其引用上层宿主、控制面、Kernel 实现或 Execution Runtime 实现。
- Module 输出必须可诊断、可审计、可回放。
