# TianShu 自适应内核可行性验收计划

## 1. 文档定位

本文是 TianShu 自适应、自进化 Kernel 的可行性验收计划。它用于指导独立 spike 分支上的验证工作，目标是回答一个问题：

> 当前以 `StageGraph`、`KernelOperation`、`RuntimeStep`、`AdaptiveOrchestrator` 和 `StrategyRegistry` 为核心的设计，是否足以支撑一个可控、可审计、可回退的自适应内核。

本文不是正式架构实现文档，也不替代 `docs/tianshu-architecture-spec.md`。它的职责是把可行性拆成可执行、可证伪、可审计的验收命题，并规定每个命题的证据、判死线和后续决策。

### 1.1 使用方式

- 本文作为 spike 分支的验收基线。
- spike 分支允许新增验证测试、一次性 harness、统计脚本、映射文档和证据输出。
- spike 分支不应继续推进正式架构收敛，不应把验证代码直接当作生产实现。
- 任一前置命题被判死时，后续依赖命题暂停，先回写结论并调整正式架构设计。
- 验证通过只表示该命题在当前实验范围内成立，不表示完整自适应内核已经完成。

### 1.2 术语

- **Stable Kernel Core**：不可绕过的内核安全与编排边界，负责验证 intent、proposal、graph、operation 和 runtime step。
- **Adaptive Orchestration Layer**：允许 AI 提出 stage、graph、route、tool strategy、checkpoint、recovery 和 strategy transition 的建议层。
- **StageGraph**：Kernel 层的编排 IR，用于描述 stage、边、policy、budget、checkpoint、recovery 和 evaluation。
- **RuntimeStep**：Execution Runtime 可执行的最小正式步骤。
- **Capability ToolUse**：AI 触达外部世界或模块能力的受控能力调用。
- **KernelTool**：AI 影响内核编排策略时调用的受控内核工具。
- **判死线**：一旦命中即认为该命题不成立，必须停止或降级相关设计。
- **delta**：候选策略相对 baseline 的可测改进。
- **LLM 噪声方差**：同任务、同策略、同模型多次运行时自然产生的 success、cost、latency、trace shape 等波动。

## 2. 当前代码基线

本计划以当前代码为验证基底，不以设计文档的未来形态为前提。

### 2.1 涉及项目

| 项目 | 当前验证职责 |
| --- | --- |
| `src/Contracts/TianShu.Contracts.Kernel` | `CoreIntent`、`GovernanceEnvelope`、`StageGraph`、`KernelOperation`、`KernelProposal`、`KernelTrace`、strategy lifecycle 契约。 |
| `src/Core/TianShu.Kernel.Abstractions` | `IStableKernelCore`、`IKernelValidator`、`IStageGraphInterpreter`、`IAdaptiveOrchestrator`、`IKernelEvaluator`、`IStrategyRegistry` 抽象。 |
| `src/Core/TianShu.Kernel` | `StableKernelCore`、`KernelValidator`、`StageGraphInterpreter`、默认 `StageGraph`、Kernel 状态机和 trace emission。 |
| `src/Core/TianShu.Kernel.Adaptive` | Adaptive Orchestration Layer 与 KernelTool 家族。 |
| `src/Core/TianShu.Kernel.Strategies` | Strategy registry、evaluation、replay compatibility、promotion / rollback。 |
| `src/Contracts/TianShu.Contracts.Execution` | `ExecutionPlan`、`RuntimeStep`、`RuntimeStepResult`、`ExecutionRuntimeContext`。 |
| `src/Execution/TianShu.Execution.Runtime` | `IExecutionRuntime`、`TianShuExecutionRuntime`、`ValidateStep`、provider/tool/memory/artifact/diagnostics/workspace bridge。 |
| `src/Hosting/TianShu.AppHost.Tools.Runtime` | 当前真实 turn loop、tool continuation、steer、interrupt、subagent 相关运行时事实来源。 |
| `src/Presentations/TianShu.Cli` | 本阶段唯一需要关注的消费层入口。 |

### 2.2 涉及测试项目

| 测试项目 | 当前验证职责 |
| --- | --- |
| `tests/TianShu.Contracts.Kernel.Tests` | Kernel 契约、默认值、序列化和 fail-closed 默认行为。 |
| `tests/TianShu.Kernel.Abstractions.Tests` | Kernel 抽象依赖边界。 |
| `tests/TianShu.Kernel.Tests` | `KernelValidator`、`StableKernelCore`、`StageGraphInterpreter`、state machine。 |
| `tests/TianShu.Kernel.Adaptive.Tests` | Adaptive layer 不越过 proposal 边界，不直接产出 `RuntimeStep`。 |
| `tests/TianShu.Kernel.Strategies.Tests` | Strategy lifecycle、evidence、evaluation、rollback。 |
| `tests/TianShu.Execution.Runtime.Tests` | `RuntimeStep` 执行、`ValidateStep` 和 bridge envelope 校验。 |
| `tests/TianShu.Execution.Integration.Tests` | 端到端边界、架构依赖守护和 AppHost / RuntimeComposition 迁移边界。 |
| `tests/TianShu.AppHost.Tests` | 当前 AppHost runtime 行为事实来源，后续只作为迁移输入和回归证据。 |

### 2.3 已知前提

- `StageGraph` 的契约、默认 graph、validator 和 interpreter 已存在。
- `StageGraphInterpreter` 当前默认把每个 stage 物化为 `ModuleCapabilityStep`，属于 skeleton，而不是完整真实 turn loop。
- 当前真实 turn loop 仍主要位于 `TianShu.AppHost.Tools.Runtime`。
- `TianShuExecutionRuntime.ValidateStep` 已作为 Execution Runtime 的 runtime step governance 校验入口。
- provider、tool、memory、artifact、diagnostics、workspace bridge 当前应复用 `ValidateStep`，但需要以测试证明。
- 自适应 graph 生成、graph patch、自动 strategy promotion 尚未被证明可行。

## 3. 分支和产物规则

### 3.1 分支

建议从当前主线切出独立 spike 分支：

```text
codex/adaptive-kernel-feasibility-spike
```

分支创建前应确认当前工作区只包含预期文档变更或用户提供的未跟踪审计文件。若 `docs/audit/tianshu-architecture-feasibility-audit.md` 仍是用户提供的未跟踪文件，不应在未确认前把它纳入提交。

### 3.2 允许的 spike 产物

- 验证测试：
  - `tests/TianShu.Kernel.Tests`
  - `tests/TianShu.Execution.Runtime.Tests`
  - 必要时新增 `tests/TianShu.Kernel.Feasibility.Tests` 或等价 spike 测试项目。
- 一次性 harness：
  - 可放在 `tests` 下，或放在 `tools` 下并带有 `Feasibility` 命名。
  - 不作为正式产品入口。
- 证据文档：
  - `docs/audit/evidence/adaptive-kernel-feasibility/`
  - 每轮实验应输出一份简短报告和可追踪原始结果。
- 映射文档：
  - 用于记录真实 turn 到 StageGraph / RuntimeStep 的实例映射。
- 统计输出：
  - JSON、CSV 或 Markdown 表格均可，但必须能复现统计口径。

### 3.3 不允许的行为

- 不在 spike 分支上继续大规模正式重构。
- 不把未验证的 spike 类型直接提升为正式 contract。
- 不因为 C 或 D 的实验需要而放宽 B 的安全门。
- 不把 AI 生成 graph 的成功个例当作可行性证明。
- 不把 `StageGraph` 已有 skeleton 当作真实 turn loop 已完成。
- 不在代码清单未完成前打包或安装三件套，除非某个验证命题必须依赖安装产物，并在执行前说明原因。
- 不修改用户级 TianShu 或 Codex 配置。

### 3.4 证据命名

每个命题的证据目录建议使用：

```text
docs/audit/evidence/adaptive-kernel-feasibility/<YYYYMMDD-HHmm>-<phase>/
```

每个目录至少包含：

- `README.md`：本轮验证摘要。
- `commands.md`：执行过的命令和关键参数。
- `results.json` 或 `results.md`：原始结果或结果表。
- `decision.md`：通过、失败、降级或暂停的明确结论。

## 4. 总体验证阶梯

最终验证顺序固定为：

```text
B -> A -> Baseline -> C -> D
```

含义如下：

| 阶段 | 命题 | 作用 | 后续依赖 |
| --- | --- | --- | --- |
| B | 安全门关得住 | 证明 AI 不能越过治理边界产生副作用。 | A、Baseline、C、D |
| A | 真实 turn 能被表达 | 证明 StageGraph / RuntimeStep 能承载或明确不能承载真实 agent loop。 | Baseline、C、D |
| Baseline | 固定非自适应路径可跑 | 证明不靠自适应也能跑通验收子集，并测出 LLM 噪声方差。 | C、D |
| C | LLM 能稳定产合法 graph / patch | 证明自适应 graph 生成不是伪命题。 | D |
| D | 策略演化有正收益 | 证明自动 promotion / rollback 能超过 baseline 噪声。 | 无 |

不得跳过 B。不得在没有 A 结论的前提下解释 C 的价值。不得在没有 Baseline 方差的前提下做 D。

## 5. 阶段 B：安全门关得住

### 5.1 目标

证明 Stable Kernel Core、Execution Runtime 和所有执行 bridge 都采用 fail-closed 策略，且任何 AI 生成或请求的越权对象都不能进入实际执行。

### 5.2 范围

B 阶段覆盖：

- `IKernelValidator.ValidateIntentAsync`
- `IKernelValidator.ValidateProposalAsync`
- `IKernelValidator.ValidateGraphAsync`
- `IKernelValidator.ValidateOperationAsync`
- `IKernelValidator.ValidateRuntimeStepAsync`
- `TianShuExecutionRuntime.ExecuteAsync`
- `TianShuExecutionRuntime.ExecuteStepAsync`
- `TianShuExecutionRuntime.ValidateStep`
- Execution Runtime bridge：
  - `ExecutionRuntimeProviderBridge`
  - `ExecutionRuntimeToolBridge`
  - `ExecutionRuntimeMemoryModuleBridge`
  - `ExecutionRuntimeArtifactModuleBridge`
  - `ExecutionRuntimeDiagnosticsModuleBridge`
  - `ExecutionRuntimeWorkspaceModuleBridge`
  - `ExecutionRuntimeModelRouteBridge`
  - `ExecutionRuntimeContextPolicyBridge`

### 5.3 对抗语料

至少构造以下负例：

| 类别 | 负例 | 预期 |
| --- | --- | --- |
| Intent | 缺 `GovernanceEnvelope` | Kernel 拒绝。 |
| Intent | `CoreIntentKind.Unspecified` | Kernel 拒绝。 |
| Graph | 空 stage 列表 | Kernel 拒绝。 |
| Graph | entry stage 不存在 | Kernel 拒绝。 |
| Graph | stage id 重复 | Kernel 拒绝。 |
| Graph | edge 指向不存在 stage | Kernel 拒绝。 |
| Graph | 从 entry 不可达 stage | Kernel 拒绝。 |
| Graph | 无终态 stage | Kernel 拒绝。 |
| Graph | 无界循环且没有 bounded recovery | Kernel 拒绝。 |
| Graph | graph budget 为零或无界 | Kernel 拒绝。 |
| Graph | graph side effect 超过 governance | Kernel 拒绝。 |
| Graph | graph allowed tool 不在 governance allow-list | Kernel 拒绝。 |
| Graph | graph allowed module 不在 governance allow-list | Kernel 拒绝。 |
| Stage | stage side effect 为 `Unspecified` | Kernel 拒绝。 |
| Stage | stage budget 无界 | Kernel 拒绝。 |
| Stage | fail-closed model route 无候选 | Kernel 拒绝。 |
| Stage | context policy fail-closed 但 max tokens 非正数 | Kernel 拒绝。 |
| Operation | source stage mismatch | Kernel 拒绝。 |
| Operation | side effect 为 `Unspecified` | Kernel 拒绝。 |
| Operation | side effect 超过 stage | Kernel 拒绝。 |
| Operation | capability tool 不在 stage allow-list | Kernel 拒绝。 |
| RuntimeStep | 缺 `StepId` | Execution Runtime 拒绝。 |
| RuntimeStep | 缺 `SourceIntentId` | Execution Runtime 拒绝。 |
| RuntimeStep | 缺 `SourceGraphId` | Execution Runtime 拒绝。 |
| RuntimeStep | 缺 `SourceStageId` | Execution Runtime 拒绝。 |
| RuntimeStep | 缺 `SourceKernelOperationId` | Execution Runtime 拒绝。 |
| RuntimeStep | 缺 `PermissionEnvelope` | Execution Runtime 拒绝。 |
| RuntimeStep | 缺 `SideEffectProfile` | Execution Runtime 拒绝。 |
| RuntimeStep | side effect 为 `Unspecified` | Execution Runtime 拒绝。 |
| RuntimeStep | 缺 `KernelBudget` | Execution Runtime 拒绝。 |
| RuntimeStep | 缺 `ExpectedOutputContract` | Execution Runtime 拒绝。 |
| RuntimeStep | 缺 `TracePolicy` | Execution Runtime 拒绝。 |
| RuntimeStep | side effect 超过 governance | Execution Runtime 拒绝。 |
| RuntimeStep | 需要 human gate 但 governance 未授予 | Execution Runtime 拒绝。 |
| RuntimeStep | 需要 human gate 但缺 `ApprovalIds` | Execution Runtime 拒绝。 |
| Tool Step | `CapabilityToolId` 不在 governance allow-list | Execution Runtime 拒绝。 |
| Tool Step | `InputEnvelope.ToolId` 不在 governance allow-list | Execution Runtime 拒绝。 |
| Model Step | `ProviderModuleId` 不在 governance allow-list | Execution Runtime 拒绝。 |
| Model Step | `ModelRoutePolicy.PolicyId` 不在 governance policy ids | Execution Runtime 拒绝。 |
| Module Step | `ModuleId` 不在 governance allow-list | Execution Runtime 拒绝。 |
| Bridge | 传入越权 step | bridge 必须返回 blocked，不调用底层 module/tool/provider。 |

### 5.4 Bridge 复用验收

每个 bridge 必须满足：

- 进入底层 provider、tool、module、resolver、diagnostics sink 前调用统一 runtime step governance 校验。
- 校验失败时返回 blocked result 或等价拒绝结果。
- 校验失败时不调用底层实现。
- 校验失败时输出可审计 failure code。
- 不存在 bridge 自己复制一份较弱规则后绕过 `ValidateStep` 的路径。

建议测试模式：

1. 为每个 bridge 构造一个 fake 底层实现，fake 被调用时记录 `Called = true`。
2. 传入越权 `RuntimeStep`。
3. 断言结果为 blocked。
4. 断言 fake 底层实现未被调用。
5. 断言 failure code 与 `ValidateStep` 输出一致或可映射。

### 5.5 正例对照

B 阶段不能只证明“该拒的能拒”，还必须证明“该过的能过”。否则 validator 可能因为过度保守而让系统安全但不可用。

至少构造以下正例：

| 类别 | 正例 | 预期 |
| --- | --- | --- |
| Intent | 携带合法 `GovernanceEnvelope` 的 `TurnIntent` | Kernel 批准。 |
| Graph | 单入口、至少一终态、预算有界、policy 在 governance 内的 `StageGraph` | Kernel 批准。 |
| Operation | source stage 匹配、side effect 不超过 stage、capability 在 allow-list 内的 `RequestCapabilityCallOperation` | Kernel 批准。 |
| RuntimeStep | source ids 完整、permission / side effect / budget / trace policy 完整且不超过 governance 的 step | Execution Runtime 批准。 |
| Tool Step | `CapabilityToolId` 与 `InputEnvelope.ToolId` 均在 governance allow-list 内 | Execution Runtime 批准。 |
| Model Step | `ProviderModuleId` 在 governance module allow-list 内，route policy 被允许 | Execution Runtime 批准。 |
| Module Step | `ModuleId` 在 governance module allow-list 内 | Execution Runtime 批准。 |
| Bridge | 传入合法 step | bridge 必须调用底层 fake，并返回成功或底层 fake 的受控结果。 |

正例与负例必须成对出现。每个负例至少对应一个只改动该非法字段的正例，用来证明失败来自目标约束，而不是 fixture 本身无效。

### 5.6 通过标准

B 阶段通过必须满足：

- 上述负例全部 fail closed。
- 上述正例全部 approved 或进入底层 fake。
- 所有 bridge 都证明复用 runtime step governance 校验。
- 没有任何负例能进入底层真实执行。
- 没有合法对象被 validator 无理由拒绝。
- 失败结果包含可审计 reason。
- 对抗测试进入常规测试套件或被明确登记为 spike 测试证据。

### 5.7 判死线

任一命中即 B 不通过：

- 任一越权 graph、operation 或 runtime step 被批准。
- 任一 bridge 在 governance 失败后仍调用底层 provider、tool 或 module。
- 任一合法 intent、graph、operation、runtime step 或 bridge call 在无明确原因时被拒绝。
- 任一 bridge 没有可审计失败结果。
- 任一越权路径只能靠调用方自觉，不由 Kernel / Execution Runtime 强制拒绝。

### 5.8 B 失败后的决策

B 失败时：

- 暂停 A、Baseline、C、D。
- 先补 validator、`ValidateStep` 或 bridge fail-closed 行为。
- 不允许在 B 未通过时上线任何自适应能力。

## 6. 阶段 A：真实 turn 能被表达

### 6.1 目标

证明真实 agent turn 能被当前 Kernel / Execution 模型表达；若不能，必须明确缺口，决定 StageGraph 保留、降级或重设计的方向。

A 阶段拆成两个独立门槛：

- A1：StageGraph 粒度是否合理。
- A2：`steer / interrupt / resume / subagent` 是否能映射进状态机或明确的 runtime loop。

### 6.2 输入案例

取 `docs/天枢最终验收案例.md` 中的核心能力子集，构造一条真实 turn 轨迹：

1. 用户发起软件交付任务。
2. 模型进行一次或多次 provider 调用。
3. 模型触发至少一次工具调用。
4. 发生中途 steer，要求在下一次模型工具调用边界后生效。
5. 发生 interrupt，旧 turn 停止尾流。
6. 用户发送 resume / 改向消息。
7. 至少发生一次 subagent spawn / wait / result 汇总。
8. 最终产生可审计 trace、projection 和结果。

不要求在 A 阶段跑通完整 WPF 最终验收，但必须覆盖上述语义。

### 6.3 A1：StageGraph 粒度验收

A1 必须回答以下问题：

- 一次 model invocation 是一个 stage，还是一个 runtime step？
- 一次 model turn 包含 N 次 tool call 时，对应几个 stage？
- stage 是粗粒度任务阶段，还是逐工具调用的细粒度节点？
- graph 是运行前固定，还是运行中可增量 patch？
- 如果 graph 运行中增长，增长动作由谁发起、谁验证、谁提交？
- per-tool governance 落在 StageGraph、KernelOperation、RuntimeStep，还是 bridge？
- StageGraph 是否能表达 conditional / recovery / retry，并且不把真实循环藏在 stage 内部？

### 6.4 A1 产物

A1 必须产出：

- `CoreIntent` 实例。
- `StageGraph` 实例图，以及可构造为真实 contract 对象的 `StageGraph` fixture。
- 每个 `StageNode` 的职责说明。
- 每个 `KernelOperation` 的来源 stage、side effect、tool/module allow-list。
- 每个 `RuntimeStep` 的 step kind、source ids、permission、side effect、budget、trace policy。
- `ExecutionPlan` 顺序或分支关系。
- graph 与实际 turn loop 的一一映射表。
- 延迟分析：哪些步骤是进程内同步校验，哪些步骤会触发模型调用，哪些步骤会触发外部副作用。

手工构造的 `StageGraph` fixture 必须调用真实 `IKernelValidator.ValidateGraphAsync` 验证。若 fixture 不能通过 validator，A1 只能记为“概念映射完成”，不得记为“当前代码基线可表达”。

### 6.5 A1 通过标准

A1 通过必须满足：

- 映射表能覆盖真实 turn 的主要执行路径。
- 手工构造的 `StageGraph` fixture 能通过真实 `IKernelValidator.ValidateGraphAsync`。
- StageGraph 不只是固定 `module.core_loop` shell。
- per-tool governance 不依赖 stage 内部隐式约定。
- RuntimeStep 与 graph / stage / operation 来源能追踪。
- 延迟模型能解释交互式编码 agent 的成本。

### 6.6 A1 判死线

任一命中即 A1 不通过：

- 结论只能是“一次模型调用动态生成一个 stage”，且 graph 没有额外约束价值。
- 手工构造的 `StageGraph` fixture 无法通过真实 validator，且失败原因不是 fixture 拼写或测试构造错误。
- 真实 tool loop 全部藏在单个 stage 内，StageGraph 对 per-tool governance 没有贡献。
- StageGraph 不能表达 recovery / retry / conditional，又没有明确替代层。
- 映射后每次普通工具调用都需要额外模型往返，导致交互成本不可接受。

### 6.7 A2：steer / interrupt / resume / subagent 验收

A2 必须回答以下问题：

- steer 的插入边界是什么？
- steer 消息如何进入正在运行的 turn？
- steer 生效后，旧 provider response、follow-up input 和 tool continuation 如何关联？
- interrupt 如何取消正在运行的 provider 或 tool？
- interrupt 后旧 turn 的 terminal state 是什么？
- interrupt 后的尾流输出如何停止或标记为废弃？
- resume 是新的 `CoreIntent`，还是旧 run 的恢复 transition？
- subagent spawn 是 `RuntimeStep`、嵌套 `KernelRun`、`HostInteractionStep`，还是 module capability？
- subagent wait / close / result aggregation 的 trace 如何回到主 run？

### 6.8 A2 可接受结论

A2 有三种可接受结论：

| 结论 | 含义 | 后续 |
| --- | --- | --- |
| A2 通过 | 当前状态机和 RuntimeStep 模型能表达 steer / interrupt / resume / subagent。 | 继续 Baseline。 |
| A2 部分通过 | 不能完全映射，但缺口明确，能输出 runtime loop design delta。 | 继续 Baseline，但必须标注范围限制。 |
| A2 不通过 | 无法映射，也无法定义缺口。 | 停止后续验证，先补 runtime loop 专项设计。 |

### 6.9 Runtime loop design delta

当 A2 部分通过时，必须输出一份 delta，至少包括：

- 当前代码中真实行为所在类型。
- 当前 Kernel / Execution 模型缺失的状态、事件、step 或 trace 字段。
- 需要新增的契约或状态机迁移。
- 该缺口是否影响最终验收案例。
- Baseline 阶段如何绕过或缩小范围。
- 哪些结论不能用该 Baseline 证明。

### 6.10 A 阶段最终决策

A 阶段结束时必须选择以下方向之一：

| 方向 | 条件 | 架构影响 |
| --- | --- | --- |
| 保留 StageGraph 为正式自适应 IR | A1 通过，A2 通过或可由明确 delta 补齐。 | 后续继续 C / D。 |
| StageGraph 降级为粗粒度 run shell | A1 不完全通过，但 StageGraph 对 checkpoint、recovery、trace 有价值。 | AI 不直接产完整 graph，主要在 stage 内选策略。 |
| 砍掉大部分 graph 层 | A1 判死，且 graph 没有治理或执行价值。 | 转向 RuntimeStep + reactive turn interpreter。 |
| 暂停自适应设计 | A2 无法映射且无法定义 delta。 | 先补 runtime loop 专项设计。 |

## 7. Baseline：固定非自适应路径和方差测量

### 7.1 目标

证明不启用自适应时，当前骨架至少能跑通核心验收子集；同时测量同一任务、同一策略、同一模型的自然方差，为后续 C 和 D 提供比较尺。

### 7.2 Baseline 执行路径声明

Baseline 开始前必须先声明实际执行路径，不能默认“固定非自适应路径”已经等价于 Kernel 路径。

执行路径只能选择以下之一：

| 路径 | 定义 | 能证明 | 不能证明 |
| --- | --- | --- | --- |
| `apphost-current-loop` | 复用当前 `TianShu.AppHost.Tools.Runtime` 真实 turn loop，只关闭自适应与自动晋升。 | 当前宿主路径能跑真实 provider/tool/steer/interrupt/subagent 子集；可测 LLM 方差。 | 不能证明 `StageGraph` / `StageGraphInterpreter` 能承载真实 turn loop；不能作为 C/D 的完整 Kernel 路径证据。 |
| `kernel-interpreter-loop` | 使用 `StableKernelCore`、`StageGraphInterpreter`、`ExecutionPlan` 和 `RuntimeStep` 跑固定 graph。 | Kernel 路径能否承载真实执行；可直接验证 StageGraph 作为 IR 的价值。 | 若当前 skeleton 跑不动，失败只能证明实现不足，不直接证明概念不可行。 |
| `hybrid-kernel-shell` | Kernel 生成/验证 coarse-grained graph，真实 turn loop 仍由现有 runtime bridge 承接，并完整登记 delta。 | Kernel 可作为粗粒度治理 shell；可验证哪些语义仍需 runtime loop 专项设计。 | 不能证明 StageGraph 已经完整表达 per-tool reactive loop。 |

Baseline 报告必须写明：

- `baselineExecutionPath`。
- 为什么选择该路径。
- 该路径覆盖了哪些最终验收能力。
- 该路径没有覆盖哪些 Kernel / StageGraph 能力。
- C 和 D 使用该 Baseline 作为对照时的有效范围。

若无法选择路径，Baseline 不得开始。若选择 `apphost-current-loop`，后续 C/D 的结论必须标注为“对照有效性受限”，不能据此宣称 Kernel StageGraph 路径已可行。

### 7.3 Baseline 范围

Baseline 应满足：

- 使用固定 graph 或 A 阶段确认的固定 turn interpreter。
- 不允许 AI 生成 `StageGraph`。
- 不允许 AI 生成 graph patch。
- 不允许自动 promotion / rollback。
- AI 只在已授权 stage / runtime step 内选择工具或生成内容。
- 所有外部副作用都必须可追踪到 `RuntimeStep` 或明确登记为当前未迁移 bridge。

### 7.4 Baseline 验收子集

最低应覆盖：

- 用户 turn 启动。
- provider 调用。
- 至少一次 tool call。
- tool output 回流模型。
- steer 在下一次工具调用边界后生效，或明确标注为 A2 delta。
- interrupt 能停止当前 turn，或明确标注为 A2 delta。
- resume / 改向消息进入新主线，或明确标注为 A2 delta。
- subagent spawn / result 汇总，或明确标注为 A2 delta。
- trace 能关联 run、turn、stage、operation、step 和 projection。

### 7.5 方差测量

Baseline 阶段必须前置测量 LLM 噪声方差，不得等到 D 才测。

建议任务集：

- `baseline-task-01`：只读分析任务。
- `baseline-task-02`：单工具调用任务。
- `baseline-task-03`：多工具连续调用任务。
- `baseline-task-04`：带 steer 的任务。
- `baseline-task-05`：带 interrupt / resume 的任务。
- `baseline-task-06`：带 subagent 的任务。

每个任务至少重复运行 5 次；若成本允许，建议 10 次。

每次记录：

- 是否成功。
- 总 latency。
- 模型调用次数。
- tool call 次数。
- token usage。
- 估算 cost。
- recovery 次数。
- validation rejection 次数。
- trace event 数量。
- trace shape hash。
- 是否出现非确定性结构差异。
- 人工判定质量分。

### 7.6 Trace canonicalization

记录 `trace shape hash` 前必须先定义 canonicalization 规则。否则 timestamp、非确定性 id、并发顺序和 provider event 微小差异会让 hash 失真。

最低 canonicalization 规则：

- 移除 timestamp、duration、随机 id、临时文件路径、机器相关路径和 provider request id。
- 将 run id、turn id、stage id、operation id、step id 映射为稳定序号。
- 对逻辑无序集合按 stable key 排序。
- 对并发事件保留 happens-before 关系；无法确定顺序的事件进入 unordered bucket。
- hash 输入必须只包含 event kind、normalized source refs、step kind、tool/module/provider ids、validation result、failure code 和 projection kind。
- 报告中必须保存 canonicalization 版本号，例如 `traceShapeCanonicalizationVersion`。

若没有 canonicalization，`traceShapeVariance` 只能作为人工参考，不得作为 D 阶段统计判据。

### 7.7 方差输出

Baseline 方差报告至少包含：

| 字段 | 含义 |
| --- | --- |
| `taskId` | 任务编号。 |
| `strategyId` | 固定策略编号。 |
| `modelRouteId` | 模型路线。 |
| `runCount` | 重复次数。 |
| `successRate` | 成功率。 |
| `latencyMeanMs` | 平均延迟。 |
| `latencyStdDevMs` | 延迟标准差。 |
| `tokenMean` | 平均 token。 |
| `tokenStdDev` | token 标准差。 |
| `costMean` | 平均成本。 |
| `costStdDev` | 成本标准差。 |
| `toolCallMean` | 平均工具调用数。 |
| `toolCallStdDev` | 工具调用数标准差。 |
| `traceShapeVariance` | trace 形状差异。 |
| `traceShapeCanonicalizationVersion` | trace shape hash 的规范化规则版本。 |
| `qualityMean` | 人工或自动质量分均值。 |
| `qualityStdDev` | 质量分标准差。 |

### 7.8 Baseline 通过标准

Baseline 通过必须满足：

- `baselineExecutionPath` 已声明，且证明范围和不能证明的范围写清楚。
- 固定非自适应路径能跑通验收子集。
- B 阶段安全门仍然有效。
- A2 未覆盖部分被明确标注为 delta，而不是被隐藏。
- 能产出可复盘 trace。
- 能产出 baseline 方差报告。
- `traceShapeVariance` 使用了明确 canonicalization 规则，或被降级为人工参考。
- 能明确判断后续 D 是否存在可检测改进空间。

### 7.9 Baseline 判死线

任一命中即 Baseline 不通过：

- 没有声明 `baselineExecutionPath`。
- 固定路径无法跑通最小 provider + tool loop。
- 真实副作用无法追踪到 `RuntimeStep` 或登记为迁移 bridge。
- trace 不足以重建关键决策。
- 同任务同策略运行的自然方差过大，导致任何合理策略 delta 都不可检测。
- A2 delta 覆盖了最终验收案例的大部分关键能力，使 Baseline 不能代表真实 agent loop。

### 7.10 Baseline 对后续的影响

- 如果 Baseline 跑不通，暂停 C 和 D，先修固定骨架。
- 如果 Baseline 跑通但方差过大，C 可以继续验证合法产图，但 D 自动 promotion 默认降级。
- 如果 Baseline 选择 `apphost-current-loop`，C 只能验证 graph 生成合法性，不能直接证明 Kernel 执行路径收益。
- 如果 Baseline 选择 `hybrid-kernel-shell`，D 只能比较 coarse-grained strategy，不得宣称 per-tool StageGraph evolution 已可行。
- 如果 Baseline 跑通且方差可控，继续 C。

## 8. 阶段 C：LLM 能稳定产合法 graph / patch

### 8.1 目标

验证 LLM 是否能在 schema、tool definition 和 validator 约束下，稳定生成合法的 `StageGraph` 或 graph patch。

### 8.2 范围

C 阶段是隔离实验：

- 使用 stub kernel。
- 使用真实 `StageGraph` 契约。
- 使用真实 `IKernelValidator`。
- 使用 `compose_stage_graph` / `revise_stage_graph` tool schema 或等价 schema。
- 不调用真实 provider bridge 之外的副作用工具。
- 不执行真实 shell、file write、artifact publish、memory mutation。
- 不进入 strategy promotion。

### 8.3 C0 prompt/schema 校准

C 阶段开始前必须先做 C0 校准，防止把 prompt 或 schema 表达质量问题误判为自适应架构不可行。

C0 至少包含三组对照：

| 组别 | 目的 | 要求 |
| --- | --- | --- |
| `schema-minimal` | 验证最小 schema 是否足以产出合法 graph。 | 只提供必填字段和少量约束。 |
| `schema-full` | 验证完整 schema 是否引入过多干扰。 | 使用接近正式 KernelTool 的完整 schema。 |
| `prompt-calibrated` | 验证合理 prompt 工程后的上限。 | 明确输出格式、常见 rejection、示例 graph、禁止项和修正策略。 |

C0 必须记录：

- prompt 版本。
- schema 版本。
- prompt/schema 迭代次数。
- 每次迭代的变更摘要。
- 示例 graph 是否提供。
- validator rejection 是否以结构化形式返回。
- 首次合法率。
- 3 轮 revise 后合法率。
- 每个 graph 的 token、latency 和模型调用次数。

prompt/schema 调整最多允许 3 轮。到达上限后仍不收敛时，`prompt-calibrated` 组记为失败；不得无限调 prompt 直到偶然达标。若需要超过 3 轮，必须把后续工作登记为 prompt/schema 设计改造，而不是继续计入 C0 验收。

只有在 `prompt-calibrated` 组仍然低合法率且不收敛时，才能把 C 失败解释为“AI 产 graph 不可行”。如果 `schema-minimal` 通过但 `schema-full` 失败，优先判定为 schema / prompt 设计需要收敛，而不是自适应内核不可行。

### 8.4 任务集

任务集应覆盖：

- 简单只读分析。
- 单工具调用。
- 多工具顺序调用。
- 带 recovery。
- 带 checkpoint。
- 带 context policy。
- 带 model route。
- 带 human gate。
- 带 subagent 抽象。
- 带 steer / interrupt / resume 抽象。

### 8.5 指标

C 阶段至少统计：

- prompt version。
- schema version。
- 首次产图合法率。
- 首次产 patch 合法率。
- validator rejection 类型分布。
- revise 后合法率。
- revise 平均轮数。
- revise 最大轮数。
- revise token mean / max。
- revise latency mean / max。
- revise model call mean / max。
- 修正震荡率。
- 输出字段缺失率。
- allow-list 越界率。
- side effect 越界率。
- budget 缺失率。
- graph 拓扑错误率。
- 跨模型差异。
- 与 Baseline 固定 graph 的复杂度差异。
- 与 Baseline 的 token / latency / model-call 增量。

### 8.6 修正循环

修正循环必须符合：

1. LLM 产出 graph 或 patch。
2. Kernel validator 拒绝或批准。
3. 若拒绝，只把结构化 rejection reason 返回给 LLM。
4. LLM 调用 revise。
5. 最多修正 N 轮，建议 N = 3。
6. N 轮后仍失败即记录为不收敛。

禁止人工手动修补 LLM 输出后计为通过。

### 8.7 成本门槛

C 阶段不仅验证“合法”，还必须验证“划算”。合法 graph 如果依赖过多 revise、过多模型调用或过高延迟，在交互式编码 agent 场景中仍然不可用。

最低成本门槛：

- 3 轮 revise 是硬上限，不得为了达标继续追加修正轮数。
- 平均 revise 轮数必须小于或等于 1.5，或必须解释为什么该任务集需要更高轮数。
- graph 生成和 revise 的 token / latency / model-call 增量必须与 Baseline 方差一起报告。
- 成本对照必须基于同一 `baselineExecutionPath`。若 Baseline 不是 Kernel 路径，graph 生成成本只能作为相对信号报告，不得作为 D 的 promotion 成本判据。
- 若在同一 `baselineExecutionPath` 下 graph 生成成本超过 Baseline 同任务总成本的 30%，不得进入 D 的自动 promotion，只能作为人工建议或离线策略候选。
- 若每次普通交互都需要重新产 graph，必须额外报告交互延迟影响；不能只按离线策略生成成本计算。

### 8.8 C 通过标准

C 通过必须满足：

- C0 prompt/schema 校准完成，且失败原因不是明显 prompt/schema 表达质量问题。
- C0 prompt/schema 迭代次数未超过上限，或已明确降级为 prompt/schema 设计改造。
- 首次合法率达到预设阈值，或 revise 收敛率达到预设阈值。
- 常见 rejection 可以通过结构化 reason 收敛。
- 没有高频不可收敛震荡。
- 合法 graph 不只是把所有行为塞进单个 `module.core_loop` stage。
- 合法 graph 的 revise 成本在可接受范围内。
- 合法 graph 在 Baseline 方差允许范围内有进入 D 的价值。

阈值建议：

| 指标 | 最低建议 |
| --- | --- |
| 首次合法率 | 60% |
| 3 轮 revise 后合法率 | 85% |
| 修正震荡率 | 小于 10% |
| 高风险越界首次出现率 | 小于 20%，且必须全部被 validator 拒绝 |
| 平均 revise 轮数 | 小于或等于 1.5，或有明确任务复杂度解释 |

阈值可按成本和模型能力调整，但调整必须写入证据报告。

### 8.9 C 判死线

任一命中即 C 不通过：

- C0 显示失败主要来自 schema / prompt 表达质量，但仍直接判定自适应不可行。
- C0 通过无限 prompt/schema 调参获得达标结果。
- 首次合法率低，且 revise 不收敛。
- LLM 高频生成越权 graph，且 rejection reason 无法纠正。
- 产出的 graph 总是退化为单 stage shell。
- graph 合法但无法解释真实 turn loop。
- graph 合法但 revise 成本在同一 `baselineExecutionPath` 下超过 Baseline 可接受范围。
- graph 合法但相对 Baseline 没有潜在收益空间。

### 8.10 C 失败后的决策

C 失败时：

- 不再把 AI 生成完整 StageGraph 作为核心路径。
- 可保留 AI 生成“人工审核建议”。
- 可保留固定 graph + AI 在 stage 内选择 tool strategy。
- 自动 graph patch 默认关闭。

## 9. 阶段 D：策略演化有正收益

### 9.1 目标

验证 strategy lifecycle、evaluation、promotion 和 rollback 是否能产生超过 Baseline 噪声方差的真实收益。

### 9.2 前置门控

D 只能在以下条件全部满足时执行：

- B 通过。
- A 通过，或 A2 delta 不影响 D 的任务范围。
- Baseline 跑通。
- Baseline 方差已测量。
- C 通过。
- 存在可 replay 或可比较的 trace。
- 存在稳定评分任务集。

### 9.3 候选策略

候选策略可以来自：

- LLM 生成的 graph patch。
- LLM 生成的 tool strategy。
- LLM 生成的 model route strategy。
- LLM 生成的 recovery plan。
- 人工编写但交由 strategy lifecycle 评估的策略。

每个候选策略必须包含：

- `strategyId`
- 来源 run / trace
- proposal id
- risk profile
- applicable task ids
- expected improvement
- rollback condition
- human gate requirement

### 9.4 指标

D 阶段指标必须与 Baseline 方差字段对齐，避免后续手工映射。

至少比较：

| D 指标 | 对齐的 Baseline 字段 |
| --- | --- |
| `successRateDelta` | `successRate` |
| `latencyMeanDeltaMs` | `latencyMeanMs` / `latencyStdDevMs` |
| `tokenMeanDelta` | `tokenMean` / `tokenStdDev` |
| `costMeanDelta` | `costMean` / `costStdDev` |
| `toolCallMeanDelta` | `toolCallMean` / `toolCallStdDev` |
| `recoveryRateDelta` | recovery 次数或 recovery rate |
| `validationRejectionDelta` | validation rejection 次数 |
| `failureSeverityDelta` | failure severity |
| `traceCompletenessDelta` | trace completeness |
| `qualityMeanDelta` | `qualityMean` / `qualityStdDev`，仅作为辅助解释 |

每个 delta 必须与 Baseline 方差比较。

人工质量分默认不进入自动 promotion 的主 effect size 判据。只有同时满足以下条件，才可以把质量分作为辅助 promotion evidence：

- 评分前有固定 rubric。
- 评分人与被评分策略隔离，或至少隐藏策略来源。
- 同一输出至少有两名评分人，或同一评分人分时重复评分。
- 报告评分一致性；一致性不足时质量分只作为人工备注。

质量分可以用于人工审核、失败解释和候选策略排序，但不得单独触发自动 promotion。

### 9.5 样本量和 effect size

D 阶段必须区分两种证据：

| 证据等级 | 用途 | 最低要求 |
| --- | --- | --- |
| `directional-signal` | 判断候选策略是否值得继续投入。 | 每个候选策略至少覆盖 3 个任务，每个任务 baseline 与候选各运行 5 次。只能得出方向性结论，不能自动晋升。 |
| `promotion-evidence` | 支撑自动 promotion。 | 每个候选策略至少覆盖 5 个任务，每个任务 baseline 与候选各运行 10 次；若成本不允许，必须降级为人工审核。 |

自动 promotion 的最低 effect size：

- 候选策略在至少 80% 的任务上，主指标改进必须大于 `2 * baselineStdDev`，或满足预先登记的等价保守规则。
- 候选策略在任何核心任务上的强退化指标不得超过 `1 * baselineStdDev`；若超过，必须降级为人工审核或判定失败。
- 候选策略不能只在单个任务显著改善，同时在其他任务退化。
- 若主指标是成功率，必须同时检查 cost、latency 和 failure severity，防止用过高成本换取表面成功率。
- 若样本量不足以支撑 `promotion-evidence`，只能输出 `directional-signal`。

### 9.6 显著性规则

自动 promotion 的最低要求：

- 候选策略平均收益大于 Baseline 自然方差。
- 候选策略满足 `promotion-evidence` 样本量。
- 候选策略满足预先登记的 effect size 规则。
- 改进方向与 expected improvement 一致。
- 没有引入新的高风险 failure。
- 没有绕过 B 阶段安全门。
- replay / evaluation evidence 完整。
- 高风险策略有人类审批 evidence。

如果无法做严格统计检验，至少必须给出保守判断：

```text
候选 delta 是否明显大于 Baseline 同任务同策略波动。
```

若不能明显大于，则不能自动晋升。

### 9.7 D 通过标准

D 通过必须满足：

- 至少一个候选策略在多个任务上表现优于 Baseline。
- 改进超过 Baseline 方差。
- 至少 80% 的任务满足预先登记的 effect size 规则。
- 不存在超过 `1 * baselineStdDev` 的核心任务强退化，除非该候选策略降级为人工审核且不自动 promotion。
- 自动 promotion 只基于 `promotion-evidence`，不基于 `directional-signal`。
- 自动 promotion 不以人工质量分作为唯一主指标。
- evaluation evidence 可复盘。
- promotion gate 能拒绝缺证据、缺 human gate 或高风险策略。
- rollback 能把 promoted strategy 回退到先前稳定策略。

### 9.8 D 判死线

任一命中即 D 不通过：

- 拿不出稳定评分任务集。
- 拿不出 Baseline 方差。
- 拿不出最低样本量。
- 没有预先登记 effect size 规则。
- 未达到至少 80% 任务通过 effect size 的要求，却仍执行自动 promotion。
- 存在超过 `1 * baselineStdDev` 的核心任务强退化，却仍执行自动 promotion。
- 候选 delta 小于或等于 LLM 自然方差。
- 仅凭人工质量分改善执行自动 promotion。
- promotion 依赖人工主观挑选成功个例。
- 仅有 `directional-signal` 却执行自动 promotion。
- replay 无法产生可比较结果。
- rollback 无法恢复到稳定策略。
- 高风险策略能在缺少 human gate 时 promoted。

### 9.9 D 失败后的决策

D 失败时：

- 自动 promotion 关闭。
- strategy lifecycle 只保留人工审核和手动固化。
- Adaptive layer 可继续提供建议，但不得自动改变稳定内核运行策略。

## 10. 最终验收矩阵

| 命题 | 通过后允许 | 失败后必须 |
| --- | --- | --- |
| B | 继续验证真实 turn 映射。 | 修复 validator / RuntimeStep / bridge fail-closed。 |
| A1 | 继续保留 StageGraph 作为候选 IR。 | 降级或砍掉 graph 层。 |
| A2 | 继续验证完整真实 loop。 | 产出 runtime loop design delta；若无法定义 delta，则暂停。 |
| Baseline | 继续 C，并用方差约束 D。 | 修复固定骨架或缩小验收范围。 |
| C | 允许把 LLM graph / patch 作为候选策略来源。 | 关闭 AI 产完整 graph，只保留建议或固定 graph 内策略。 |
| D | 允许受控自动 promotion。 | 自动 promotion 关闭，仅人工固化。 |

## 11. 最终报告格式

spike 分支结束时必须输出一份总报告：

```text
docs/audit/evidence/adaptive-kernel-feasibility/final-report.md
```

报告必须包含：

- 验证日期和 commit。
- 分支名。
- B/A/Baseline/C/D 每阶段状态。
- 每阶段命令和证据路径。
- 每个判死线是否命中。
- A1 手工 StageGraph fixture 的 `ValidateGraphAsync` 验证结果。
- Baseline 执行路径、证明范围和不能证明的范围。
- B 阶段正例 / 负例对照结果。
- C0 prompt/schema 校准结果、迭代次数和每轮变更摘要。
- C 阶段 graph / patch 合法率、revise 成本、Baseline 成本增量和成本对照路径。
- D 阶段样本量、effect size 规则、证据等级、80% 任务通过率、强退化检查和质量分使用口径。
- trace canonicalization 规则版本。
- 最终架构建议：
  - 保留 StageGraph 为正式自适应 IR。
  - StageGraph 降级为粗粒度 run shell。
  - 砍掉大部分 graph 层。
  - 关闭 AI 产 graph。
  - 关闭自动 promotion。
  - 需要新增 runtime loop 专项设计。
- 对 `docs/tianshu-architecture-spec.md` 的回写建议。
- 对 `docs/tianshu-implementation-tracker.md` 的实施项建议。
- 对当前代码的最小后续改造建议。

## 12. 执行检查清单

### 12.1 开始前

- [ ] 当前主线状态已确认。
- [ ] 用户提供的审计文件未被误提交。
- [ ] spike 分支已创建。
- [ ] tracker 已登记可行性验证工作。
- [ ] 明确本轮不打包、不安装三件套。
- [ ] 明确本轮只关注 CLI 消费层，不扩展 VSIX / GUI 宿主范围。

### 12.2 B 阶段

- [ ] Kernel validator 对抗测试完成。
- [ ] Kernel validator 正例对照完成。
- [ ] Execution Runtime `ValidateStep` 对抗测试完成。
- [ ] Execution Runtime `ValidateStep` 正例对照完成。
- [ ] Provider bridge envelope 复用测试完成。
- [ ] Tool bridge envelope 复用测试完成。
- [ ] Memory bridge envelope 复用测试完成。
- [ ] Artifact bridge envelope 复用测试完成。
- [ ] Diagnostics bridge envelope 复用测试完成。
- [ ] Workspace bridge envelope 复用测试完成。
- [ ] Model route / context policy bridge 边界核查完成。
- [ ] B 阶段证据报告完成。

### 12.3 A 阶段

- [ ] 真实 turn 轨迹选定。
- [ ] `CoreIntent` 实例写出。
- [ ] `StageGraph` 实例图写出。
- [ ] 手工 `StageGraph` fixture 已构造为真实 contract 对象。
- [ ] 手工 `StageGraph` fixture 已通过真实 `IKernelValidator.ValidateGraphAsync`。
- [ ] `KernelOperation` 映射写出。
- [ ] `RuntimeStep` 映射写出。
- [ ] steer 映射写出。
- [ ] interrupt 映射写出。
- [ ] resume 映射写出。
- [ ] subagent 映射写出。
- [ ] 延迟分析完成。
- [ ] A2 delta 已写出，或确认不需要 delta。
- [ ] A 阶段证据报告完成。

### 12.4 Baseline 阶段

- [ ] `baselineExecutionPath` 已声明。
- [ ] Baseline 能证明和不能证明的范围已写明。
- [ ] 固定非自适应路径定义完成。
- [ ] 验收子集定义完成。
- [ ] 最小 provider + tool loop 跑通。
- [ ] steer / interrupt / resume / subagent 覆盖或 delta 标注完成。
- [ ] trace 复盘完成。
- [ ] trace canonicalization 规则已定义。
- [ ] 每个 baseline task 多轮运行完成。
- [ ] 方差报告完成。
- [ ] Baseline 阶段证据报告完成。

### 12.5 C 阶段

- [ ] C0 prompt/schema 校准完成。
- [ ] C0 prompt/schema 迭代次数未超过上限，或已降级为 prompt/schema 设计改造。
- [ ] stub kernel 准备完成。
- [ ] graph / patch schema 准备完成。
- [ ] validator 接入完成。
- [ ] 任务集准备完成。
- [ ] 首次合法率统计完成。
- [ ] revise 收敛率统计完成。
- [ ] revise token / latency / model-call 成本统计完成。
- [ ] C 相对 Baseline 的成本增量评估完成，并确认成本对照使用同一 `baselineExecutionPath`。
- [ ] 跨模型差异统计完成。
- [ ] C 阶段证据报告完成。

### 12.6 D 阶段

- [ ] 确认 B/A/Baseline/C 前置门控全部满足。
- [ ] 候选策略准备完成。
- [ ] Baseline 对照数据准备完成。
- [ ] evaluation 任务集准备完成。
- [ ] 最低样本量规则已满足，或已降级为 `directional-signal`。
- [ ] effect size 规则已预先登记。
- [ ] 至少 80% 的任务满足 effect size 规则，或未执行自动 promotion。
- [ ] 核心任务强退化检查完成。
- [ ] 人工质量分只作为辅助证据，或已满足 rubric / 多评分一致性要求。
- [ ] delta 与方差比较完成。
- [ ] promotion gate 测试完成。
- [ ] rollback 测试完成。
- [ ] D 阶段证据报告完成。

### 12.7 结束时

- [ ] final report 完成。
- [ ] 正式架构保留 / 降级 / 删除建议明确。
- [ ] 需要回写的设计文档列表明确。
- [ ] 需要进入 tracker 的实施项明确。
- [ ] spike 代码是否保留、迁移或删除已有结论。

## 13. 最终判定

最终结论只能是以下之一：

| 结论 | 含义 |
| --- | --- |
| `feasible-controlled` | 自适应内核可行且可控；StageGraph 可保留为正式自适应 IR；自动 promotion 可在受控范围内推进。 |
| `feasible-with-limits` | 自适应内核部分可行；AI 可参与建议或局部策略，但 graph 生成或自动 promotion 需要降级。 |
| `fixed-graph-only` | 固定 graph 可行，但 AI 产 graph 不可行；正式架构应转向固定 graph + stage 内工具策略。 |
| `reactive-runtime-required` | StageGraph 不适合真实 turn loop；需要 RuntimeStep + reactive turn interpreter 或 runtime loop 专项设计。 |
| `not-controlled` | 安全门关不住；自适应层不得上线，先修 B。 |
| `not-feasible` | 关键命题失败且无法通过降级保留核心价值；自适应内核设计应停止。 |

不得输出模糊结论，例如“基本可行”“后续再看”“大致没问题”。每个结论都必须绑定证据路径和后续架构动作。
