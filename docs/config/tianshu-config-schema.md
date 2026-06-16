# TianShu 配置 Schema 设计

## 1. 文档定位

配置模块属于 Module Plane 的基础能力，并向 Host Gateway 提供只读配置投影。配置系统只负责读取、校验、合并、投影和保存配置事实，不直接拥有 Kernel 编排、模型路由选择、上下文裁切、工具授权或 provider 调用决策。

本文是配置 schema 与配置业务逻辑改造的验收基线。后续实现和测试必须以本文为准，并与 `docs/tianshu-architecture-spec.md` 的六层架构、`docs/architecture/tianshu-kernel-core-loop-design.md` 的 Kernel 契约保持一致。

## 2. 当前项目

| 项目 | 当前用途 | 新基线下的职责 |
| --- | --- | --- |
| `src/Contracts/TianShu.Contracts.Configuration` | 配置投影与变更契约。 | 承载正式配置 schema、配置投影、配置变更预览和应用结果的 typed contract。 |
| `src/Core/TianShu.Configuration` | 配置解析、schema catalog、投影构造。 | 作为 Configuration Module 的核心实现，负责 TOML 读取、层叠合并、schema 校验、配置事实输出和只读投影。 |
| `src/Hosting/TianShu.AppHost.Configuration` | AppHost 配置组合与宿主侧读取。 | 只作为宿主进程配置加载、路径解析、投影和迁移 bridge；不得包含 Kernel、Execution、Provider 或 Tool 决策。 |
| `src/Hosting/TianShu.AppHost.Catalog` | AppHost catalog 与 provider 诊断 surface。 | 承接 `catalog/list`、provider connectivity probe 和 provider smoke test plan；只能消费配置投影与 provider 抽象，不得回流到配置加载项目。 |
| `src/Presentations/TianShu.ConfigGui` | 配置 GUI。 | 只编辑正式 schema 字段，只通过配置投影和变更预览写入配置。 |
| `tests/TianShu.Contracts.Configuration.Tests` | 配置契约测试。 | 覆盖 schema contract、projection contract、变更 contract。 |
| `tests/TianShu.Configuration.Tests` | 配置实现测试。 | 覆盖 schema catalog、TOML 解析、层叠合并、正式字段校验、未知字段诊断。 |
| `tests/TianShu.ConfigGui.Tests` | GUI 配置测试。 | 覆盖 ConfigGUI 只编辑正式 schema 和 preview/apply 流程。 |
| `tests/TianShu.AppHost.Configuration.Tests` | 宿主配置测试。 | 覆盖 AppHost.Configuration 只加载/投影，不做运行时决策。 |

## 3. 配置入口

| 类型 | 入口 |
| --- | --- |
| 用户目录 | `~/.tianshu` |
| 主配置 | `tianshu.toml` |
| Prompt 配置 | `modules/prompts/<package>/prompt.toml` |
| 模块配置 | `modules/**/**/*.toml` |
| 环境变量 | `TIANSHU_CONFIG__...`，仅作为显式 schema source layer；其他 `TIANSHU_*` 只能作为 secret reference 指向的外部值来源 |

配置读取必须按 source layer 生成 `ConfigurationSourceLayer`。任何环境变量、命令行参数或 session override 都必须进入配置层叠和投影，不能绕过 schema 直接改变 Kernel、Execution Runtime、Provider 或 Tool 行为。环境变量覆盖只接受 `TIANSHU_CONFIG__` 前缀，并用双下划线映射 schema path，例如 `TIANSHU_CONFIG__KERNEL__ENABLED` 映射到 `kernel.enabled`；普通密钥环境变量不得直接成为配置字段，只能被 `*_env`、`*.env`、`*_ref`、`*.ref` 等 secret reference 字段引用。命令行和 session override 也只接受正式 schema path，不得把 `modelProvider`、`mcpServers`、`apiKey`、`enabledTools` 等旧别名自动转写为正式字段；旧别名只能进入未知字段诊断或迁移建议。

## 4. 正式 Schema 区域

| 区域 | 归属 | 允许影响 |
| --- | --- | --- |
| `host` | Host Gateway 默认行为。 | Host surface、snapshot、subscription、projection 默认值。 |
| `control` | Control Plane 分类与治理默认值。 | operation normalization、query/control/state/governance/core-intent 分类默认值。 |
| `kernel` | Kernel / Core Loop 配置。 | 内置 StageGraph 选择、adaptive orchestration 开关、strategy lifecycle gate、Kernel 预算和验证器开关。 |
| `execution` | Execution Runtime 配置。 | RuntimeStep 超时、stream、retry、并发、trace、diagnostics ref 要求和资源上限。 |
| `modules` | Module Plane 配置。 | 模块注册、启用、trust level、health check、capability set、descriptor 路径。 |
| `providers` | Provider Module 配置。 | provider descriptor、endpoint、协议能力、模型 catalog、secret reference。 |
| `tools` | Tool Module 配置。 | tool descriptor、permission declaration、side effect profile、audit profile、实现绑定。 |
| `plugins` / `apps` | Plugin / App Connector 配置。 | 插件启用状态、已安装插件投影、marketplace trust、remote marketplace、app connector 绑定。 |
| `memory` | Memory / Identity Module 配置。 | memory space、provider、binding、读写模式、保留策略。 |
| `diagnostics` | Diagnostics Module 配置。 | diagnostics sink、trace、replay、artifact 输出和脱敏策略。 |
| `workspace` | Workspace / Environment Module 配置。 | workspace facts、trust policy、resolver、project binding。 |
| `experience` | Experience Plane 配置。 | CLI/TUI/GUI 展示、realtime、review、feedback 体验配置。 |

正式 schema 不再保留旧字段别名。未知字段只能进入 `raw.unmapped` 投影并产生配置诊断，不得被运行时静默消费。

## 5. Schema 到业务逻辑的边界

### 5.1 Configuration Module

Configuration Module 必须负责：

- 读取 `tianshu.toml`、模块 TOML、环境变量层、命令行层和 session 层。
- 按 source layer 顺序合并为 typed configuration facts。
- 使用 schema catalog 校验字段、值类型、枚举、secret reference 和互斥关系。
- 输出 `ConfigurationProjection`、配置诊断、配置变更 preview 和 apply result。
- 为 Host Gateway、Control Plane、Kernel、Execution Runtime 和 Module Plane 提供只读配置事实或投影。

Configuration Module 不得负责：

- 选择 StageGraph 或解释 StageGraph。
- 生成 `CoreIntent`、`KernelOperation`、`RuntimeStep`。
- 直接调用 provider、tool、memory、artifact、diagnostics sink。
- 基于配置绕过 Stable Kernel Core、permission envelope、side effect profile 或 audit。

### 5.2 AppHost.Configuration

`TianShu.AppHost.Configuration` 只保留进程宿主需要的配置加载、路径解析、投影和迁移 bridge。它不得继续承担以下职责：

- 不得选择模型路由最终候选。
- 不得裁切上下文。
- 不得决定工具权限。
- 不得构造 Kernel 可执行图或 RuntimeStep。
- 不得承载 catalog/list surface、provider connectivity probe 或 provider smoke test plan。
- 不得读取非正式旧字段作为正式执行输入。

如果需要保留迁移 bridge，bridge 必须只输出配置诊断或迁移建议，不得进入主执行链路。

### 5.3 ConfigGUI

ConfigGUI 只能消费 `ConfigurationProjection` 和配置变更 preview/apply contract。ConfigGUI 不得读取 runtime 私有对象，不得编辑未进入 schema catalog 的字段，不得生成 Kernel、Execution 或 Module 私有输入。

## 6. `kernel` 区域目标形态

`kernel` 区域只影响 Kernel 的选择、开关、预算和验证策略，不能绕过 Stable Kernel Core。

目标字段族：

- `kernel.enabled`
- `kernel.default_graph_id`
- `kernel.graph_sets.*`
- `kernel.adaptive.enabled`
- `kernel.adaptive.allowed_kernel_tools`
- `kernel.adaptive.max_proposals_per_turn`
- `kernel.strategy.default_registry`
- `kernel.strategy.promotion_gate`
- `kernel.strategy.trial_runs`
- `kernel.budget.token_budget`
- `kernel.budget.time_budget_ms`
- `kernel.budget.cost_budget`
- `kernel.budget.retry_budget`
- `kernel.budget.tool_call_budget`
- `kernel.validation.fail_closed`
- `kernel.validation.require_governance_envelope`
- `kernel.validation.require_trace_policy`

配置只能产生 `KernelConfigurationFacts` 或等价投影，由 Kernel 读取后再经 Stable Kernel Core 校验。

## 7. `execution` 区域目标形态

`execution` 区域只影响 Execution Runtime 执行已批准 RuntimeStep 的资源边界。

目标字段族：

- `execution.default_profile`
- `execution.profiles.*.timeout_ms`
- `execution.profiles.*.stream_idle_timeout_ms`
- `execution.profiles.*.retry_budget`
- `execution.profiles.*.max_parallelism`
- `execution.profiles.*.require_source_ids`
- `execution.profiles.*.require_permission_envelope`
- `execution.profiles.*.require_trace_policy`
- `execution.profiles.*.diagnostics_ref_required`
- `execution.profiles.*.runtime_trace_ref_required`
- `execution.profiles.*.side_effect_ceiling`

Execution Runtime 必须拒绝缺少 `SourceGraphId`、`SourceStageId`、`SourceKernelOperationId`、`PermissionEnvelope` 或 `TracePolicy` 的 step。配置只能收紧或默认化执行限制，不能放宽 Kernel 已批准的治理边界。

## 8. `modules` 区域目标形态

`modules` 区域统一描述 Module Plane 的可装配能力。

目标字段族：

- `modules.discovery_roots`
- `modules.providers.*.enabled`
- `modules.providers.*.descriptor_ref`
- `modules.tools.*.enabled`
- `modules.tools.*.descriptor_ref`
- `modules.memory.*.enabled`
- `modules.artifacts.*.enabled`
- `modules.diagnostics.*.enabled`
- `modules.workspace.*.enabled`
- `modules.*.trust_level`
- `modules.*.capabilities`
- `modules.*.health_check`

Module 配置只生成 descriptor、health、trust 和 capability set 投影。Module 不得反向依赖 Experience、Host Gateway、Control Plane、Kernel 或 Execution Runtime 具体实现。

## 9. 既有区域收敛

现有 `providers`、`tools`、`plugins`、`apps`、`memory`、`diagnostics`、`workspace` 和 `experience` 字段继续作为正式 schema 的业务域存在，但必须与新架构对象对齐：

- `providers` 输出 `ProviderDescriptor` 和 provider invocation 所需静态配置。
- `tools` 输出 `ToolDescriptor`、`PermissionDeclaration`、`SideEffectProfile`、`AuditProfile`。
- `plugins` 只使用 `plugins.enabled`、`plugins.installed.*`、`plugins.marketplace_trust.*`、`plugins.remote_marketplaces.*` 等正式路径；旧 `features.plugins`、`experimentalFeatures.plugins`、`plugins.marketplaceTrust.*`、`plugins.remoteMarketplaces.*` 不得作为正式输入。
- plugin manifest / marketplace JSON 属于外部 manifest bridge，可保留 `mcpServers`、`envVars`、`installUrl`、`installPolicy` 等 JSON camelCase surface，但不得把这些字段提升为 `tianshu.toml` schema 输入。
- `apps` 输出 app connector descriptor、启用状态和 secret reference，不直接发起外部 connector 调用。
- `memory` 输出 Memory / Identity Module 的 capability facts，不直接改写长期记忆。
- `diagnostics` 输出 trace、audit、redaction、sink facts。
- `workspace` 输出 workspace facts 和 trust facts，不执行文件变更。
- `experience` 只影响界面显示和 Host operation 默认值。

## 10. 当前落地收口

35 起，Config Plane 的可验收实现口径如下：

| 口径 | 实现项目 | 验收要求 |
| --- | --- | --- |
| typed contract | `src/Contracts/TianShu.Contracts.Configuration` | `ConfigurationProjection`、`ConfigurationSourceLayer`、`ConfigurationIssue`、`ConfigurationChangePreview`、`ConfigurationApplyResult`、`ConfigurationFacts` 是唯一对外配置契约；Host Gateway / ConfigGUI 不消费运行时私有配置对象。 |
| schema catalog | `src/Core/TianShu.Configuration` | `TianShuConfigurationSchemaCatalog` 必须覆盖 `host`、`control`、`kernel`、`execution`、`modules`、`providers`、`tools`、`plugins/apps`、`memory`、`diagnostics`、`workspace`、`experience` 的代表字段；通配字段必须投影为具体 key。 |
| source layer | `src/Core/TianShu.Configuration`、`src/Hosting/TianShu.AppHost.Configuration` | `tianshu.toml`、用户模块 TOML、项目 TOML、request/session override 与 `TIANSHU_CONFIG__...` 环境变量必须以 source layer 进入投影；非 `TIANSHU_CONFIG__` 环境变量只能作为 secret reference 的外部值来源。 |
| fail-closed diagnostics | `src/Core/TianShu.Configuration` | 非法枚举、非法预算、互斥 secret reference、空 secret reference、疑似明文 secret、缺失必填 module descriptor 都必须输出 error issue；未登记字段必须输出 `raw.unmapped` / `config.field.unmapped`。 |
| formal facts | `src/Core/TianShu.Configuration` | `TianShuConfigurationFactsBuilder` 只能从正式 schema 字段生成 `KernelConfigurationFacts`、`ExecutionConfigurationFacts` 和 `ModuleConfigurationFacts`；`raw.unmapped` 字段必须以 `config.facts.unmapped_rejected` 记录，不能进入运行时事实。 |
| module write routing | `src/Core/TianShu.Configuration` | `TianShuConfigurationTomlChangeApplier.ApplyRouted` 必须把 route set、protocol rule set、provider instance 等模块归属配置写入 `modules/**`，而不是把模块私有内容压回 `tianshu.toml`。 |
| ConfigGUI | `src/Presentations/TianShu.ConfigGui` | 只能通过 `TianShuConfigurationTomlProjectionLoader` 和 `TianShuConfigurationTomlChangeApplier` 读取/预览/应用正式 schema 字段；`raw.unmapped` 不可编辑。 |
| AppHost.Configuration | `src/Hosting/TianShu.AppHost.Configuration` | 只能加载、解析、投影和迁移配置；生产项目不得引用 Kernel、Execution Runtime、Provider probe、Tool bridge、StageGraph、RuntimeStep 或 context slicing 决策类型。 |

默认安装模块和 CLI 首次运行 bootstrap 必须生成公开、中性、无 secret 的模型配置，而不是只生成单 provider 或 `auto` 占位。`tools/Install-TianShuCli.ps1` 与 `tianshu init` 的 `modules/model/provider-instances/default.toml`、`modules/model/route-sets/default.toml` 与 `modules/model/protocol-rules/default.toml` 必须至少覆盖以下非 Google 路径：

- OpenAI Responses：`openai` / `gpt-5.5` / `openai_responses`，secret reference 为 `OPENAI_API_KEY`。
- Anthropic Messages：`anthropic` / `claude-opus-4.8` / `anthropic_messages`，secret reference 为 `ANTHROPIC_API_KEY`。
- OpenAI Chat Completions-compatible：`openai-compatible` / `openai-compatible-default` / `openai_chat_completions`，secret reference 为 `OPENAI_COMPATIBLE_API_KEY`。默认 `base_url` 只能使用公开中性 endpoint；用户可在本地配置中替换为自己的 OpenAI-compatible endpoint。

Google provider adapter 可以继续作为模块安装，但默认 live 矩阵暂不把 `google_generative` 作为阻塞路径；后续若重新纳入，必须先补稳定模型、route、protocol rule、provider instance 和 live evidence。

旧字段处理固定为：`modelProvider`、`mcpServers`、`apiKey`、`enabledTools` 等旧别名或 camelCase 字段不得成为 `tianshu.toml` 正式执行输入。若这些字段出现在用户主配置或 override 中，只能进入未映射诊断；若它们属于外部 plugin / marketplace / manifest JSON，则只在对应 manifest bridge 内保留，不提升为 Config Plane schema。

## 11. 测试验收标准

- `TianShu.Contracts.Configuration` 必须包含 `kernel`、`execution`、`modules` 的 typed schema/projection contract。
- `TianShu.Configuration` schema catalog 必须暴露上述目标字段族，并对未知字段输出 `raw.unmapped` 诊断。
- 配置解析必须 fail closed：非法枚举、非法预算、缺失必填 secret reference、互斥字段冲突必须产生 error issue。
- ConfigGUI 必须只编辑 schema catalog 中的正式字段，并通过 preview/apply 写入。
- AppHost.Configuration 不得包含 StageGraph 解释、RuntimeStep 生成、provider 调用、tool 授权或 context slicing 决策。
- Provider、Tool、Execution、Kernel 测试不得从旧配置字段或未映射字段读取正式执行输入。
- 安装脚本必须保留既有 `tianshu.toml` 和模块配置内容；Prompt 的当前 source-of-truth 是 `modules/prompts/<package>/prompt.toml`。历史 `default_prompt.toml` / `prompt/` 只能作为迁移输入或忽略对象，不得再写入当前安装保留契约。
