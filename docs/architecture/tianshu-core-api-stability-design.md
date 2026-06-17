# TianShu 核心 API 稳定承诺

## 1. 文档定位

本文定义 TianShu v1.0 前必须冻结的核心 API 稳定承诺。它是 `docs/tianshu-architecture-spec.md` 的兼容策略补充，约束 Contracts、Module SDK、Host Gateway、Control Plane、RuntimeStep 和 StageGraph 的版本演进。

本文只定义公开兼容边界，不承诺内部实现、组合根、测试夹具或产品私有 DTO 的稳定性。P31.2 的版本兼容测试必须以本文作为验收基线。

## 2. 涉及项目

| 稳定面 | 当前项目 | 兼容责任 |
| --- | --- | --- |
| Contracts | `src/Contracts/TianShu.Contracts.*` | 公开 typed boundary、值对象、请求、结果、投影、诊断和治理信封。 |
| Module SDK | `src/Contracts/TianShu.Contracts.Modules`、`src/Contracts/TianShu.Contracts.Provider`、`src/Contracts/TianShu.Contracts.Tools`、`src/Contracts/TianShu.Contracts.Memory`、`src/Contracts/TianShu.Contracts.Diagnostics`、`src/Contracts/TianShu.Contracts.Projections`、`src/Contracts/TianShu.Contracts.Remote`、`src/Provider/TianShu.Provider.Abstractions` | 第三方模块可依赖的 descriptor、manifest、health、family invocation、usage / metrics projection 和可选 helper。 |
| Host Gateway | `src/Contracts/TianShu.Contracts.Host`、`src/Core/TianShu.HostGateway` | 宿主 typed operation、thread/status snapshot、event subscription、远程命令接入和宿主可消费 projection。 |
| Control Plane | `src/Core/TianShu.ControlPlane`、`src/Contracts/TianShu.Contracts.Governance`、`src/Contracts/TianShu.Contracts.Kernel` | operation 归一化、治理信封、受控 CoreIntent、状态/查询/control operation 分流结果。 |
| RuntimeStep / ExecutionPlan | `src/Contracts/TianShu.Contracts.Execution`、`src/Contracts/TianShu.Contracts.Kernel`、`src/Execution/TianShu.Execution.Runtime` | Kernel 批准后进入 Execution Runtime 的 step 形态、来源标识、治理包络、结果、metrics 和 replay refs。 |
| StageGraph | `src/Contracts/TianShu.Contracts.Kernel`、`src/Core/TianShu.Kernel`、`src/Core/TianShu.Adaptive` | Kernel IR、stage/edge/policy/budget/evaluation 语义、候选 graph / patch 的验证输入和 replay 兼容标识。 |

未来若新增公开 SDK 项目，必须先在本文增加所属稳定面和兼容责任，再进入产品发布门禁。

## 3. 稳定等级

TianShu 的 API 稳定性分为四级：

| 等级 | 含义 | 变更规则 |
| --- | --- | --- |
| Stable public contract | 第三方模块、宿主、远程客户端或发布包消费者可依赖的类型与语义。 | 只能 additive 演进；破坏性变更必须升主版本并提供迁移说明。 |
| Versioned public schema | 配置 schema、module manifest、StageGraph fixture、release manifest 等可持久化结构。 | 必须带 schema / contract version；未知主版本 fail closed；兼容次版本可降级诊断。 |
| Internal typed boundary | 仓库内部跨项目类型，但不承诺外部依赖稳定。 | 可随实现重构，但不得破坏 Stable public contract 的行为。 |
| Implementation detail | bridge、composition root、私有 DTO、provider raw payload、CLI 渲染细节、测试 helper。 | 不提供兼容承诺；不得被第三方模块或宿主作为依赖。 |

公开文档、README、模板和示例只能把 Stable public contract 与 Versioned public schema 描述为可依赖边界。内部实现可以被解释为当前实现，但不能写成第三方稳定 API。

## 4. 总体兼容策略

核心 API 演进必须遵守以下规则：

- 优先新增字段、新增枚举值、新增 capability 和新增 projection，不重命名、不删除、不改变现有字段语义。
- 新增字段必须有安全默认值，旧消费者忽略该字段时不得扩大权限、副作用、预算或信任边界。
- 破坏性变更必须同时包含版本提升、迁移说明、诊断 code、兼容测试和 release notes。
- 反序列化遇到未知主版本、未知高风险 capability、未知 side effect level、未知 trust level 或未知 governance 语义时必须 fail closed。
- 反序列化遇到可忽略的未知次版本字段时可以 degraded，但必须输出 diagnostics，不得伪造成完整支持。
- deprecated 字段至少保留一个公开 release 窗口；删除前必须有替代字段、迁移说明和测试覆盖。
- 所有稳定边界不得包含 API key、token、secret 明文、本机绝对私有路径、raw provider request body、raw provider response body 或未脱敏 diagnostics。
- 任何 compatibility bridge 只能位于边界适配层，不能作为新的核心路径继续扩散。

## 5. Contracts 稳定承诺

Contracts 是 TianShu 跨层 typed boundary 的 source of truth。v1.0 稳定承诺如下：

- `TianShu.Contracts.Primitives` 的 identifier、metadata、structured value、problem details 等基础值对象保持 additive 演进。
- `TianShu.Contracts.Kernel` 的 `CoreIntent`、`StageGraph`、`StageNode`、`StageEdge`、`KernelOperation`、`KernelProposal`、`KernelTrace`、evaluation evidence 和 strategy lifecycle 是 Kernel/Core Loop 的稳定合同。
- `TianShu.Contracts.Execution` 的 `ExecutionPlan`、`RuntimeStep`、`RuntimeStepResult`、runtime metrics、diagnostics refs 和 replay refs 是 Execution Runtime 的稳定合同。
- `TianShu.Contracts.Host` 与 `TianShu.Contracts.Projections` 的 thread/status/evidence projection 是宿主消费状态的稳定合同；宿主不得依赖 RuntimeComposition 内部 DTO。
- `TianShu.Contracts.Provider` 只稳定 provider-neutral invocation、stream event、tool request/result、usage projection 和 diagnostics ref；provider wire payload 不稳定。
- `TianShu.Contracts.Tools` 只稳定 tool descriptor、schema、permission、side effect、handler invocation 和 result envelope；具体工具实现不稳定。
- `TianShu.Contracts.Modules` 只稳定 module descriptor、manifest projection、health、trust、configuration schema ref、capability descriptor 和 smoke check；discovery loader 实现不稳定。
- `TianShu.Contracts.Remote` 只稳定 remote snapshot、event cursor、command envelope、scope、idempotency、pairing/token descriptor 和 remote module activation；具体 transport 实现不稳定。

Contracts 项目不得引用 Host Gateway、Control Plane、Kernel、Execution Runtime、Provider 实现、Tool 实现或 AppHost 实现项目。

## 6. Module SDK 稳定承诺

Module SDK 的稳定边界是“第三方模块能依赖什么”，不是 TianShu 内部如何装配模块。v1.0 承诺如下：

- 第三方模块只能依赖公开 contracts / abstractions，不得依赖 `TianShu.RuntimeComposition`、`TianShu.Execution.Runtime`、Host Gateway、Control Plane、Kernel 实现或产品宿主项目。
- module manifest 必须携带 `moduleVersion`、`sdkContractVersion`、`minimumTianShuVersion`、`capabilitySchemaVersion` 和 runtime dependencies；TOML 字段分别为 `version`、`sdk_contract_version`、`min_tianshu_version` 和 `capability_schema_version`。
- `sdkContractVersion` 主版本不兼容必须 fail closed；次版本缺能力可 degraded，但必须诊断。
- descriptor 与 manifest 冲突时拒绝加载；缺 descriptor、缺 health、缺配置、版本不兼容、trust 不足或治理不允许时必须 fail closed。
- 旧 manifest 缺少 `sdk_contract_version` 或 `capability_schema_version` 时可以继续进入既有 descriptor / health / trust / governance 门禁；一旦显式声明不可解析版本或未知主版本，必须分别输出 `module_load.sdk_contract_version_invalid`、`module_load.sdk_contract_version_incompatible`、`module_load.capability_schema_version_invalid` 或 `module_load.capability_schema_version_incompatible`。
- family invocation contract 稳定，family implementation 不稳定。例如 Provider SDK 稳定 `IProviderModule` 和 provider-neutral request / stream event，不稳定 OpenAI / Anthropic / Google 的内部 HTTP composer。
- 模板和示例必须只引用公开 SDK 包，不能依赖本机安装路径、私有仓库路径、API key 或用户配置明文。

## 7. Host Gateway 稳定承诺

Host Gateway 是 CLI、VSIX、Config GUI、Remote Module、Service 或未来宿主获得统一控制入口的稳定边界。v1.0 承诺如下：

- 宿主只能提交 typed host operation、宿主能力声明、显式 feature flag 和已解析安全配置引用。
- Host Gateway 输出宿主可消费 projection：thread snapshot、runtime status、token usage、diagnostics、evidence、approval/input request 和 terminal disposition。
- Host Gateway 不暴露 `CoreIntent`、`StageGraph`、`RuntimeStep`、`StableKernelCore`、`AdaptiveRuntimeExecutionLoop` 或 RuntimeComposition 私有 DTO。
- 远程写入命令必须通过 `IRemoteCommandIngress -> IHostGateway.InvokeAsync -> ControlPlane.ProcessAsync`，不得直接写 runtime state、workspace 或 store。
- Host Gateway 兼容性以 typed operation 名称、payload schema version、projection schema version 和 failure code 为准；CLI 输出文本不是稳定 API。

## 8. Control Plane 稳定承诺

Control Plane 的稳定职责是 operation normalization、governance 和 routing。v1.0 承诺如下：

- 用户操作、系统操作、远程命令和宿主控制操作必须先归一化为 typed control operation。
- Control Plane 可以直接处理查询、目录、诊断、投影或状态控制；需要核心执行时只产出受治理 `CoreIntent`。
- Control Plane 不编排 StageGraph，不选择模型调用步骤，不执行工具，不处理 provider wire protocol。
- `GovernanceEnvelope` 的权限、副作用、human gate、budget、allowed tool/module 集合和 trace policy 是稳定治理边界。
- 任何未知 operation、未知 governance 主版本或无法证明授权的 command 必须 fail closed，并输出稳定 failure code。

## 9. RuntimeStep / ExecutionPlan 稳定承诺

`RuntimeStep` 是 Execution Runtime 能产生外部影响的唯一执行形式。v1.0 承诺如下：

- 每个 step 必须携带 `stepId`、`RuntimeStepKind`、source intent / graph / stage / kernel operation、governance envelope、budget、trace refs 和 diagnostics refs。
- RuntimeStep kind 可以 additive 扩展；旧 runtime 遇到未知 kind 必须 blocked / failed，不得静默执行。
- `ModelInvocationStep`、`ToolInvocationStep`、`ModuleCapabilityStep`、`HostInteractionStep`、`DiagnosticStep`、`StateCommitStep` 和 `ArtifactStep` 的核心语义保持稳定。
- Execution Runtime 必须对 RuntimeStep 与 descriptor / tool descriptor / provider descriptor / module descriptor 进行二次治理校验。
- runtime result 必须保留 status、failure code、result payload ref、metrics event ids、diagnostics refs、replay refs 和 token / cost source。
- `estimated=true` 的 token / cost 只能作为 diagnostics，不得计入真实 provider usage 或 strategy promotion 成本。
- `RunReactiveAsync` 允许模型动态请求工具后物化新 step，但新 step 仍必须携带 Kernel 来源标识并通过治理校验。

## 10. StageGraph 稳定承诺

StageGraph 是 Kernel/Core Loop 的可验证 IR。v1.0 承诺如下：

- StageGraph 必须表达 graph id、version、intent type、stages、edges、policies、budgets、allowed tools、checkpoint rules、recovery rules 和 evaluation rules。
- Stage 必须表达 id、kind、objective、input/output contract、allowed KernelTool、allowed CapabilityTool、side effect level、budget、success criteria 和 failure handler。
- Adaptive Orchestration Layer 只能提出 StageGraph / patch / strategy 候选；Stable Kernel Core 必须验证后才可物化 ExecutionPlan。
- StageGraph schema 必须带版本；未知主版本 fail closed；已知主版本的 additive 字段可以忽略但必须保留 diagnostics。
- StageGraph fixture 的兼容性以 IR 字段、stage/edge/policy 语义、source ids、replay refs 和 validation diagnostics 为准，不以自然语言 plan 为准。
- StageGraph 不得包含 secret、raw provider payload、本地绝对私有路径或未脱敏工具输出。

## 11. 非稳定边界

下列内容不属于核心 API 稳定承诺：

- `src/Core/TianShu.RuntimeComposition` 的组合根和默认绑定细节。
- `src/Execution/TianShu.Execution.Runtime` 内部 bridge、binding registry 和 resource lifecycle 实现。
- Provider 内部 HTTP request composer、SSE parser、retry strategy、endpoint 选择细节。
- CLI 控制台文字、颜色、进度条、交互渲染和调试输出。
- Config GUI、VSIX、AppHost 的私有 DTO。
- 测试 helper、验收脚本内部 JSON、临时 evidence 文件和本地 runtime state。
- raw provider request / response、raw secret、raw token、用户配置明文和本机私有路径。

如果上述内部对象需要被外部使用，必须先提升为正式 contract，并补齐版本、诊断、兼容测试和文档。

## 12. P31.2 兼容测试基线

P31.2 必须至少补齐以下版本兼容测试：

| 测试对象 | 最低要求 |
| --- | --- |
| 旧配置 | 旧 `tianshu.toml` 能加载到当前 typed config；不兼容字段给出诊断；secret 不回显。 |
| 旧 module manifest | 旧 manifest 能进入 discovery / validation；缺字段 degraded 或 fail closed；版本不兼容有稳定 failure code。 |
| 旧 StageGraph fixture | 旧 graph / patch fixture 能被 validator 解释；未知主版本 fail closed；次版本 additive 字段不扩大治理边界。 |
| 旧 release package | 旧 release 的配置、manifest、checksum、doctor / init 行为可被当前工具诊断；不能依赖私有路径。 |

这些测试必须验证兼容行为和 failure code，不得只做字符串存在性检查。
