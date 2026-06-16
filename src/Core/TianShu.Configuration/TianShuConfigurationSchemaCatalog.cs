using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;

namespace TianShu.Configuration;

/// <summary>
/// TianShu 配置 schema catalog 的只读入口。
/// Read-only schema catalog entry point for TianShu configuration.
/// </summary>
public interface ITianShuConfigurationSchemaCatalog
{
    ConfigurationSchemaCatalogSnapshot GetSnapshot();
}

/// <summary>
/// 配置 schema catalog 快照。
/// Snapshot of the configuration schema catalog.
/// </summary>
public sealed record ConfigurationSchemaCatalogSnapshot
{
    public IReadOnlyList<ConfigurationCategoryDescriptor> Categories { get; init; } = Array.Empty<ConfigurationCategoryDescriptor>();

    public IReadOnlyList<ConfigurationGroupDescriptor> Groups { get; init; } = Array.Empty<ConfigurationGroupDescriptor>();

    public IReadOnlyList<ConfigurationFieldDescriptor> Fields { get; init; } = Array.Empty<ConfigurationFieldDescriptor>();
}

/// <summary>
/// TianShu 原生配置的 typed schema catalog。
/// Typed schema catalog for TianShu native configuration.
/// </summary>
public sealed class TianShuConfigurationSchemaCatalog : ITianShuConfigurationSchemaCatalog
{
    public const string RawUnmappedGroupId = "raw.unmapped";

    private static readonly ConfigurationSchemaCatalogSnapshot Snapshot = new()
    {
        Categories =
        [
            Category(ConfigurationCategoryIds.Foundation, ConfigurationCategoryKind.Foundation, "基础入口", "应用入口、profile、默认 prompt 和 schema 版本。", 0),
            Category(ConfigurationCategoryIds.ConnectivityModel, ConfigurationCategoryKind.ConnectivityModel, "连接模型", "模型、provider、endpoint、协议与推理意图。", 10),
            Category(ConfigurationCategoryIds.AgentBehavior, ConfigurationCategoryKind.AgentBehavior, "Agent 行为", "代理角色、会话行为、上下文与自动化策略。", 20),
            Category(ConfigurationCategoryIds.SecurityGovernance, ConfigurationCategoryKind.SecurityGovernance, "安全治理", "审批、权限、沙箱与风险策略。", 30),
            Category(ConfigurationCategoryIds.CapabilitiesTools, ConfigurationCategoryKind.CapabilitiesTools, "能力工具", "工具、MCP、技能、插件和应用能力开关。", 40),
            Category(ConfigurationCategoryIds.IdentityMemory, ConfigurationCategoryKind.IdentityMemory, "记忆身份", "身份、账户、设备、记忆 profile 与长期个体化。", 50),
            Category(ConfigurationCategoryIds.Workspace, ConfigurationCategoryKind.Workspace, "工作空间", "项目、cwd、会话、协作空间与线程生命周期。", 60),
            Category(ConfigurationCategoryIds.DiagnosticsState, ConfigurationCategoryKind.DiagnosticsState, "诊断状态", "诊断、artifact、state、日志和审计采集。", 70),
            Category(ConfigurationCategoryIds.Experience, ConfigurationCategoryKind.Experience, "界面体验", "CLI/TUI/GUI 展示、实时交互、review 与 feedback。", 80),
            Category(ConfigurationCategoryIds.KernelCore, ConfigurationCategoryKind.KernelCore, "Kernel Core", "StageGraph 选择、自适应编排、策略生命周期与 Kernel 预算。", 90),
            Category(ConfigurationCategoryIds.ExecutionRuntime, ConfigurationCategoryKind.ExecutionRuntime, "Execution Runtime", "RuntimeStep 超时、重试、并发、trace 和 side effect 边界。", 100),
            Category(ConfigurationCategoryIds.ModulePlane, ConfigurationCategoryKind.ModulePlane, "Module Plane", "模块发现、descriptor、trust、capability 与 health check。", 110),
            Category(ConfigurationCategoryIds.ExtensionsImports, ConfigurationCategoryKind.ExtensionsImports, "扩展导入", "外部配置导入、扩展源和未映射配置项。", 120),
        ],
        Groups =
        [
            Group("app", ConfigurationCategoryIds.Foundation, "应用入口", "TianShu 根配置与默认入口。", 0),
            Group("prompt", ConfigurationCategoryIds.Foundation, "Prompt 配置", "默认 prompt 文件与 profile。", 10),
            Group("profile", ConfigurationCategoryIds.Foundation, "Profile 引用", "默认 profile 与各能力 profile 的组合引用。", 20),
            Group("model", ConfigurationCategoryIds.ConnectivityModel, "模型选择", "默认模型、provider 与协议解析入口。", 0),
            Group("provider", ConfigurationCategoryIds.ConnectivityModel, "Provider 连接", "provider endpoint、鉴权引用与连接协议。", 10),
            Group("model_route_set", ConfigurationCategoryIds.ConnectivityModel, "模型路由方案", "Model Route Set、route 候选顺序、模型族、上下文窗口与 reasoning 默认值。", 20),
            Group("agent", ConfigurationCategoryIds.AgentBehavior, "Agent 行为", "默认 agent、上下文预算与会话行为。", 0),
            Group("execution", ConfigurationCategoryIds.AgentBehavior, "执行 Profile", "执行入口、流式超时、工具并发与 web search。", 10),
            Group("session", ConfigurationCategoryIds.AgentBehavior, "会话行为", "会话模式、模型绑定、恢复、压缩与线程默认值。", 20),
            Group("conversation", ConfigurationCategoryIds.AgentBehavior, "对话行为", "历史、文件搜索与待补录输入策略。", 30),
            Group("security", ConfigurationCategoryIds.SecurityGovernance, "安全治理", "审批策略、权限 profile 与沙箱模式。", 0),
            Group("permission", ConfigurationCategoryIds.SecurityGovernance, "权限 Profile", "权限 profile、工具规则与执行许可。", 10),
            Group("governance", ConfigurationCategoryIds.SecurityGovernance, "治理交互", "审批队列、用户补录与风险确认策略。", 20),
            Group("sandbox", ConfigurationCategoryIds.SecurityGovernance, "沙箱", "沙箱模式、网络和可读写根。", 30),
            Group("tools", ConfigurationCategoryIds.CapabilitiesTools, "能力工具", "工具、MCP、技能和插件开关。", 0),
            Group("mcp", ConfigurationCategoryIds.CapabilitiesTools, "MCP", "MCP 总开关、注册表与 server 连接。", 10),
            Group("skills", ConfigurationCategoryIds.CapabilitiesTools, "技能", "技能根目录、远程技能与技能 profile。", 20),
            Group("plugins_apps", ConfigurationCategoryIds.CapabilitiesTools, "插件应用", "插件、应用和 connector 能力。", 30),
            Group("identity_memory", ConfigurationCategoryIds.IdentityMemory, "记忆身份", "身份 profile、账户、设备与记忆策略。", 0),
            Group("memory", ConfigurationCategoryIds.IdentityMemory, "记忆系统", "记忆 profile、space、provider 与 binding。", 10),
            Group("workspace", ConfigurationCategoryIds.Workspace, "工作空间", "项目信任、会话目录与工作区策略。", 0),
            Group("collaboration", ConfigurationCategoryIds.Workspace, "协作空间", "协作和 workflow profile 默认值。", 10),
            Group("state", ConfigurationCategoryIds.DiagnosticsState, "状态存储", "state、artifact 和本地持久化策略。", 0),
            Group("diagnostics", ConfigurationCategoryIds.DiagnosticsState, "诊断状态", "诊断采集级别、artifact 与 telemetry。", 10),
            Group("runtime", ConfigurationCategoryIds.DiagnosticsState, "宿主运行时", "host、runtime 和正式 runtime surface。", 20),
            Group("experience", ConfigurationCategoryIds.Experience, "界面体验", "CLI/TUI/GUI 体验开关。", 0),
            Group("feature", ConfigurationCategoryIds.Experience, "功能开关", "feature、realtime、review 与 feedback。", 10),
            Group("kernel", ConfigurationCategoryIds.KernelCore, "Kernel", "Kernel 启用状态和默认 StageGraph。", 0),
            Group("kernel_adaptive", ConfigurationCategoryIds.KernelCore, "Adaptive Orchestration", "自适应编排开关与 Kernel tool 边界。", 10),
            Group("kernel_strategy", ConfigurationCategoryIds.KernelCore, "Strategy Lifecycle", "策略注册表、晋升门禁与试运行预算。", 20),
            Group("kernel_budget", ConfigurationCategoryIds.KernelCore, "Kernel 预算", "Kernel 级 token、时间、成本、重试和工具调用预算。", 30),
            Group("kernel_validation", ConfigurationCategoryIds.KernelCore, "Kernel 校验", "Stable Kernel Core 的 fail-closed 与 envelope 要求。", 40),
            Group("execution_runtime", ConfigurationCategoryIds.ExecutionRuntime, "Execution Runtime", "RuntimeStep profile、超时、重试、trace 和 side effect ceiling。", 0),
            Group("modules", ConfigurationCategoryIds.ModulePlane, "Modules", "模块发现、启用状态、descriptor、trust、capability 和 health check。", 0),
            Group("imports", ConfigurationCategoryIds.ExtensionsImports, "扩展导入", "外部配置导入和扩展源。", 0),
            Group("requirements", ConfigurationCategoryIds.ExtensionsImports, "配置约束", "requires 策略与配置合规约束。", 10),
            Group(RawUnmappedGroupId, ConfigurationCategoryIds.ExtensionsImports, "未映射配置", "schema catalog 尚未覆盖但已在配置文件中出现的键。", 1000),
        ],
        Fields =
        [
            Field("schema_version", "app", "Schema 版本", ConfigurationValueKind.Integer, "配置 schema 主版本。", StructuredValue.FromNumber("1"), isAdvanced: false),
            Field("profile", "app", "当前配置方案", ConfigurationValueKind.String, "选择当前默认启用的配置方案；未设置时使用根级默认配置。"),
            Field("app.name", "app", "应用名称", ConfigurationValueKind.String, "当前 TianShu 实例展示名称。", StructuredValue.FromString("TianShu")),
            Field("app.instance_id", "app", "实例 ID", ConfigurationValueKind.String, "本机或本 TianShu 实例标识。", StructuredValue.FromString("local"), isAdvanced: true),
            Field("app.locale", "app", "界面语言", ConfigurationValueKind.String, "默认语言与区域。", StructuredValue.FromString("zh-CN")),
            Field("app.telemetry", "app", "遥测模式", ConfigurationValueKind.Enum, "应用级遥测默认模式。", StructuredValue.FromString("local"), allowedValues: ["off", "local", "remote"]),
            Field("profiles.*.description", "profile", "Profile 说明", ConfigurationValueKind.String, "profile 的人类可读说明。"),
            Field("profiles.*.extends", "profile", "继承 Profile", ConfigurationValueKind.String, "当前 profile 继承的基线 profile。", isAdvanced: true),
            Field("profiles.*.agent", "profile", "Agent Profile", ConfigurationValueKind.String, "引用的 agent id。"),
            Field("profiles.*.execution", "profile", "执行 Profile", ConfigurationValueKind.String, "引用的 execution profile。"),
            Field("profiles.*.conversation", "profile", "对话配置文件", ConfigurationValueKind.String, "引用的 conversation profile。"),
            Field("profiles.*.permissions", "profile", "权限 Profile", ConfigurationValueKind.String, "引用的 permission profile。"),
            Field("profiles.*.model_route_set", "profile", "模型路由方案", ConfigurationValueKind.String, "引用的模型路由方案配置文件。"),
            Field("profiles.*.memory", "profile", "记忆 Profile", ConfigurationValueKind.String, "引用的 memory profile。"),
            Field("profiles.*.tools", "profile", "工具 Profile", ConfigurationValueKind.String, "引用的 tool profile。"),
            Field("profiles.*.tui", "profile", "TUI Profile", ConfigurationValueKind.String, "引用的 TUI profile。"),
            Field("profiles.*.workspace", "profile", "工作空间 Profile", ConfigurationValueKind.String, "引用的 workspace profile。"),
            Field("profiles.*.session", "profile", "会话 Profile", ConfigurationValueKind.String, "引用的 session profile。"),
            Field("profiles.*.collaboration", "profile", "协作 Profile", ConfigurationValueKind.String, "引用的 collaboration profile。"),
            Field("profiles.*.workflow", "profile", "工作流 Profile", ConfigurationValueKind.String, "引用的 workflow profile。"),
            Field("profiles.*.identity", "profile", "身份 Profile", ConfigurationValueKind.String, "引用的 identity profile。"),
            Field("profiles.*.governance", "profile", "治理 Profile", ConfigurationValueKind.String, "引用的 governance profile。"),
            Field("profiles.*.features", "profile", "功能 Profile", ConfigurationValueKind.String, "引用的 feature profile。"),
            Field("profiles.*.realtime", "profile", "Realtime Profile", ConfigurationValueKind.String, "引用的 realtime profile。"),
            Field("profiles.*.stages.planning", "profile", "规划阶段配置方案", ConfigurationValueKind.String, "规划阶段引用的配置方案。"),
            Field("profiles.*.stages.execution", "profile", "执行阶段配置方案", ConfigurationValueKind.String, "执行阶段引用的配置方案。"),
            Field("profiles.*.stages.review", "profile", "审阅阶段配置方案", ConfigurationValueKind.String, "审阅阶段引用的配置方案。"),
            Field("profiles.*.stages.summary", "profile", "总结阶段配置方案", ConfigurationValueKind.String, "总结阶段引用的配置方案。"),
            Field("model", "model", "默认模型", ConfigurationValueKind.String, "新会话默认模型。"),
            Field("provider", "model", "默认 Provider", ConfigurationValueKind.String, "新会话默认 provider id。"),
            Field("model_route_set", "model_route_set", "默认模型路由", ConfigurationValueKind.String, "根级默认模型路由方案 id；默认 route set 内容由 model/route-sets/<id>.toml 模块层叠提供，未被配置文件、agent、execution 或 session 覆盖时进入会话 resolved snapshot。"),
            Field("model_protocol_rule_set", "model_route_set", "默认协议规则集", ConfigurationValueKind.String, "根级默认模型协议规则集 id；默认内容由 model-protocol-rules/<id>.toml 模块层叠提供。"),
            Field("provider_instances", "provider", "Provider 实例配置", ConfigurationValueKind.String, "根级默认 Provider instance 配置集 id；默认内容由 provider-instances/<id>.toml 模块层叠提供。", StructuredValue.FromString("default")),
            Field("model_protocol_rule_sets.*.display_name", "model_route_set", "协议规则集显示名", ConfigurationValueKind.String, "模型协议规则集在人类界面中的展示名。"),
            Field("model_protocol_rule_sets.*.description", "model_route_set", "协议规则集说明", ConfigurationValueKind.String, "模型协议规则集的说明。"),
            Field("model_protocol_rule_sets.*.rules", "model_route_set", "默认协议规则", ConfigurationValueKind.Array, "跨 provider 的默认模型通配到 wire protocol 优先级规则；provider model_overrides 与 protocol_rules 显式命中时优先于该规则集。"),
            Field("stage_registry.enabled", "model_route_set", "Stage Registry 扩展开关", ConfigurationValueKind.Boolean, "是否启用受信配置声明的扩展 Stage。", StructuredValue.FromBoolean(true), isAdvanced: true),
            Field("stage_registry.stages", "model_route_set", "扩展 Stage 定义", ConfigurationValueKind.Array, "受信扩展 StageDefinition 列表；每个 Stage 必须声明 id 与 lifecycle_order，model_route_kind 默认等于 id。", isAdvanced: true),
            Field("stage_registry.stages.*.id", "model_route_set", "扩展 Stage ID", ConfigurationValueKind.String, "扩展 Stage 的生命周期标识。", isAdvanced: true),
            Field("stage_registry.stages.*.enabled", "model_route_set", "启用扩展 Stage", ConfigurationValueKind.Boolean, "是否启用该扩展 Stage。", StructuredValue.FromBoolean(true), isAdvanced: true),
            Field("stage_registry.stages.*.display_name", "model_route_set", "扩展 Stage 显示名", ConfigurationValueKind.String, "扩展 Stage 的展示名称。", isAdvanced: true),
            Field("stage_registry.stages.*.lifecycle_order", "model_route_set", "扩展 Stage 顺序", ConfigurationValueKind.Integer, "扩展 Stage 的生命周期排序值。", isAdvanced: true),
            Field("stage_registry.stages.*.model_route_kind", "model_route_set", "扩展 Stage 模型路由", ConfigurationValueKind.String, "该 Stage 绑定的模型 route kind；未配置时使用 Stage id。", isAdvanced: true),
            Field("stage_registry.stages.*.allowed_previous", "model_route_set", "扩展 Stage 上游", ConfigurationValueKind.Array, "允许跳转到该 Stage 的上游 Stage id 列表。", isAdvanced: true),
            Field("stage_registry.stages.*.allowed_next", "model_route_set", "扩展 Stage 下游", ConfigurationValueKind.Array, "该 Stage 可跳转到的下游 Stage id 列表。", isAdvanced: true),
            Field("stage_registry.stages.*.required_capabilities", "model_route_set", "扩展 Stage 能力", ConfigurationValueKind.Array, "该 Stage 对模型候选的能力要求。", isAdvanced: true),
            Field("stage_registry.stages.*.context_projection_mode", "model_route_set", "扩展 Stage 上下文投影", ConfigurationValueKind.Enum, "该 Stage 的上下文投影模式。", allowedValues: ["summary", "selected_segments", "full_within_budget", "references_only"], isAdvanced: true),
            Field("stage_registry.stages.*.executor_binding", "model_route_set", "扩展 Stage 执行器", ConfigurationValueKind.String, "该 Stage 绑定的执行器标识。", isAdvanced: true),
            Field("kernel.enabled", "kernel", "启用 Kernel", ConfigurationValueKind.Boolean, "是否启用正式 Kernel Core。", StructuredValue.FromBoolean(true)),
            Field("kernel.default_graph_id", "kernel", "默认 StageGraph", ConfigurationValueKind.String, "Kernel 默认选择的内置或受控 StageGraph id。"),
            Field("kernel.graph_sets.*", "kernel", "StageGraph 集合", ConfigurationValueKind.Object, "受控 StageGraph 集合定义；配置只提供候选事实，最终解释由 Kernel 完成。", isAdvanced: true),
            Field("kernel.adaptive.enabled", "kernel_adaptive", "启用自适应编排", ConfigurationValueKind.Boolean, "是否允许 Adaptive Orchestration Layer 生成受控 Kernel proposal。", StructuredValue.FromBoolean(false)),
            Field("kernel.adaptive.allowed_kernel_tools", "kernel_adaptive", "允许的 Kernel Tool", ConfigurationValueKind.Array, "编排模型可请求的 Kernel tool id 列表。"),
            Field("kernel.adaptive.max_proposals_per_turn", "kernel_adaptive", "每轮最大提案数", ConfigurationValueKind.Integer, "单轮允许生成的 Kernel proposal 数量上限。", StructuredValue.FromNumber("4")),
            Field("kernel.strategy.default_registry", "kernel_strategy", "策略注册表", ConfigurationValueKind.String, "Kernel 策略注册表 id。"),
            Field("kernel.strategy.promotion_gate", "kernel_strategy", "策略晋升门禁", ConfigurationValueKind.Enum, "高风险策略晋升的人机门禁要求。", StructuredValue.FromString("human_required"), allowedValues: ["disabled", "human_required", "policy_required"]),
            Field("kernel.strategy.trial_runs", "kernel_strategy", "策略试运行次数", ConfigurationValueKind.Integer, "策略晋升前要求的试运行次数。", StructuredValue.FromNumber("3")),
            Field("kernel.budget.token_budget", "kernel_budget", "Token 预算", ConfigurationValueKind.Integer, "Kernel 单次 run 的 token 上限；只能收紧执行边界。"),
            Field("kernel.budget.time_budget_ms", "kernel_budget", "时间预算", ConfigurationValueKind.Integer, "Kernel 单次 run 的时间预算，单位毫秒。"),
            Field("kernel.budget.cost_budget", "kernel_budget", "成本预算", ConfigurationValueKind.Number, "Kernel 单次 run 的成本预算。"),
            Field("kernel.budget.retry_budget", "kernel_budget", "重试预算", ConfigurationValueKind.Integer, "Kernel 可消耗的重试次数预算。"),
            Field("kernel.budget.tool_call_budget", "kernel_budget", "工具调用预算", ConfigurationValueKind.Integer, "Kernel 可消耗的 tool call 次数预算。"),
            Field("kernel.validation.fail_closed", "kernel_validation", "Fail Closed", ConfigurationValueKind.Boolean, "配置或治理缺口出现时是否 fail closed。", StructuredValue.FromBoolean(true)),
            Field("kernel.validation.require_governance_envelope", "kernel_validation", "要求 Governance Envelope", ConfigurationValueKind.Boolean, "Kernel 是否必须持有 GovernanceEnvelope 才能生成执行计划。", StructuredValue.FromBoolean(true)),
            Field("kernel.validation.require_trace_policy", "kernel_validation", "要求 Trace Policy", ConfigurationValueKind.Boolean, "Kernel 是否必须持有 TracePolicy。", StructuredValue.FromBoolean(true)),
            Field("execution.default_profile", "execution_runtime", "默认 Runtime Profile", ConfigurationValueKind.String, "Execution Runtime 默认 profile id。", StructuredValue.FromString("default")),
            Field("execution.profiles.*.timeout_ms", "execution_runtime", "Step 超时", ConfigurationValueKind.Integer, "RuntimeStep 默认超时时间，单位毫秒。"),
            Field("execution.profiles.*.stream_idle_timeout_ms", "execution_runtime", "流式空闲超时", ConfigurationValueKind.Integer, "RuntimeStep 流式输出空闲超时，单位毫秒。"),
            Field("execution.profiles.*.retry_budget", "execution_runtime", "重试预算", ConfigurationValueKind.Integer, "RuntimeStep profile 的重试预算。"),
            Field("execution.profiles.*.max_parallelism", "execution_runtime", "最大并发", ConfigurationValueKind.Integer, "Execution Runtime 并发上限。", StructuredValue.FromNumber("1")),
            Field("execution.profiles.*.require_source_ids", "execution_runtime", "要求来源 ID", ConfigurationValueKind.Boolean, "RuntimeStep 是否必须携带 SourceGraphId、SourceStageId 和 SourceKernelOperationId。", StructuredValue.FromBoolean(true)),
            Field("execution.profiles.*.require_permission_envelope", "execution_runtime", "要求 Permission Envelope", ConfigurationValueKind.Boolean, "RuntimeStep 是否必须携带 PermissionEnvelope。", StructuredValue.FromBoolean(true)),
            Field("execution.profiles.*.require_trace_policy", "execution_runtime", "要求 Trace Policy", ConfigurationValueKind.Boolean, "RuntimeStep 是否必须携带 TracePolicy。", StructuredValue.FromBoolean(true)),
            Field("execution.profiles.*.diagnostics_ref_required", "execution_runtime", "要求 Diagnostics Ref", ConfigurationValueKind.Boolean, "RuntimeStep 结果是否必须关联 diagnostics ref。", StructuredValue.FromBoolean(true)),
            Field("execution.profiles.*.runtime_trace_ref_required", "execution_runtime", "要求 Runtime Trace Ref", ConfigurationValueKind.Boolean, "RuntimeStep 结果是否必须关联 runtime trace ref。", StructuredValue.FromBoolean(true)),
            Field("execution.profiles.*.side_effect_ceiling", "execution_runtime", "Side Effect 上限", ConfigurationValueKind.Enum, "RuntimeStep profile 允许的最大 side effect。", StructuredValue.FromString("read_only"), allowedValues: ["none", "read_only", "workspace_write", "network", "external_write"]),
            Field("modules.discovery_roots", "modules", "模块发现根", ConfigurationValueKind.Array, "Module Plane 可扫描的模块根目录。"),
            Field("modules.providers.*.enabled", "modules", "Provider Module 启用", ConfigurationValueKind.Boolean, "Provider module 是否启用。", StructuredValue.FromBoolean(true)),
            Field("modules.providers.*.descriptor_ref", "modules", "Provider Descriptor 引用", ConfigurationValueKind.String, "Provider module descriptor 引用。"),
            Field("modules.tools.*.enabled", "modules", "Tool Module 启用", ConfigurationValueKind.Boolean, "Tool module 是否启用。", StructuredValue.FromBoolean(true)),
            Field("modules.tools.*.descriptor_ref", "modules", "Tool Descriptor 引用", ConfigurationValueKind.String, "Tool module descriptor 引用。"),
            Field("modules.memory.*.enabled", "modules", "Memory Module 启用", ConfigurationValueKind.Boolean, "Memory module 是否启用。", StructuredValue.FromBoolean(true)),
            Field("modules.memory.*.descriptor_ref", "modules", "Memory Descriptor 引用", ConfigurationValueKind.String, "Memory module descriptor 引用。"),
            Field("modules.artifacts.*.enabled", "modules", "Artifact Module 启用", ConfigurationValueKind.Boolean, "Artifact module 是否启用。", StructuredValue.FromBoolean(true)),
            Field("modules.artifacts.*.descriptor_ref", "modules", "Artifact Descriptor 引用", ConfigurationValueKind.String, "Artifact module descriptor 引用。"),
            Field("modules.diagnostics.*.enabled", "modules", "Diagnostics Module 启用", ConfigurationValueKind.Boolean, "Diagnostics module 是否启用。", StructuredValue.FromBoolean(true)),
            Field("modules.diagnostics.*.descriptor_ref", "modules", "Diagnostics Descriptor 引用", ConfigurationValueKind.String, "Diagnostics module descriptor 引用。"),
            Field("modules.workspace.*.enabled", "modules", "Workspace Module 启用", ConfigurationValueKind.Boolean, "Workspace module 是否启用。", StructuredValue.FromBoolean(true)),
            Field("modules.workspace.*.descriptor_ref", "modules", "Workspace Descriptor 引用", ConfigurationValueKind.String, "Workspace module descriptor 引用。"),
            Field("modules.*.trust_level", "modules", "模块 Trust Level", ConfigurationValueKind.Enum, "模块 trust level；配置只能提供事实，治理 envelope 仍可进一步收紧。", allowedValues: ["trusted", "workspace", "prompt", "untrusted", "blocked"]),
            Field("modules.*.capabilities", "modules", "模块能力集合", ConfigurationValueKind.Array, "模块声明或允许的 capability id 列表。"),
            Field("modules.*.health_check", "modules", "模块健康检查", ConfigurationValueKind.String, "模块 health check 策略或 endpoint 引用。"),
            Field("providers.*.display_name", "provider", "Provider 显示名", ConfigurationValueKind.String, "provider 在选择器中的展示名。"),
            Field("providers.*.kind", "provider", "Provider 类型", ConfigurationValueKind.String, "provider adapter id。"),
            Field("providers.*.transport", "provider", "Provider 传输", ConfigurationValueKind.Enum, "provider 传输方式。", StructuredValue.FromString("http"), allowedValues: ["http", "websocket", "stdio", "sidecar"]),
            Field("providers.*.base_url", "provider", "Provider 根地址", ConfigurationValueKind.String, "provider 服务根地址或兼容 endpoint。"),
            Field("providers.*.api_key_env", "provider", "API Key 环境变量", ConfigurationValueKind.SecretReference, "保存 API key 的环境变量名。", editMode: ConfigurationFieldEditMode.SecretReferenceOnly),
            Field("providers.*.api_key_secret", "provider", "API Key 凭据引用", ConfigurationValueKind.SecretReference, "系统凭据或 secret provider 引用，不保存明文。", editMode: ConfigurationFieldEditMode.SecretReferenceOnly),
            Field("providers.*.organization_env", "provider", "组织 ID 环境变量", ConfigurationValueKind.SecretReference, "保存组织 id 的环境变量名。", editMode: ConfigurationFieldEditMode.SecretReferenceOnly),
            Field("providers.*.default_protocol", "provider", "默认协议", ConfigurationValueKind.Enum, "provider 默认 wire protocol。", allowedValues: ["auto", "openai_responses", "openai_chat_completions", "anthropic_messages", "google_generative"]),
            Field("providers.*.protocol_fallbacks", "provider", "协议兜底顺序", ConfigurationValueKind.Array, "首选 protocol 不可用时的 provider 级兜底顺序。"),
            Field("providers.*.protocol_rules", "provider", "协议规则", ConfigurationValueKind.Array, "按模型通配匹配 protocol 的规则列表；推荐用 protocols 数组表达有序协议优先级。", isAdvanced: true),
            Field("providers.*.model_overrides", "provider", "模型协议覆写", ConfigurationValueKind.Array, "精确模型到 protocol 的覆写列表；推荐用 protocols 数组表达有序协议优先级。", isAdvanced: true),
            Field("providers.*.supports_streaming", "provider", "支持流式", ConfigurationValueKind.Boolean, "provider 是否支持流式输出。", StructuredValue.FromBoolean(true)),
            Field("providers.*.supports_websockets", "provider", "支持 WebSocket", ConfigurationValueKind.Boolean, "provider 是否支持 websocket。", StructuredValue.FromBoolean(false)),
            Field("providers.*.request_max_retries", "provider", "请求重试次数", ConfigurationValueKind.Integer, "普通请求最大重试次数。", StructuredValue.FromNumber("2")),
            Field("providers.*.stream_max_retries", "provider", "流式重试次数", ConfigurationValueKind.Integer, "流式请求最大重试次数。", StructuredValue.FromNumber("2")),
            Field("providers.*.stream_idle_timeout_ms", "provider", "流式空闲超时", ConfigurationValueKind.Integer, "流式响应空闲超时时间，单位毫秒。", StructuredValue.FromNumber("90000")),
            Field("providers.*.websocket_connect_timeout_ms", "provider", "WebSocket 连接超时", ConfigurationValueKind.Integer, "websocket 连接超时时间，单位毫秒。", StructuredValue.FromNumber("15000")),
            Field("providers.*.reasoning_activation", "provider", "推理映射策略", ConfigurationValueKind.Enum, "provider 默认 reasoning 映射策略。", StructuredValue.FromString("auto"), allowedValues: ["auto", "none", "implicit", "openai_responses", "anthropic_thinking", "google_thinking_config", "openai_compatible_extra_body", "provider_native"]),
            Field("providers.*.headers", "provider", "固定 Header", ConfigurationValueKind.Object, "非 secret 固定 header。", isAdvanced: true),
            Field("providers.*.reasoning.enabled", "provider", "Provider 推理开关", ConfigurationValueKind.Boolean, "是否为该 provider 启用 reasoning/thinking 意图。", StructuredValue.FromBoolean(true)),
            Field("providers.*.reasoning.effort", "provider", "Provider 推理强度", ConfigurationValueKind.Enum, "provider 默认 reasoning effort。", allowedValues: ["minimal", "low", "medium", "high", "xhigh"]),
            Field("providers.*.reasoning.summary", "provider", "Provider 推理摘要", ConfigurationValueKind.Enum, "provider 默认 reasoning summary。", allowedValues: ["off", "auto", "concise", "detailed"]),
            Field("providers.*.reasoning.verbosity", "provider", "Provider 输出详略", ConfigurationValueKind.Enum, "provider 默认 verbosity。", allowedValues: ["low", "normal", "medium", "high"]),
            Field("providers.*.reasoning.budget_tokens", "provider", "Provider 推理预算", ConfigurationValueKind.Integer, "provider 默认 thinking/reasoning token 预算。"),
            Field("providers.*.reasoning.protocol_rules", "provider", "Provider 推理协议规则", ConfigurationValueKind.Array, "provider reasoning 子配置下的模型通配到协议规则，用于表达特定模型族应如何映射 reasoning/thinking 协议意图；当前主请求 wire protocol 路由仍优先使用 providers.<id>.protocol_rules。", isAdvanced: true),
            Field("models.*.provider", "model_route_set", "模型 Provider", ConfigurationValueKind.String, "模型所属 provider。"),
            Field("models.*.name", "model_route_set", "模型原生名称", ConfigurationValueKind.String, "provider 原生模型名。"),
            Field("models.*.display_name", "model_route_set", "模型显示名", ConfigurationValueKind.String, "模型在人类界面中的显示名。"),
            Field("models.*.family", "model_route_set", "模型族", ConfigurationValueKind.String, "模型族或协议启发分类。"),
            Field("models.*.context_window", "model_route_set", "上下文窗口", ConfigurationValueKind.Integer, "模型上下文窗口大小。"),
            Field("models.*.default_reasoning_effort", "model_route_set", "默认推理强度", ConfigurationValueKind.Enum, "该模型默认 reasoning effort。", allowedValues: ["minimal", "low", "medium", "high", "xhigh"]),
            Field("models.*.default_reasoning_summary", "model_route_set", "默认推理摘要", ConfigurationValueKind.Enum, "该模型默认 reasoning summary。", allowedValues: ["off", "auto", "concise", "detailed"]),
            Field("models.*.default_verbosity", "model_route_set", "默认输出详略", ConfigurationValueKind.Enum, "该模型默认 verbosity。", allowedValues: ["low", "normal", "medium", "high"]),
            Field("models.*.reasoning_activation", "model_route_set", "模型推理策略", ConfigurationValueKind.Enum, "该模型 reasoning 激活策略。", allowedValues: ["none", "implicit", "openai_responses", "anthropic_thinking", "google_thinking_config", "openai_compatible_extra_body", "provider_native"]),
            Field("models.*.reasoning_budget_tokens", "model_route_set", "模型推理预算", ConfigurationValueKind.Integer, "该模型默认 thinking/reasoning token 预算。"),
            Field("models.*.protocols", "model_route_set", "模型协议优先级", ConfigurationValueKind.Array, "模型级 wire protocol 优先级；优先级低于 provider 覆写/规则，高于 provider 默认协议与内置启发式。", isAdvanced: true),
            Field("models.*.hidden", "model_route_set", "隐藏模型", ConfigurationValueKind.Boolean, "是否从普通选择器隐藏。", StructuredValue.FromBoolean(false)),
            Field("model_route_sets.*.display_name", "model_route_set", "路由方案显示名", ConfigurationValueKind.String, "模型路由方案在配置界面中的展示名。"),
            Field("model_route_sets.*.description", "model_route_set", "路由方案说明", ConfigurationValueKind.String, "模型路由方案的说明。"),
            Field("model_route_sets.*.routes", "model_route_set", "模型路由列表", ConfigurationValueKind.Array, "按用途保存的模型 route 数组；每个 route 内 candidates 数组顺序就是首选与 fallback 顺序。"),
            Field("agents.*.display_name", "agent", "Agent 显示名", ConfigurationValueKind.String, "agent 在消费侧展示的名称。"),
            Field("agents.*.model", "agent", "Agent 模型", ConfigurationValueKind.String, "指定 agent 的默认模型。"),
            Field("agents.*.provider", "agent", "Agent Provider", ConfigurationValueKind.String, "指定 agent 的默认 provider。"),
            Field("agents.*.model_route_set", "agent", "Agent 模型路由", ConfigurationValueKind.String, "指定 agent 默认使用的模型路由方案。"),
            Field("agents.*.personality", "agent", "Agent 风格", ConfigurationValueKind.String, "agent 的默认行为风格。"),
            Field("agents.*.system_prompt", "agent", "System Prompt 文件", ConfigurationValueKind.Path, "agent 主 system prompt 文件。"),
            Field("agents.*.instructions", "agent", "Instruction 文件", ConfigurationValueKind.Array, "agent 附加 instruction 文件列表。", isAdvanced: true),
            Field("agents.*.temperature", "agent", "采样温度", ConfigurationValueKind.Number, "agent 默认采样温度。"),
            Field("agents.*.max_output_tokens", "agent", "输出上限", ConfigurationValueKind.Integer, "单次响应输出 token 上限。"),
            Field("agents.*.reasoning.enabled", "agent", "Agent 推理开关", ConfigurationValueKind.Boolean, "是否向支持 provider 请求 reasoning/thinking 能力。", StructuredValue.FromBoolean(true)),
            Field("agents.*.reasoning.effort", "agent", "Agent 推理强度", ConfigurationValueKind.Enum, "agent 默认 reasoning effort。", allowedValues: ["minimal", "low", "medium", "high", "xhigh"]),
            Field("agents.*.reasoning.summary", "agent", "Agent 推理摘要", ConfigurationValueKind.Enum, "agent 默认 reasoning summary。", allowedValues: ["off", "auto", "concise", "detailed"]),
            Field("agents.*.reasoning.verbosity", "agent", "Agent 输出详略", ConfigurationValueKind.Enum, "agent 默认 verbosity。", allowedValues: ["low", "normal", "medium", "high"]),
            Field("agents.*.reasoning.budget_tokens", "agent", "Agent 推理预算", ConfigurationValueKind.Integer, "agent 默认 thinking/reasoning token 预算。"),
            Field("context.default_budget_tokens", "agent", "默认上下文预算", ConfigurationValueKind.Integer, "默认上下文 soft budget。", StructuredValue.FromNumber("50000")),
            Field("execution_profiles.*.provider", "execution", "执行 Provider", ConfigurationValueKind.String, "执行 profile 的默认 provider。"),
            Field("execution_profiles.*.agent", "execution", "执行 Agent", ConfigurationValueKind.String, "执行 profile 的默认 agent。"),
            Field("execution_profiles.*.model_route_set", "execution", "执行模型路由", ConfigurationValueKind.String, "执行配置文件默认使用的模型路由方案。"),
            Field("execution_profiles.*.approval", "execution", "执行审批", ConfigurationValueKind.Enum, "执行 profile 的默认审批策略。", allowedValues: ["never", "on-request", "on-failure", "always", "ask", "untrusted"]),
            Field("execution_profiles.*.sandbox", "execution", "执行沙箱", ConfigurationValueKind.String, "执行 profile 的默认 sandbox。"),
            Field("execution_profiles.*.service_tier", "execution", "服务层级", ConfigurationValueKind.String, "provider service tier。"),
            Field("execution_profiles.*.web_search", "execution", "Web Search", ConfigurationValueKind.Enum, "是否启用 web search。", allowedValues: ["off", "auto", "on"]),
            Field("execution_profiles.*.turn_timeout_seconds", "execution", "回合超时", ConfigurationValueKind.Integer, "单回合最大执行时间，单位秒。"),
            Field("execution_profiles.*.stream_idle_timeout_ms", "execution", "流式空闲超时", ConfigurationValueKind.Integer, "执行 profile 流式空闲超时时间，单位毫秒。"),
            Field("execution_profiles.*.parallel_tool_calls", "execution", "并行工具调用", ConfigurationValueKind.Boolean, "是否允许并行工具调用。", StructuredValue.FromBoolean(true)),
            Field("workspace_profiles.*.root_markers", "workspace", "根目录标记", ConfigurationValueKind.Array, "识别 workspace root 的标记文件或目录。"),
            Field("workspace_profiles.*.default_workspace", "workspace", "默认工作区", ConfigurationValueKind.Path, "默认 workspace 路径。"),
            Field("workspace_profiles.*.trust_policy", "workspace", "信任策略", ConfigurationValueKind.Enum, "工作区信任策略。", allowedValues: ["prompt", "trusted-only", "disabled"]),
            Field("workspace_profiles.*.artifact_root", "workspace", "Artifact 根目录", ConfigurationValueKind.Path, "工作区 artifact 根目录。"),
            Field("workspace_profiles.*.state_root", "workspace", "状态根目录", ConfigurationValueKind.Path, "工作区 state 根目录。"),
            Field("workspace_profiles.*.model", "workspace", "工作区模型", ConfigurationValueKind.String, "目录新会话默认模型；inherit 表示继承。"),
            Field("workspace_profiles.*.model_lock", "workspace", "工作区模型锁", ConfigurationValueKind.Enum, "目录新会话是否固化模型快照。", allowedValues: ["off", "snapshot-on-create"]),
            Field("projects.*.path", "workspace", "项目路径", ConfigurationValueKind.Path, "项目覆盖对应的路径；用于 ConfigGUI 创建项目覆盖时给出可编辑锚点。"),
            Field("projects.*.trust", "workspace", "项目信任", ConfigurationValueKind.Enum, "项目路径级信任状态。", allowedValues: ["trusted", "untrusted"]),
            Field("projects.*.profile", "workspace", "项目 Profile", ConfigurationValueKind.String, "项目路径级默认 profile。"),
            Field("projects.*.config_allowed", "workspace", "允许项目配置", ConfigurationValueKind.Boolean, "是否允许读取项目配置。"),
            Field("projects.*.model", "workspace", "项目模型", ConfigurationValueKind.String, "项目路径级新会话默认模型。"),
            Field("projects.*.model_lock", "workspace", "项目模型锁", ConfigurationValueKind.Enum, "项目路径级模型锁策略。", allowedValues: ["off", "snapshot-on-create"]),
            Field("session_profiles.*.mode", "session", "会话模式", ConfigurationValueKind.String, "会话 profile 的默认模式。"),
            Field("session_profiles.*.model", "session", "会话模型", ConfigurationValueKind.String, "会话 profile 默认模型。"),
            Field("session_profiles.*.model_route_set", "session", "会话模型路由", ConfigurationValueKind.String, "会话配置文件默认模型路由方案；创建会话时应固化到 resolved snapshot。"),
            Field("session_profiles.*.model_binding", "session", "模型绑定", ConfigurationValueKind.Enum, "会话模型绑定策略。", allowedValues: ["snapshot-on-create", "live-config"]),
            Field("session_profiles.*.model_change_scope", "session", "模型切换写入范围", ConfigurationValueKind.Enum, "会话内模型切换的写入边界。", allowedValues: ["session", "config-explicit"]),
            Field("session_profiles.*.memory_mode", "session", "会话记忆模式", ConfigurationValueKind.Enum, "会话记忆读写策略。", allowedValues: ["read-write", "read-only", "ephemeral", "disabled"]),
            Field("session_profiles.*.auto_resume", "session", "自动恢复", ConfigurationValueKind.Enum, "启动时恢复历史会话的策略。", allowedValues: ["ask", "never", "last"]),
            Field("session_profiles.*.default_collaboration", "session", "默认协作", ConfigurationValueKind.String, "默认 collaboration id。"),
            Field("session_profiles.*.default_thread_title", "session", "默认线程标题", ConfigurationValueKind.String, "新线程默认标题。"),
            Field("session_profiles.*.compact_after_turns", "session", "回合后压缩", ConfigurationValueKind.Integer, "超过多少回合后触发压缩；0 表示关闭。"),
            Field("conversation_profiles.*.thread_source", "conversation", "线程来源", ConfigurationValueKind.String, "对话线程来源标识。"),
            Field("conversation_profiles.*.history", "conversation", "历史策略", ConfigurationValueKind.Enum, "对话历史携带策略。", allowedValues: ["full", "sliced", "summary", "none"]),
            Field("conversation_profiles.*.fuzzy_file_search", "conversation", "模糊文件搜索", ConfigurationValueKind.Boolean, "是否启用模糊文件搜索。", StructuredValue.FromBoolean(true)),
            Field("conversation_profiles.*.pending_input_timeout_seconds", "conversation", "补录输入超时", ConfigurationValueKind.Integer, "用户补录输入等待时间，单位秒。"),
            Field("approval_policy", "security", "审批策略", ConfigurationValueKind.Enum, "默认审批策略。", allowedValues: ["never", "on-request", "on-failure", "always", "ask", "untrusted"]),
            Field("sandbox_mode", "security", "沙箱模式", ConfigurationValueKind.Enum, "默认沙箱模式。", allowedValues: ["read-only", "workspace-write", "danger-full-access"]),
            Field("permission_profiles.*.approval", "permission", "权限审批", ConfigurationValueKind.Enum, "permission profile 的默认审批策略。", allowedValues: ["never", "on-request", "on-failure", "always", "ask", "untrusted"]),
            Field("permission_profiles.*.sandbox", "permission", "权限沙箱", ConfigurationValueKind.String, "permission profile 的默认 sandbox。"),
            Field("permission_profiles.*.allow_network", "permission", "允许网络", ConfigurationValueKind.Boolean, "是否允许网络访问。"),
            Field("permission_profiles.*.allow_shell", "permission", "允许 Shell", ConfigurationValueKind.Boolean, "是否允许 shell 工具。"),
            Field("permission_profiles.*.allow_file_write", "permission", "允许写文件", ConfigurationValueKind.Boolean, "是否允许文件写入。"),
            Field("permission_profiles.*.allow_process_spawn", "permission", "允许启动进程", ConfigurationValueKind.Boolean, "是否允许启动子进程。"),
            Field("permission_profiles.*.rules", "permission", "权限规则", ConfigurationValueKind.Array, "工具级 allow/deny 规则列表。", isAdvanced: true),
            Field("governance_profiles.*.approval_queue", "governance", "审批队列", ConfigurationValueKind.Boolean, "是否启用审批队列。", StructuredValue.FromBoolean(true)),
            Field("governance_profiles.*.user_input_requests", "governance", "用户补录请求", ConfigurationValueKind.Boolean, "是否启用用户补录请求。", StructuredValue.FromBoolean(true)),
            Field("governance_profiles.*.risk_acknowledgement", "governance", "风险确认", ConfigurationValueKind.Enum, "风险确认策略。", allowedValues: ["ask", "never", "always"]),
            Field("governance_profiles.*.default_requested_from", "governance", "默认请求对象", ConfigurationValueKind.String, "治理交互默认请求用户。"),
            Field("sandboxes.*.mode", "sandbox", "沙箱模式", ConfigurationValueKind.Enum, "沙箱执行模式。", allowedValues: ["read-only", "workspace-write", "danger-full-access"]),
            Field("sandboxes.*.network", "sandbox", "沙箱网络", ConfigurationValueKind.Boolean, "沙箱是否允许网络。", StructuredValue.FromBoolean(false)),
            Field("sandboxes.*.writable_roots", "sandbox", "可写根", ConfigurationValueKind.Array, "沙箱允许写入的根路径。"),
            Field("sandboxes.*.readable_roots", "sandbox", "可读根", ConfigurationValueKind.Array, "沙箱允许读取的根路径。"),
            Field("sandboxes.*.exclude_tmpdir_env_var", "sandbox", "排除 TMPDIR", ConfigurationValueKind.Boolean, "是否排除 TMPDIR 环境变量路径。", StructuredValue.FromBoolean(false)),
            Field("sandboxes.*.exclude_slash_tmp", "sandbox", "排除 /tmp", ConfigurationValueKind.Boolean, "是否排除 /tmp。", StructuredValue.FromBoolean(false)),
            Field("tools.enabled", "tools", "启用工具", ConfigurationValueKind.Boolean, "是否启用工具能力。", StructuredValue.FromBoolean(true)),
            Field("tool_profiles.*.enabled", "tools", "启用工具集合", ConfigurationValueKind.Array, "该工具 profile 显式启用的工具集合。"),
            Field("tool_profiles.*.disabled", "tools", "禁用工具集合", ConfigurationValueKind.Array, "该工具 profile 显式禁用的工具集合。"),
            Field("tool_profiles.*.memory.enabled", "tools", "启用工具记忆", ConfigurationValueKind.Boolean, "该工具 profile 是否允许工具侧使用记忆能力。", StructuredValue.FromBoolean(false)),
            Field("tool_profiles.*.memory.default_profile", "tools", "工具记忆配置文件", ConfigurationValueKind.String, "该工具 profile 使用的默认 memory profile。"),
            Field("tools.*.enabled", "tools", "工具开关", ConfigurationValueKind.Boolean, "单个工具是否可用。", StructuredValue.FromBoolean(true)),
            Field("tools.*.provider", "tools", "工具 Provider", ConfigurationValueKind.String, "单个工具选择的 provider id。", isAdvanced: true),
            Field("tools.*.implementation_id", "tools", "工具实现 ID", ConfigurationValueKind.String, "单个工具选择的实现 id。", isAdvanced: true),
            Field("tools.*.implementation_kind", "tools", "工具实现类型", ConfigurationValueKind.Enum, "单个工具选择的实现类型。", allowedValues: ["managed", "externalprocess", "providerhosted", "mcpstdio", "mcphttp", "platformnative", "unavailable"], isAdvanced: true),
            Field("tools.*.priority", "tools", "工具实现优先级", ConfigurationValueKind.Integer, "同一工具多个实现的选择优先级。", StructuredValue.FromNumber("0"), isAdvanced: true),
            Field("tools.*.fallback", "tools", "工具 fallback", ConfigurationValueKind.String, "工具实现不可用时的 fallback 策略。", isAdvanced: true),
            Field("tool_providers.*.enabled", "tools", "工具 Provider 开关", ConfigurationValueKind.Boolean, "第三方工具 provider 是否启用。", StructuredValue.FromBoolean(true), isAdvanced: true),
            Field("tool_providers.*.type", "tools", "工具 Provider 类型", ConfigurationValueKind.Enum, "第三方工具 provider 来源类型。", allowedValues: ["assembly", "package", "plugin"], isAdvanced: true),
            Field("tool_providers.*.assembly_path", "tools", "工具 Provider 程序集", ConfigurationValueKind.Path, "第三方工具 provider 程序集路径。", isAdvanced: true),
            Field("tool_providers.*.provider_type", "tools", "工具 Provider 类型名", ConfigurationValueKind.String, "实现 ITianShuToolProvider 的完整类型名。", isAdvanced: true),
            Field("tool_providers.*.priority", "tools", "工具 Provider 优先级", ConfigurationValueKind.Integer, "第三方工具 provider 注册优先级。", StructuredValue.FromNumber("0"), isAdvanced: true),
            Field("tools.*.approval", "tools", "工具审批", ConfigurationValueKind.Enum, "单个工具的审批策略。", allowedValues: ["never", "on-request", "on-failure", "always", "ask", "untrusted"]),
            Field("tools.shell.timeout_seconds", "tools", "Shell 超时", ConfigurationValueKind.Integer, "shell 默认超时，单位秒。", StructuredValue.FromNumber("120")),
            Field("tools.shell.working_directory", "tools", "Shell 工作目录", ConfigurationValueKind.Path, "shell 默认工作目录。"),
            Field("tools.shell.environment_policy", "tools", "Shell 环境策略", ConfigurationValueKind.Enum, "shell 环境变量继承策略。", allowedValues: ["empty", "inherit-safe", "inherit-all"]),
            Field("tools.filesystem.max_read_bytes", "tools", "文件读取上限", ConfigurationValueKind.Integer, "单次文件读取最大字节数。", StructuredValue.FromNumber("1048576")),
            Field("tools.filesystem.write_requires_approval", "tools", "写文件需审批", ConfigurationValueKind.Boolean, "文件写入是否额外审批。", StructuredValue.FromBoolean(false)),
            Field("tools.patch.engine", "tools", "Patch 引擎", ConfigurationValueKind.String, "patch 工具使用的引擎。", StructuredValue.FromString("apply-patch")),
            Field("mcp.enabled", "mcp", "启用 MCP", ConfigurationValueKind.Boolean, "是否启用 MCP 能力。", StructuredValue.FromBoolean(true)),
            Field("mcp.auto_start", "mcp", "自动启动 MCP", ConfigurationValueKind.Boolean, "是否自动启动 MCP server。", StructuredValue.FromBoolean(true)),
            Field("mcp.registry", "mcp", "MCP Registry", ConfigurationValueKind.String, "MCP registry 来源。"),
            Field("mcp.servers.*.enabled", "mcp", "MCP Server 开关", ConfigurationValueKind.Boolean, "MCP server 是否启用。", StructuredValue.FromBoolean(true)),
            Field("mcp.servers.*.transport", "mcp", "MCP 传输", ConfigurationValueKind.Enum, "MCP server 传输方式。", allowedValues: ["stdio", "http", "websocket"]),
            Field("mcp.servers.*.command", "mcp", "MCP 命令", ConfigurationValueKind.String, "MCP stdio server 启动命令。", isAdvanced: true),
            Field("mcp.servers.*.args", "mcp", "MCP 参数", ConfigurationValueKind.Array, "MCP stdio server 启动参数。", isAdvanced: true),
            Field("mcp.servers.*.env", "mcp", "MCP 环境变量", ConfigurationValueKind.Object, "MCP server 环境变量映射。", isAdvanced: true),
            Field("mcp.servers.*.url", "mcp", "MCP URL", ConfigurationValueKind.String, "MCP HTTP/WebSocket server URL。"),
            Field("mcp.servers.*.bearer_token_env", "mcp", "MCP Token 环境变量", ConfigurationValueKind.SecretReference, "MCP bearer token 环境变量名。", editMode: ConfigurationFieldEditMode.SecretReferenceOnly),
            Field("mcp.servers.*.startup_timeout_ms", "mcp", "MCP 启动超时", ConfigurationValueKind.Integer, "MCP server 启动等待超时，单位毫秒。"),
            Field("mcp_servers.*.command", "mcp", "旧 MCP 命令", ConfigurationValueKind.String, "旧式 MCP server 启动命令兼容键。", isAdvanced: true),
            Field("skills.enabled", "skills", "启用技能", ConfigurationValueKind.Boolean, "是否启用 skills。", StructuredValue.FromBoolean(true)),
            Field("skills.system_root", "skills", "系统技能目录", ConfigurationValueKind.Path, "系统内置技能根目录。"),
            Field("skills.user_root", "skills", "用户技能目录", ConfigurationValueKind.Path, "用户技能根目录。"),
            Field("skills.project_root", "skills", "项目技能目录", ConfigurationValueKind.Path, "项目技能根目录。"),
            Field("skills.extra_roots", "skills", "额外技能目录", ConfigurationValueKind.Array, "额外技能根目录。"),
            Field("skills.remote.*.enabled", "skills", "远程技能开关", ConfigurationValueKind.Boolean, "远程技能源是否启用。", StructuredValue.FromBoolean(false)),
            Field("skills.remote.*.scope", "skills", "远程技能范围", ConfigurationValueKind.String, "远程技能源作用域。"),
            Field("skills.remote.*.product_surface", "skills", "远程技能消费面", ConfigurationValueKind.String, "远程技能目标消费面。"),
            Field("skill_profiles.*.enabled", "skills", "启用技能集合", ConfigurationValueKind.Array, "该 skill profile 显式启用的技能。"),
            Field("skill_profiles.*.disabled", "skills", "禁用技能集合", ConfigurationValueKind.Array, "该 skill profile 显式禁用的技能。"),
            Field("skill_profiles.*.force_reload", "skills", "强制重载技能", ConfigurationValueKind.Boolean, "是否强制刷新技能缓存。", StructuredValue.FromBoolean(false)),
            Field("plugins.enabled", "plugins_apps", "启用插件", ConfigurationValueKind.Boolean, "是否启用插件系统。", StructuredValue.FromBoolean(true)),
            Field("plugins.root", "plugins_apps", "插件根目录", ConfigurationValueKind.Path, "插件根目录。"),
            Field("plugins.auto_load", "plugins_apps", "自动加载插件", ConfigurationValueKind.Boolean, "是否自动加载插件。", StructuredValue.FromBoolean(true)),
            Field("plugins.installed.*.enabled", "plugins_apps", "已安装插件开关", ConfigurationValueKind.Boolean, "已安装插件是否启用。", StructuredValue.FromBoolean(true)),
            Field("plugins.installed.*.kind", "plugins_apps", "插件类型", ConfigurationValueKind.String, "插件来源或类型。"),
            Field("plugins.installed.*.path", "plugins_apps", "插件路径", ConfigurationValueKind.Path, "插件路径。"),
            Field("plugins.marketplace_trust.require_signer", "plugins_apps", "插件市场要求签名者", ConfigurationValueKind.Boolean, "安装插件市场条目时是否要求声明 signer。", StructuredValue.FromBoolean(false), isAdvanced: true),
            Field("plugins.marketplace_trust.allow_remote_archive_sources", "plugins_apps", "允许远程插件归档源", ConfigurationValueKind.Boolean, "是否允许 marketplace 使用远程 archive 源。", StructuredValue.FromBoolean(false), isAdvanced: true),
            Field("plugins.marketplace_trust.remote_archive_max_bytes", "plugins_apps", "远程插件归档大小上限", ConfigurationValueKind.Integer, "远程 archive 源下载大小上限，单位字节。", StructuredValue.FromNumber("52428800"), isAdvanced: true),
            Field("plugins.marketplace_trust.allow_remote_marketplace_sources", "plugins_apps", "允许远程插件市场", ConfigurationValueKind.Boolean, "是否允许同步远程 marketplace。", StructuredValue.FromBoolean(false), isAdvanced: true),
            Field("plugins.marketplace_trust.remote_marketplace_max_bytes", "plugins_apps", "远程插件市场大小上限", ConfigurationValueKind.Integer, "远程 marketplace JSON 下载大小上限，单位字节。", StructuredValue.FromNumber("2097152"), isAdvanced: true),
            Field("plugins.marketplace_trust.trusted_signers", "plugins_apps", "可信插件签名者", ConfigurationValueKind.Array, "允许的 marketplace signer 名称集合。", isAdvanced: true),
            Field("plugins.marketplace_trust.signers.*.public_key_sha256", "plugins_apps", "插件签名公钥 SHA-256", ConfigurationValueKind.String, "可信 signer 公钥 SHA-256。", isAdvanced: true),
            Field("plugins.marketplace_trust.signers.*.public_key", "plugins_apps", "插件签名公钥", ConfigurationValueKind.SecretReference, "可信 signer 公钥引用。", editMode: ConfigurationFieldEditMode.SecretReferenceOnly, isAdvanced: true),
            Field("plugins.marketplace_trust.certificate_authorities.*.enabled", "plugins_apps", "插件证书 CA 开关", ConfigurationValueKind.Boolean, "是否启用该证书 CA。", StructuredValue.FromBoolean(true), isAdvanced: true),
            Field("plugins.marketplace_trust.certificate_authorities.*.certificate_sha256", "plugins_apps", "插件证书 CA SHA-256", ConfigurationValueKind.String, "可信证书 CA SHA-256。", isAdvanced: true),
            Field("plugins.marketplace_trust.certificate_authorities.*.certificate", "plugins_apps", "插件证书 CA", ConfigurationValueKind.SecretReference, "可信证书 CA 引用。", editMode: ConfigurationFieldEditMode.SecretReferenceOnly, isAdvanced: true),
            Field("plugins.marketplace_trust.transparency_logs.*.enabled", "plugins_apps", "透明日志开关", ConfigurationValueKind.Boolean, "是否启用该 transparency log。", StructuredValue.FromBoolean(true), isAdvanced: true),
            Field("plugins.marketplace_trust.transparency_logs.*.public_key_sha256", "plugins_apps", "透明日志公钥 SHA-256", ConfigurationValueKind.String, "transparency log 公钥 SHA-256。", isAdvanced: true),
            Field("plugins.marketplace_trust.transparency_logs.*.public_key", "plugins_apps", "透明日志公钥", ConfigurationValueKind.SecretReference, "transparency log 公钥引用。", editMode: ConfigurationFieldEditMode.SecretReferenceOnly, isAdvanced: true),
            Field("plugins.remote_marketplaces.*.enabled", "plugins_apps", "远程插件市场开关", ConfigurationValueKind.Boolean, "是否启用该远程 marketplace。", StructuredValue.FromBoolean(true), isAdvanced: true),
            Field("plugins.remote_marketplaces.*.url", "plugins_apps", "远程插件市场 URL", ConfigurationValueKind.String, "远程 marketplace JSON 地址。", isAdvanced: true),
            Field("plugins.remote_marketplaces.*.sha256", "plugins_apps", "远程插件市场 SHA-256", ConfigurationValueKind.String, "远程 marketplace JSON 完整性 SHA-256。", isAdvanced: true),
            Field("apps.enabled", "plugins_apps", "启用应用连接", ConfigurationValueKind.Boolean, "是否启用 app connector。", StructuredValue.FromBoolean(true)),
            Field("apps.catalog", "plugins_apps", "应用目录", ConfigurationValueKind.String, "应用 connector catalog 来源。"),
            Field("apps.connectors.*.enabled", "plugins_apps", "Connector 开关", ConfigurationValueKind.Boolean, "应用 connector 是否启用。", StructuredValue.FromBoolean(true)),
            Field("apps.connectors.*.provider", "plugins_apps", "Connector Provider", ConfigurationValueKind.String, "应用 connector provider。"),
            Field("apps.connectors.*.auth", "plugins_apps", "Connector 鉴权", ConfigurationValueKind.String, "应用 connector 鉴权方式。"),
            Field("apps.connectors.*.token_env", "plugins_apps", "Connector Token 环境变量", ConfigurationValueKind.SecretReference, "connector token 环境变量名。", editMode: ConfigurationFieldEditMode.SecretReferenceOnly),
            Field("identity_profile", "identity_memory", "身份 Profile", ConfigurationValueKind.String, "默认身份 profile。"),
            Field("identity_profiles.*.account", "identity_memory", "身份账户", ConfigurationValueKind.String, "identity profile 绑定的账户。"),
            Field("identity_profiles.*.device_binding", "identity_memory", "设备绑定", ConfigurationValueKind.String, "identity profile 绑定的设备。"),
            Field("identity_profiles.*.habit_profile", "identity_memory", "习惯 Profile", ConfigurationValueKind.String, "identity profile 绑定的用户习惯画像。"),
            Field("identity_profiles.*.allow_device_sync", "identity_memory", "允许设备同步", ConfigurationValueKind.Boolean, "是否允许设备间同步。", StructuredValue.FromBoolean(false)),
            Field("accounts.*.display_name", "identity_memory", "账户显示名", ConfigurationValueKind.String, "账户展示名称。"),
            Field("accounts.*.provider", "identity_memory", "账户 Provider", ConfigurationValueKind.String, "账户来源 provider。"),
            Field("accounts.*.email_env", "identity_memory", "邮箱环境变量", ConfigurationValueKind.SecretReference, "账户邮箱环境变量名。", editMode: ConfigurationFieldEditMode.SecretReferenceOnly),
            Field("devices.*.display_name", "identity_memory", "设备显示名", ConfigurationValueKind.String, "设备展示名称。"),
            Field("devices.*.kind", "identity_memory", "设备类型", ConfigurationValueKind.String, "设备类型。"),
            Field("devices.*.trust", "identity_memory", "设备信任", ConfigurationValueKind.Enum, "设备信任等级。", allowedValues: ["local", "trusted", "untrusted"]),
            Field("memory.enabled", "identity_memory", "启用记忆", ConfigurationValueKind.Boolean, "是否启用记忆系统。", StructuredValue.FromBoolean(true)),
            Field("memory.default_profile", "identity_memory", "记忆 Profile", ConfigurationValueKind.String, "默认记忆 profile。"),
            Field("memory_profiles.*.enabled", "memory", "记忆 Profile 开关", ConfigurationValueKind.Boolean, "memory profile 是否启用。", StructuredValue.FromBoolean(true)),
            Field("memory_profiles.*.default_space", "memory", "默认记忆空间", ConfigurationValueKind.String, "memory profile 默认 space。"),
            Field("memory_profiles.*.overlay", "memory", "启用记忆 Overlay", ConfigurationValueKind.Boolean, "是否把记忆 overlay 注入上下文。", StructuredValue.FromBoolean(true)),
            Field("memory_profiles.*.extract", "memory", "记忆抽取", ConfigurationValueKind.Enum, "记忆候选抽取策略。", allowedValues: ["off", "manual", "background"]),
            Field("memory_profiles.*.retention", "memory", "记忆保留", ConfigurationValueKind.Enum, "记忆保留策略。", allowedValues: ["keep", "archive", "forget"]),
            Field("memory.spaces.*.scope", "memory", "记忆空间范围", ConfigurationValueKind.Enum, "memory space 的作用域。", allowedValues: ["user", "workspace", "team", "session", "agent", "collaboration"]),
            Field("memory.spaces.*.provider", "memory", "记忆空间 Provider", ConfigurationValueKind.String, "memory space 默认 provider。"),
            Field("memory.spaces.*.read_only", "memory", "记忆空间只读", ConfigurationValueKind.Boolean, "memory space 是否只读。", StructuredValue.FromBoolean(false)),
            Field("memory.spaces.*.tags", "memory", "记忆空间标签", ConfigurationValueKind.Array, "memory space 标签。"),
            Field("memory.providers.*.kind", "memory", "记忆 Provider 类型", ConfigurationValueKind.String, "memory provider adapter id。"),
            Field("memory.providers.*.display_name", "memory", "记忆 Provider 显示名", ConfigurationValueKind.String, "在配置界面和 provider 列表中显示的名称。"),
            Field("memory.providers.*.mode", "memory", "记忆 Provider 模式", ConfigurationValueKind.Enum, "memory provider 模式。", allowedValues: ["read-only", "read-write", "mirror", "import-export"]),
            Field("memory.providers.*.root", "memory", "记忆 Provider 根目录", ConfigurationValueKind.Path, "本地 memory provider 根目录。"),
            Field("memory.providers.*.enabled", "memory", "启用记忆 Provider", ConfigurationValueKind.Boolean, "是否启用该 memory provider。语义或外部 provider 建议显式配置后再启用。", StructuredValue.FromBoolean(true)),
            Field("memory.providers.*.host", "memory", "记忆 Provider 主机", ConfigurationValueKind.String, "外部或本地 memory provider 服务主机名；只用于 provider adapter，不进入 northbound contracts。"),
            Field("memory.providers.*.port", "memory", "记忆 Provider 端口", ConfigurationValueKind.Integer, "外部或本地 memory provider 的 HTTP/TCP 端口。"),
            Field("memory.providers.*.grpc_port", "memory", "记忆 Provider gRPC 端口", ConfigurationValueKind.Integer, "预留的 gRPC 端口字段；当前正式 adapter 仍使用 HTTP port，gRPC adapter 落地前不会用于探测或调用。", isAdvanced: true),
            Field("memory.providers.*.api_key_env", "memory", "记忆 Provider API Key 环境变量", ConfigurationValueKind.SecretReference, "保存外部 memory provider API key 的环境变量名；配置只保存变量名，不保存密钥。", editMode: ConfigurationFieldEditMode.SecretReferenceOnly),
            Field("memory.providers.*.authorization_env", "memory", "记忆 Provider Authorization 环境变量", ConfigurationValueKind.SecretReference, "保存完整 Authorization header 值的环境变量名；配置只保存变量名，不保存密钥。", editMode: ConfigurationFieldEditMode.SecretReferenceOnly),
            Field("memory.providers.*.connect_timeout_ms", "memory", "记忆 Provider 连接超时", ConfigurationValueKind.Integer, "外部 memory provider 建立连接时的超时时间，单位为毫秒。"),
            Field("memory.providers.*.capabilities", "memory", "记忆 Provider 能力", ConfigurationValueKind.Array, "provider 自声明能力，例如 semantic-search、embedding-indexing、read-only 或 read-write。"),
            Field("memory.bindings.*.space", "memory", "记忆绑定空间", ConfigurationValueKind.String, "memory binding 目标 space。"),
            Field("memory.bindings.*.provider", "memory", "记忆绑定 Provider", ConfigurationValueKind.String, "memory binding 目标 provider。"),
            Field("memory.bindings.*.capabilities", "memory", "记忆绑定能力", ConfigurationValueKind.Array, "memory binding 允许能力。"),
            Field("memory.bindings.*.mode", "memory", "记忆绑定模式", ConfigurationValueKind.Enum, "memory binding 模式。", allowedValues: ["read-only", "read-write", "mirror", "import-export"]),
            Field("workspace_profiles.default.root_markers", "workspace", "默认根目录标记", ConfigurationValueKind.Array, "默认工作空间用于识别项目根目录的标记文件或目录。"),
            Field("workspace_profiles.default.default_workspace", "workspace", "默认工作目录", ConfigurationValueKind.Path, "没有项目级覆盖时，新会话使用的默认工作目录。"),
            Field("workspace_profiles.default.trust_policy", "workspace", "默认信任策略", ConfigurationValueKind.Enum, "默认工作空间遇到未知目录时的信任判断策略。", StructuredValue.FromString("prompt"), allowedValues: ["prompt", "trusted-only", "disabled"]),
            Field("workspace_profiles.default.artifact_root", "workspace", "默认 Artifact 根目录", ConfigurationValueKind.Path, "默认工作空间保存运行工件的根目录。"),
            Field("workspace_profiles.default.state_root", "workspace", "默认状态根目录", ConfigurationValueKind.Path, "默认工作空间保存状态文件的根目录。"),
            Field("workspace_profiles.default.model", "workspace", "默认工作区模型", ConfigurationValueKind.String, "默认工作空间中新建会话使用的模型；inherit 表示继承 active agent/model。", StructuredValue.FromString("inherit")),
            Field("workspace_profiles.default.model_lock", "workspace", "默认模型锁", ConfigurationValueKind.Enum, "默认工作空间中新建会话是否固化模型快照。", StructuredValue.FromString("snapshot-on-create"), allowedValues: ["off", "snapshot-on-create"]),
            Field("projects.*.trust_level", "workspace", "项目信任级别", ConfigurationValueKind.Enum, "项目级信任声明。", allowedValues: ["trusted", "untrusted"]),
            Field("collaboration_profiles.*.default_space", "collaboration", "默认协作空间", ConfigurationValueKind.String, "collaboration profile 默认 space。"),
            Field("collaboration_profiles.*.default_workspace", "collaboration", "默认协作工作区", ConfigurationValueKind.Path, "collaboration profile 默认 workspace。"),
            Field("collaboration_profiles.*.default_execution_profile", "collaboration", "默认执行 Profile", ConfigurationValueKind.String, "collaboration profile 默认 execution profile。"),
            Field("collaboration_profiles.*.policy_key", "collaboration", "协作策略 Key", ConfigurationValueKind.String, "collaboration policy key。"),
            Field("collaboration_profiles.*.participant_role", "collaboration", "参与者角色", ConfigurationValueKind.String, "协作参与者默认角色。"),
            Field("workflow_profiles.*.default_space", "collaboration", "工作流默认空间", ConfigurationValueKind.String, "workflow profile 默认 space。"),
            Field("workflow_profiles.*.default_owner", "collaboration", "工作流默认负责人", ConfigurationValueKind.String, "workflow profile 默认 owner。"),
            Field("workflow_profiles.*.task_state", "collaboration", "默认任务状态", ConfigurationValueKind.String, "workflow profile 默认任务状态。"),
            Field("workflow_profiles.*.verification_gate", "collaboration", "验证关口", ConfigurationValueKind.Enum, "workflow 验证关口。", allowedValues: ["manual", "automatic", "disabled"]),
            Field("workflow_profiles.*.auto_dispatch_jobs", "collaboration", "自动派发任务", ConfigurationValueKind.Boolean, "是否自动派发 workflow jobs。", StructuredValue.FromBoolean(false)),
            Field("state.root", "state", "状态根目录", ConfigurationValueKind.Path, "状态存储根目录。"),
            Field("state.thread_store", "state", "线程存储", ConfigurationValueKind.Enum, "线程存储后端。", allowedValues: ["sqlite", "json"]),
            Field("state.rollout_store", "state", "Rollout 存储", ConfigurationValueKind.Enum, "rollout 存储后端。", allowedValues: ["jsonl", "off"]),
            Field("state.autosave", "state", "自动保存状态", ConfigurationValueKind.Boolean, "是否自动保存状态。", StructuredValue.FromBoolean(true)),
            Field("artifacts.root", "state", "Artifact 根目录", ConfigurationValueKind.Path, "artifact 根目录。"),
            Field("artifacts.retention", "state", "Artifact 保留", ConfigurationValueKind.Enum, "artifact 保留策略。", allowedValues: ["unlimited", "versions", "days"]),
            Field("artifacts.max_versions", "state", "Artifact 最大版本", ConfigurationValueKind.Integer, "artifact 最大版本数；0 表示无限制。"),
            Field("artifacts.atomic_commit", "state", "Artifact 原子提交", ConfigurationValueKind.Boolean, "是否使用原子提交。", StructuredValue.FromBoolean(true)),
            Field("diagnostics.enabled", "diagnostics", "启用诊断", ConfigurationValueKind.Boolean, "是否启用本地诊断采集。", StructuredValue.FromBoolean(true)),
            Field("diagnostics.default_level", "diagnostics", "默认诊断级别", ConfigurationValueKind.Enum, "诊断采集默认级别。", StructuredValue.FromString("stats"), allowedValues: ["off", "summary", "stats", "artifact", "verbose"]),
            Field("diagnostics.level", "diagnostics", "日志诊断级别", ConfigurationValueKind.Enum, "诊断日志级别。", StructuredValue.FromString("info"), allowedValues: ["trace", "debug", "info", "warn", "error"]),
            Field("diagnostics.trace", "diagnostics", "记录 Trace", ConfigurationValueKind.Boolean, "是否记录 trace 级诊断。", StructuredValue.FromBoolean(true)),
            Field("diagnostics.redact_secrets", "diagnostics", "诊断脱敏", ConfigurationValueKind.Boolean, "是否对诊断产物脱敏。", StructuredValue.FromBoolean(true)),
            Field("diagnostics.events_jsonl", "diagnostics", "事件 JSONL", ConfigurationValueKind.Path, "诊断事件 JSONL 输出路径。", isAdvanced: true),
            Field("diagnostics.telemetry.enabled", "diagnostics", "启用遥测", ConfigurationValueKind.Boolean, "是否允许外发脱敏遥测。", StructuredValue.FromBoolean(false), isAdvanced: true),
            Field("host.surface", "runtime", "宿主界面", ConfigurationValueKind.Enum, "当前 host surface。", allowedValues: ["cli", "sidecar", "vsix", "service"]),
            Field("host.app_host_project", "runtime", "AppHost 项目", ConfigurationValueKind.Path, "AppHost 项目或入口路径。", isAdvanced: true),
            Field("host.listen", "runtime", "监听地址", ConfigurationValueKind.String, "app-server / sidecar 监听地址。"),
            Field("host.analytics_default_enabled", "runtime", "宿主分析默认启用", ConfigurationValueKind.Boolean, "是否默认启用宿主分析开关。", StructuredValue.FromBoolean(false)),
            Field("runtime.control_plane", "runtime", "控制平面", ConfigurationValueKind.Enum, "运行期控制平面来源。", allowedValues: ["local", "remote", "sidecar"]),
            Field("runtime.protocol_adapter", "runtime", "协议适配器", ConfigurationValueKind.String, "执行协议适配器。"),
            Field("runtime.jsonl_protocol", "runtime", "JSONL 协议", ConfigurationValueKind.Boolean, "是否启用 JSONL 自动化协议。", StructuredValue.FromBoolean(false)),
            Field("runtime.typed_surface_first", "runtime", "Typed Surface 优先", ConfigurationValueKind.Boolean, "是否优先走 formal typed surface。", StructuredValue.FromBoolean(true)),
            Field("runtime.legacy_diagnostics_bridge", "runtime", "Legacy 诊断桥（已废弃）", ConfigurationValueKind.Array, "旧配置投影兼容字段；正式 CLI / Sidecar / VSIX RPC 不再使用 diagnostics bridge fallback，未登记 method 必须明确拒绝。", isAdvanced: true),
            Field("tui.enabled", "experience", "启用 TUI", ConfigurationValueKind.Boolean, "是否启用 TUI 体验。"),
            Field("tui.theme", "experience", "TUI 主题", ConfigurationValueKind.String, "TUI 主题名称。"),
            Field("tui_profiles.*.theme", "experience", "TUI Profile 主题", ConfigurationValueKind.String, "TUI profile 引用的主题。"),
            Field("tui_profiles.*.startup_card", "experience", "启动卡片", ConfigurationValueKind.Boolean, "TUI 是否显示启动卡片。", StructuredValue.FromBoolean(true)),
            Field("tui_profiles.*.show_model", "experience", "显示模型", ConfigurationValueKind.Boolean, "TUI 是否展示模型。", StructuredValue.FromBoolean(true)),
            Field("tui_profiles.*.show_directory", "experience", "显示目录", ConfigurationValueKind.Boolean, "TUI 是否展示目录。", StructuredValue.FromBoolean(true)),
            Field("tui_profiles.*.show_permissions", "experience", "显示权限", ConfigurationValueKind.Boolean, "TUI 是否展示权限。", StructuredValue.FromBoolean(true)),
            Field("tui_profiles.*.loading", "experience", "Loading 样式", ConfigurationValueKind.Enum, "TUI loading 展示方式。", allowedValues: ["off", "thinking", "spinner"]),
            Field("tui_profiles.*.cursor", "experience", "光标样式", ConfigurationValueKind.Enum, "TUI 光标样式。", allowedValues: ["bar", "block", "underline"]),
            Field("tui.themes.*.accent", "experience", "主题强调色", ConfigurationValueKind.String, "TUI 主题强调色。"),
            Field("tui.themes.*.danger", "experience", "主题危险色", ConfigurationValueKind.String, "TUI 主题危险色。"),
            Field("tui.themes.*.warning", "experience", "主题警告色", ConfigurationValueKind.String, "TUI 主题警告色。"),
            Field("tui.themes.*.muted", "experience", "主题弱化色", ConfigurationValueKind.String, "TUI 主题弱化文字色。"),
            Field("tui.themes.*.background", "experience", "主题背景", ConfigurationValueKind.String, "TUI 主题背景。"),
            Field("feature_profiles.*.enabled", "feature", "启用功能集合", ConfigurationValueKind.Array, "该 feature profile 显式启用的功能。"),
            Field("feature_profiles.*.disabled", "feature", "禁用功能集合", ConfigurationValueKind.Array, "该 feature profile 显式禁用的功能。"),
            Field("features.*.enabled", "feature", "功能开关", ConfigurationValueKind.Boolean, "feature 默认状态。", StructuredValue.FromBoolean(false)),
            Field("features.*.stage", "feature", "功能阶段", ConfigurationValueKind.Enum, "feature 生命周期阶段。", allowedValues: ["experimental", "preview", "stable", "deprecated"]),
            Field("features.*.description", "feature", "功能说明", ConfigurationValueKind.String, "feature 说明。"),
            Field("realtime_profiles.*.enabled", "feature", "Realtime 开关", ConfigurationValueKind.Boolean, "是否启用 realtime 控制能力。", StructuredValue.FromBoolean(false)),
            Field("realtime_profiles.*.provider", "feature", "Realtime Provider", ConfigurationValueKind.String, "realtime provider。"),
            Field("realtime_profiles.*.model", "feature", "Realtime 模型", ConfigurationValueKind.String, "realtime 模型。"),
            Field("realtime_profiles.*.audio_input", "feature", "Realtime 音频输入", ConfigurationValueKind.Boolean, "是否允许 realtime 音频输入。", StructuredValue.FromBoolean(true)),
            Field("realtime_profiles.*.audio_output", "feature", "Realtime 音频输出", ConfigurationValueKind.Boolean, "是否允许 realtime 音频输出。", StructuredValue.FromBoolean(true)),
            Field("realtime_profiles.*.handoff_mode", "feature", "Realtime Handoff", ConfigurationValueKind.Enum, "realtime handoff 策略。", allowedValues: ["manual", "auto", "off"]),
            Field("review_profiles.*.enabled", "feature", "Review 开关", ConfigurationValueKind.Boolean, "是否启用 review 启动入口。", StructuredValue.FromBoolean(true)),
            Field("review_profiles.*.default_target", "feature", "Review 默认目标", ConfigurationValueKind.Enum, "review 默认目标。", allowedValues: ["uncommitted-changes", "base-branch", "commit", "custom"]),
            Field("review_profiles.*.delivery", "feature", "Review 输出方式", ConfigurationValueKind.Enum, "review 输出方式。", allowedValues: ["inline", "detached"]),
            Field("review_profiles.*.include_diff", "feature", "Review 包含 Diff", ConfigurationValueKind.Boolean, "review 是否默认携带 diff。", StructuredValue.FromBoolean(true)),
            Field("review_profiles.*.include_logs", "feature", "Review 包含日志", ConfigurationValueKind.Boolean, "review 是否默认携带日志。", StructuredValue.FromBoolean(false)),
            Field("feedback.enabled", "feature", "Feedback 开关", ConfigurationValueKind.Boolean, "是否启用 feedback。", StructuredValue.FromBoolean(true)),
            Field("feedback.upload", "feature", "Feedback 上传", ConfigurationValueKind.Enum, "feedback 上传策略。", allowedValues: ["off", "ask", "enabled"]),
            Field("feedback.include_logs_default", "feature", "Feedback 默认日志", ConfigurationValueKind.Boolean, "feedback 是否默认附带日志。", StructuredValue.FromBoolean(false)),
            Field("feedback.extra_log_roots", "feature", "Feedback 额外日志目录", ConfigurationValueKind.Array, "feedback 可附加日志根目录。"),
            Field("imports.*.enabled", "imports", "导入源开关", ConfigurationValueKind.Boolean, "外部配置导入源是否启用。", StructuredValue.FromBoolean(true), isAdvanced: true),
            Field("imports.*.path", "imports", "导入路径", ConfigurationValueKind.Path, "外部配置导入路径。", isAdvanced: true),
            Field("imports.*.mode", "imports", "导入模式", ConfigurationValueKind.Enum, "外部配置导入模式。", allowedValues: ["read-only", "migration", "mirror"], isAdvanced: true),
            Field("imports.*.map_profile", "imports", "导入 Profile 映射", ConfigurationValueKind.String, "外部配置导入后的 profile 映射名。", isAdvanced: true),
            Field("extensions.*.enabled", "imports", "启用扩展", ConfigurationValueKind.Boolean, "扩展源开关。", isAdvanced: true),
            Field("extensions.*.kind", "imports", "扩展类型", ConfigurationValueKind.String, "扩展来源或类型。", isAdvanced: true),
            Field("extensions.*.assembly", "imports", "扩展程序集", ConfigurationValueKind.Path, "扩展程序集路径。", isAdvanced: true),
            Field("x.*.custom_policy", "imports", "扩展命名空间策略", ConfigurationValueKind.String, "扩展命名空间下的自定义策略示例。", isAdvanced: true),
            Field("requires.min_tianshu_version", "requirements", "最低 TianShu 版本", ConfigurationValueKind.String, "配置要求的最低 TianShu 版本。"),
            Field("requires.allowed_providers", "requirements", "允许 Provider", ConfigurationValueKind.Array, "允许使用的 provider 列表；空表示不限制。"),
            Field("requires.allowed_sandboxes", "requirements", "允许 Sandbox", ConfigurationValueKind.Array, "允许使用的 sandbox 列表；空表示不限制。"),
            Field("requires.forbid_danger_full_access", "requirements", "禁止全权限沙箱", ConfigurationValueKind.Boolean, "是否禁止 danger-full-access。", StructuredValue.FromBoolean(false)),
            Field("requires.require_secret_indirection", "requirements", "要求 Secret 间接引用", ConfigurationValueKind.Boolean, "是否要求 secret 只能通过 env/ref/file 间接引用。", StructuredValue.FromBoolean(true)),
            Field("requires.keys", "requirements", "Key 约束", ConfigurationValueKind.Object, "按配置 key 声明的合规约束。", isAdvanced: true),
        ],
    };

    public ConfigurationSchemaCatalogSnapshot GetSnapshot() => Snapshot;

    private static ConfigurationCategoryDescriptor Category(
        string id,
        ConfigurationCategoryKind kind,
        string displayName,
        string description,
        int order)
        => new()
        {
            Id = id,
            Kind = kind,
            DisplayName = displayName,
            Description = description,
            Order = order,
        };

    private static ConfigurationGroupDescriptor Group(
        string id,
        string categoryId,
        string displayName,
        string description,
        int order)
        => new()
        {
            Id = id,
            CategoryId = categoryId,
            DisplayName = displayName,
            Description = description,
            Order = order,
        };

    private static ConfigurationFieldDescriptor Field(
        string key,
        string groupId,
        string displayName,
        ConfigurationValueKind valueKind,
        string description,
        StructuredValue? defaultValue = null,
        ConfigurationFieldEditMode editMode = ConfigurationFieldEditMode.RequiresPreview,
        bool isAdvanced = false,
        string[]? allowedValues = null)
        => new()
        {
            Key = key,
            GroupId = groupId,
            DisplayName = displayName,
            Description = description,
            ValueKind = valueKind,
            EditMode = editMode,
            DefaultValue = defaultValue,
            IsAdvanced = isAdvanced,
            IsSecret = valueKind == ConfigurationValueKind.SecretReference,
            AllowedValues = allowedValues?.Select(value => new ConfigurationAllowedValue
            {
                Value = StructuredValue.FromString(value),
                DisplayName = value,
                Description = DescribeAllowedValue(key, value),
            }).ToArray() ?? Array.Empty<ConfigurationAllowedValue>(),
        };

    private static string DescribeAllowedValue(string key, string value)
    {
        if (TryDescribeByExactKey(key, value, out var description))
        {
            return description;
        }

        if (key.Contains("protocol", StringComparison.OrdinalIgnoreCase))
        {
            return value switch
            {
                "auto" => "让 TianShu 根据 provider 类型、模型族和能力声明自动选择最合适的请求协议；推荐给不确定协议细节的普通用户。",
                "openai_responses" => "使用 OpenAI Responses API，适合 OpenAI 新模型以及需要 reasoning、结构化输出或工具调用聚合能力的端点。",
                "openai_chat_completions" => "使用 OpenAI 兼容的 Chat Completions API，适合大多数第三方兼容服务，也是兼容性最广的兜底协议。",
                "anthropic_messages" => "使用 Anthropic Messages API，适合 Claude 模型以及支持 thinking、tool_use 等 Anthropic 语义的兼容端点。",
                "google_generative" => "使用 Google Gemini generateContent / streamGenerateContent 协议，适合 Gemini 原生模型。",
                _ => FallbackDescription,
            };
        }

        if (key.Contains("approval", StringComparison.OrdinalIgnoreCase))
        {
            return value switch
            {
                "never" => "不主动弹出审批；适合你已经完全信任当前工作区、工具权限和外层沙箱的场景。",
                "on-request" or "ask" => "遇到文件写入、命令执行、提权或其他需要确认的动作时暂停，并让用户决定是否允许。",
                "on-failure" => "先按当前权限尝试执行；如果失败、被沙箱拦截或需要更高权限，再向用户请求审批。",
                "always" => "每一次受控操作都要求用户确认；最稳妥，但会明显增加交互打断。",
                "untrusted" => "按不受信任工作区处理，默认限制更多动作，并倾向于把风险操作交给用户确认。",
                _ => FallbackDescription,
            };
        }

        if (key.Contains("sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return value switch
            {
                "read-only" => "只允许读取文件和查看环境，不允许写入、移动或删除工作区内容；适合审阅、分析和低风险查询。",
                "workspace-write" => "允许在当前工作区或明确授权目录内写入文件；适合正常开发任务，同时限制越界写入。",
                "danger-full-access" => "允许访问完整文件系统，不再由工作区边界保护；只适合你明确完全信任任务和执行环境时使用。",
                _ => FallbackDescription,
            };
        }

        return value switch
        {
            "http" => "通过 HTTP 请求连接服务，适合普通远端 API。",
            "websocket" => "通过 WebSocket 长连接通信，适合实时流式能力。",
            "stdio" => "启动本地进程，并通过标准输入/输出通信，常用于本地 MCP server。",
            "sidecar" => "连接旁路宿主进程，由独立 sidecar 负责具体服务调用或生命周期管理。",
            "openai_responses" => "把推理或请求能力映射到 OpenAI Responses 语义。",
            "anthropic_thinking" => "把推理能力映射到 Anthropic thinking 语义。",
            "google_thinking_config" => "把推理预算和开关映射到 Gemini thinkingConfig。",
            "openai_compatible_extra_body" => "通过 OpenAI 兼容请求的 extra body 扩展字段传递推理参数。",
            "provider_native" => "交给 provider adapter 使用自己的原生参数，不做 TianShu 统一协议转换。",
            _ => FallbackDescription,
        };
    }

    private const string FallbackDescription = "该选项来自当前 schema，但还缺少专门说明；请结合字段用途谨慎使用。";

    private static bool TryDescribeByExactKey(string key, string value, out string description)
    {
        description = (key, value) switch
        {
            ("app.telemetry", "off") => "关闭应用级遥测，不向本地诊断或远端遥测写入应用运行统计。",
            ("app.telemetry", "local") => "只在本机记录必要的运行统计，便于排查问题，不主动外发遥测。",
            ("app.telemetry", "remote") => "允许将脱敏后的运行统计发送到远端遥测服务；只有在你明确需要集中观测时使用。",

            ("providers.*.reasoning_activation", _) or ("models.*.reasoning_activation", _) => DescribeReasoningActivation(value),
            ("providers.*.reasoning.effort", _) or ("models.*.default_reasoning_effort", _) or ("agents.*.reasoning.effort", _) => DescribeReasoningEffort(value),
            ("providers.*.reasoning.summary", _) or ("models.*.default_reasoning_summary", _) or ("agents.*.reasoning.summary", _) => DescribeReasoningSummary(value),
            ("providers.*.reasoning.verbosity", _) or ("models.*.default_verbosity", _) or ("agents.*.reasoning.verbosity", _) => DescribeVerbosity(value),

            ("execution_profiles.*.web_search", "off") => "不允许该执行 profile 发起联网搜索；回答只能依赖模型已有知识、上下文和本地工具。",
            ("execution_profiles.*.web_search", "auto") => "由 Agent 判断是否需要联网搜索；适合大多数场景。",
            ("execution_profiles.*.web_search", "on") => "允许并倾向使用联网搜索；适合需要最新信息或外部证据的任务。",

            ("workspace_profiles.*.trust_policy", "prompt") or ("workspace_profiles.default.trust_policy", "prompt") => "遇到未记录信任状态的工作区时询问用户，由用户决定是否信任。",
            ("workspace_profiles.*.trust_policy", "trusted-only") or ("workspace_profiles.default.trust_policy", "trusted-only") => "只允许已标记为受信任的工作区进入正常执行流程，未知目录会被拦截或降级。",
            ("workspace_profiles.*.trust_policy", "disabled") or ("workspace_profiles.default.trust_policy", "disabled") => "不启用工作区信任判断；适合外层已经提供同等安全边界的环境。",
            ("workspace_profiles.*.model_lock", _) or ("workspace_profiles.default.model_lock", _) or ("projects.*.model_lock", _) => DescribeModelLock(value),
            ("projects.*.trust", _) or ("projects.*.trust_level", _) or ("devices.*.trust", _) => DescribeTrust(value),

            ("session_profiles.*.model_binding", "snapshot-on-create") => "创建会话时把当前模型配置固定到会话内；之后全局配置变更不会悄悄影响这条会话。",
            ("session_profiles.*.model_binding", "live-config") => "会话每次运行都读取最新全局配置；适合希望模型配置即时生效的场景。",
            ("session_profiles.*.model_change_scope", "session") => "会话内切换模型只影响当前会话，不写回全局配置。",
            ("session_profiles.*.model_change_scope", "config-explicit") => "只有用户明确要求写回时，模型切换才会更新配置文件。",
            ("session_profiles.*.memory_mode", _) => DescribeMemoryMode(value),
            ("session_profiles.*.auto_resume", "ask") => "启动时发现可恢复会话会询问用户是否继续。",
            ("session_profiles.*.auto_resume", "never") => "启动时总是创建新会话，不自动恢复历史会话。",
            ("session_profiles.*.auto_resume", "last") => "启动时优先恢复最近一次会话，减少重复选择。",

            ("conversation_profiles.*.history", "full") => "尽量携带完整对话历史；上下文最完整，但 token 成本和跑偏风险最高。",
            ("conversation_profiles.*.history", "sliced") => "按上下文裁切策略保留关键历史；推荐给长会话。",
            ("conversation_profiles.*.history", "summary") => "主要携带摘要，不携带大量原文；适合长周期任务的低成本延续。",
            ("conversation_profiles.*.history", "none") => "不携带历史对话；适合一次性问题或需要彻底重置上下文的场景。",

            ("governance_profiles.*.risk_acknowledgement", "ask") => "遇到高风险操作时要求用户确认风险。",
            ("governance_profiles.*.risk_acknowledgement", "never") => "不额外要求风险确认；仅适合外层已有审批和审计控制的环境。",
            ("governance_profiles.*.risk_acknowledgement", "always") => "所有带风险标记的操作都要求确认；更保守但会增加打断。",

            ("tools.shell.environment_policy", "empty") => "启动 shell 时尽量不继承当前进程环境变量，只提供最小必要环境；最安全，但可能导致 PATH、SDK 或代理配置缺失。",
            ("tools.shell.environment_policy", "inherit-safe") => "继承常用且相对安全的环境变量，同时过滤 secret、token、key 等敏感项；推荐给日常开发。",
            ("tools.shell.environment_policy", "inherit-all") => "完整继承当前进程环境变量，包括可能存在的敏感变量；兼容性最好，但泄露风险最高。",
            ("tools.*.implementation_kind", _) => DescribeToolImplementationKind(value),
            ("tool_providers.*.type", _) => DescribeToolProviderType(value),

            ("memory_profiles.*.extract", "off") => "不从对话中抽取记忆候选；适合临时任务或不希望沉淀个人/项目偏好的场景。",
            ("memory_profiles.*.extract", "manual") => "只在用户明确触发或确认时抽取记忆候选。",
            ("memory_profiles.*.extract", "background") => "允许后台自动抽取候选记忆；候选仍应经过策略和风险判断后再提升。",
            ("memory_profiles.*.retention", "keep") => "符合策略的记忆长期保留，并可参与后续召回。",
            ("memory_profiles.*.retention", "archive") => "把记忆归档为低活跃状态，保留审计和回溯能力，但默认不优先召回。",
            ("memory_profiles.*.retention", "forget") => "按遗忘策略移除或降权相关记忆，适合错误、过期或不再希望保留的信息。",
            ("memory.spaces.*.scope", _) => DescribeMemoryScope(value),
            ("memory.providers.*.mode", _) or ("memory.bindings.*.mode", _) => DescribeMemoryProviderMode(value),

            ("workflow_profiles.*.verification_gate", "manual") => "由用户或人工流程确认验证是否通过。",
            ("workflow_profiles.*.verification_gate", "automatic") => "由系统根据测试、构建、诊断事件等信号自动判断验证结果。",
            ("workflow_profiles.*.verification_gate", "disabled") => "不启用验证关口；适合草稿流程，不适合正式交付。",

            ("state.thread_store", "sqlite") => "使用 SQLite 存储会话线程，适合需要查询、事务和较稳定本地状态的场景。",
            ("state.thread_store", "json") => "使用 JSON 文件存储会话线程，便于人工查看和迁移，但并发与查询能力较弱。",
            ("state.rollout_store", "jsonl") => "用 JSON Lines 追加记录 rollout 事件，适合审计、回放和增量写入。",
            ("state.rollout_store", "off") => "关闭 rollout 持久化；会降低审计和恢复能力。",
            ("artifacts.retention", "unlimited") => "不主动清理 artifact；便于排查历史，但磁盘占用会持续增长。",
            ("artifacts.retention", "versions") => "按版本数量保留 artifact，超出后清理旧版本。",
            ("artifacts.retention", "days") => "按保留天数清理 artifact，适合长期运行环境。",

            ("diagnostics.default_level", _) => DescribeDiagnosticCollectionLevel(value),
            ("diagnostics.level", _) => DescribeDiagnosticLogLevel(value),
            ("host.surface", _) => DescribeHostSurface(value),
            ("runtime.control_plane", _) => DescribeControlPlane(value),

            ("tui_profiles.*.loading", "off") => "不展示加载动画或 thinking 状态，界面最安静。",
            ("tui_profiles.*.loading", "thinking") => "用文字状态展示模型正在思考或执行，便于理解当前阶段。",
            ("tui_profiles.*.loading", "spinner") => "用旋转指示器展示运行中，界面更紧凑。",
            ("tui_profiles.*.cursor", "bar") => "输入光标显示为竖线，接近常见文本编辑器。",
            ("tui_profiles.*.cursor", "block") => "输入光标显示为块状，更接近终端风格。",
            ("tui_profiles.*.cursor", "underline") => "输入光标显示为下划线，视觉占用更低。",

            ("features.*.stage", "experimental") => "实验阶段，行为和配置可能随时调整，不建议用于稳定流程。",
            ("features.*.stage", "preview") => "预览阶段，功能基本可用，但仍可能根据反馈改变。",
            ("features.*.stage", "stable") => "稳定阶段，默认可用于正式流程。",
            ("features.*.stage", "deprecated") => "已弃用，仅为兼容旧配置保留，不建议继续使用。",
            ("realtime_profiles.*.handoff_mode", "manual") => "Realtime 交接必须由用户手动触发。",
            ("realtime_profiles.*.handoff_mode", "auto") => "由系统根据上下文自动决定是否交接到 Realtime 能力。",
            ("realtime_profiles.*.handoff_mode", "off") => "不启用 Realtime 交接。",
            ("review_profiles.*.default_target", _) => DescribeReviewTarget(value),
            ("review_profiles.*.delivery", "inline") => "Review 结果直接显示在当前对话或交互流中。",
            ("review_profiles.*.delivery", "detached") => "Review 结果以独立报告或 artifact 形式输出，便于归档。",
            ("feedback.upload", "off") => "不上传反馈，只保留本地记录或完全关闭。",
            ("feedback.upload", "ask") => "每次上传反馈前询问用户。",
            ("feedback.upload", "enabled") => "允许按配置自动上传脱敏反馈。",
            ("imports.*.mode", "read-only") => "只读取外部配置作为参考，不把变更写回外部来源。",
            ("imports.*.mode", "migration") => "把外部配置作为迁移输入，转换为 TianShu 原生配置后使用。",
            ("imports.*.mode", "mirror") => "把外部配置作为镜像同步来源，需要特别注意冲突和覆盖策略。",
            ("stage_registry.stages.*.context_projection_mode", "summary") => "只向目标 Stage 投送会话摘要，适合低成本规划、状态判断和轻量交接。",
            ("stage_registry.stages.*.context_projection_mode", "selected_segments") => "按 Stage 需求投送筛选后的上下文片段，适合常规执行与审查链路。",
            ("stage_registry.stages.*.context_projection_mode", "full_within_budget") => "在预算允许范围内投送尽可能完整的上下文，适合高风险修改或需要全局一致性的阶段。",
            ("stage_registry.stages.*.context_projection_mode", "references_only") => "只投送引用、索引或定位信息，由目标 Stage 按需回取内容，适合大工程和低负担交接。",
            ("kernel.strategy.promotion_gate", "disabled") => "禁止策略自动晋升；适合只允许人工或固定策略运行的环境。",
            ("kernel.strategy.promotion_gate", "human_required") => "策略晋升必须经过人工确认；适合默认生产安全边界。",
            ("kernel.strategy.promotion_gate", "policy_required") => "策略晋升必须满足治理策略声明；适合自动化验收和可审计策略实验。",
            ("execution.profiles.*.side_effect_ceiling", "none") => "不允许任何外部 side effect，仅适合纯计算或纯投影步骤。",
            ("execution.profiles.*.side_effect_ceiling", "read_only") => "只允许读取环境、配置或上下文，不允许写入工作区或访问外部可变系统。",
            ("execution.profiles.*.side_effect_ceiling", "workspace_write") => "允许在治理 envelope 授权范围内写入工作区。",
            ("execution.profiles.*.side_effect_ceiling", "network") => "允许在治理 envelope 授权范围内访问网络。",
            ("execution.profiles.*.side_effect_ceiling", "external_write") => "允许在治理 envelope 授权范围内写入外部系统；必须谨慎使用。",
            ("modules.*.trust_level", "trusted") => "模块来源被系统或用户显式信任，仍需遵守治理 envelope。",
            ("modules.*.trust_level", "workspace") => "模块来源于当前工作区，默认按工作区信任策略约束。",
            ("modules.*.trust_level", "prompt") => "模块只能作为只读提示或建议来源，不应直接产生写入 side effect。",
            ("modules.*.trust_level", "untrusted") => "模块来源不受信任，默认限制能力并要求更强校验。",
            ("modules.*.trust_level", "blocked") => "模块被阻止参与正式运行，只能保留诊断或迁移信息。",

            _ => null,
        } ?? string.Empty;

        return !string.IsNullOrWhiteSpace(description);
    }

    private static string DescribeReasoningActivation(string value) => value switch
    {
        "auto" => "由 TianShu 根据 provider、模型和协议能力自动决定如何传递 reasoning/thinking 参数；推荐默认使用。",
        "none" => "不向 provider 发送 reasoning/thinking 相关参数，即使模型支持也不主动启用。",
        "implicit" => "不显式传递 reasoning 参数，依赖模型或 provider 自身默认行为。",
        "openai_responses" => "把推理配置映射为 OpenAI Responses API 的 reasoning 字段。",
        "anthropic_thinking" => "把推理配置映射为 Anthropic Messages API 的 thinking 字段。",
        "google_thinking_config" => "把推理配置映射为 Gemini thinkingConfig。",
        "openai_compatible_extra_body" => "通过 OpenAI 兼容接口的 extra body 扩展字段传递推理参数。",
        "provider_native" => "交给 provider adapter 使用原生方式传递推理参数。",
        _ => FallbackDescription,
    };

    private static string DescribeReasoningEffort(string value) => value switch
    {
        "minimal" => "尽量少用推理预算，响应最快，但复杂任务质量可能下降。",
        "low" => "使用较低推理强度，适合简单问答、轻量编辑和低成本场景。",
        "medium" => "使用中等推理强度，适合大多数日常任务。",
        "high" => "使用较高推理强度，适合复杂代码、架构分析或多步骤规划。",
        "xhigh" => "使用额外高推理强度，适合特别复杂且值得消耗更多时间和成本的任务。",
        _ => FallbackDescription,
    };

    private static string DescribeReasoningSummary(string value) => value switch
    {
        "off" => "不请求推理摘要，输出更简洁，也减少额外 token 消耗。",
        "auto" => "由 provider 或模型决定是否返回推理摘要。",
        "concise" => "请求简短推理摘要，只保留关键判断线索。",
        "detailed" => "请求更详细的推理摘要，便于调试和审计，但成本更高。",
        _ => FallbackDescription,
    };

    private static string DescribeVerbosity(string value) => value switch
    {
        "low" => "倾向更短输出，只给关键结论。",
        "normal" or "medium" => "使用常规详略，兼顾可读性和信息量。",
        "high" => "倾向更完整输出，适合需要解释、步骤和背景的任务。",
        _ => FallbackDescription,
    };

    private static string DescribeModelLock(string value) => value switch
    {
        "off" => "不锁定模型；后续配置变化可以影响新建或继续的会话。",
        "snapshot-on-create" => "创建会话时记录模型快照，避免会话中途被全局配置变化影响。",
        _ => FallbackDescription,
    };

    private static string DescribeTrust(string value) => value switch
    {
        "local" => "只信任当前本机范围，不代表跨设备或团队环境可信。",
        "trusted" => "按受信任对象处理，允许进入正常能力和权限流程。",
        "untrusted" => "按不受信任对象处理，默认更保守，风险操作会被限制或要求确认。",
        _ => FallbackDescription,
    };

    private static string DescribeToolImplementationKind(string value) => value switch
    {
        "managed" => "使用 TianShu 进程内托管实现，适合默认内置工具或可信扩展程序集。",
        "externalprocess" => "通过外部进程执行工具实现，适合隔离运行、跨语言工具或企业 runner。",
        "providerhosted" => "工具由模型 provider 或远端平台托管，TianShu 仍保留 catalog、治理和审计边界。",
        "mcpstdio" => "通过 MCP stdio server 提供工具能力，适合本地 MCP 工具包。",
        "mcphttp" => "通过 MCP HTTP 或等价远端连接提供工具能力，适合服务化 MCP 工具。",
        "platformnative" => "调用当前平台或宿主原生能力，适合 OS、IDE 或 sidecar 暴露的工具实现。",
        "unavailable" => "当前工具实现不可用；通常只出现在由运行时 catalog 导出的审阅模板中，不建议手动选择。",
        _ => FallbackDescription,
    };

    private static string DescribeToolProviderType(string value) => value switch
    {
        "assembly" => "从本地 .NET 程序集加载工具 provider，适合当前阶段的显式扩展入口。",
        "package" => "从包管理来源解析工具 provider；当前作为配置语义保留，具体包解析由后续实现接入。",
        "plugin" => "从 TianShu plugin 系统装载工具 provider，适合和插件生命周期绑定的扩展。",
        _ => FallbackDescription,
    };

    private static string DescribeMemoryMode(string value) => value switch
    {
        "read-write" => "允许读取历史记忆，也允许在策略通过后写入新记忆。",
        "read-only" => "只读取已有记忆，不写入新记忆；适合临时任务或审慎模式。",
        "ephemeral" => "只在当前会话内临时使用记忆，不沉淀为长期记忆。",
        "disabled" => "完全禁用该会话的记忆读写。",
        _ => FallbackDescription,
    };

    private static string DescribeMemoryScope(string value) => value switch
    {
        "user" => "面向当前用户的长期偏好、习惯和稳定事实。",
        "workspace" => "仅作用于当前工作区或项目目录。",
        "team" => "面向团队共享的规则、约定和知识。",
        "session" => "只作用于当前会话，不应跨会话传播。",
        "agent" => "绑定到特定 agent 角色或能力。",
        "collaboration" => "绑定到协作空间，用于多人或多 agent 工作流。",
        _ => FallbackDescription,
    };

    private static string DescribeMemoryProviderMode(string value) => value switch
    {
        "read-only" => "只从该 provider 读取记忆，不向它写入。",
        "read-write" => "允许读取和写入该 provider，是完整双向记忆后端。",
        "mirror" => "作为镜像同步目标或来源，重点保持与外部系统一致。",
        "import-export" => "只用于导入或导出交换，不作为实时记忆后端。",
        _ => FallbackDescription,
    };

    private static string DescribeDiagnosticCollectionLevel(string value) => value switch
    {
        "off" => "关闭诊断采集；最少磁盘占用，但排查问题时证据最少。",
        "summary" => "只记录摘要级事件，例如阶段、耗时和结果。",
        "stats" => "记录结构化统计事件，便于分析上下文、工具调用和 provider 请求。",
        "artifact" => "在统计事件之外保留可审计工件，例如脱敏后的 provider 请求或上下文报告。",
        "verbose" => "记录更细的调试细节；适合定位问题，但文件更多、隐私审查要求更高。",
        _ => FallbackDescription,
    };

    private static string DescribeDiagnosticLogLevel(string value) => value switch
    {
        "trace" => "记录最细粒度事件，适合深度排障，噪音最大。",
        "debug" => "记录调试信息，适合开发阶段排查问题。",
        "info" => "记录常规运行信息，适合默认使用。",
        "warn" => "只记录警告和错误，减少日志量。",
        "error" => "只记录错误，日志最少但上下文也最少。",
        _ => FallbackDescription,
    };

    private static string DescribeHostSurface(string value) => value switch
    {
        "cli" => "当前由命令行界面消费 TianShu 能力。",
        "sidecar" => "当前由旁路宿主进程消费能力，通常服务于其他前端或集成。",
        "vsix" => "当前由 Visual Studio 扩展消费能力。",
        "service" => "当前由后台服务或长驻进程消费能力。",
        _ => FallbackDescription,
    };

    private static string DescribeControlPlane(string value) => value switch
    {
        "local" => "使用本地控制平面，配置、状态和能力编排主要在本机完成。",
        "remote" => "连接远端控制平面，由远端服务统一管理部分状态或策略。",
        "sidecar" => "通过 sidecar 进程提供控制平面，适合桌面 GUI、IDE 或其他宿主复用。",
        _ => FallbackDescription,
    };

    private static string DescribeReviewTarget(string value) => value switch
    {
        "uncommitted-changes" => "默认审查当前未提交改动，适合提交前自检。",
        "base-branch" => "默认审查相对基线分支的差异，适合 PR 或分支评审。",
        "commit" => "默认审查指定提交，适合回看某次变更。",
        "custom" => "由用户手动指定 review 范围。",
        _ => FallbackDescription,
    };
}
