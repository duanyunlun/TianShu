# TianShu 自适应内核第二次可行性验收设计

## 1. 文档定位

本文是第二次自适应内核可行性验收的设计文档，基于第一次验收结论 `feasible-with-limits` 制定。

第一次验收已经证明：

- Stable Kernel Core 的基础治理边界可工作。
- `StageGraph` contract、`KernelValidator`、`StageGraphInterpreter` 可承载静态 graph 校验和解释。
- C0 prompt/schema 校准可以产出合法 StageGraph 形状。
- `KernelTool` / `KernelProposal` 边界具备继续验证价值。

第一次验收没有证明：

- `kernel-interpreter-loop` 能执行真实 live turn。
- steer / interrupt / resume / subagent 能进入完整 live runtime loop。
- LLM 能稳定产出 graph / patch / recovery / context policy / checkpoint proposal。
- C 阶段能按 token、cost、latency、model-call 与 Baseline 做有效比较。
- D 阶段 automatic strategy promotion 安全且有收益。

本文不是正式架构规范，不替代 `docs/tianshu-architecture-spec.md`。它是第二次验收的实施基线：后续若要补代码、补 runner、补测试或跑 live 验收，应先按本文顺序推进，并把结果归档到新的二次验收证据目录。

第二次验收的直接目标是补齐**独立 C/D harness**，不改 CLI/AppHost 产品主交互路径。`kernel-interpreter-loop` 活体执行、steer / interrupt / resume / subagent live loop 是产品化前必须补齐或明确降级的挂起缺口，但不应阻断独立 C/D harness 的建设。它们必须被记录和复审，不能被 C/D harness 的通过结论吞掉。

## 2. 总结论和核心修正

第二次验收不能只写测试过程，必须补源码级 harness；但它也不能把范围扩张成产品主路径改造。核心修正如下：

1. **独立 C/D harness 是本轮主线**：先证明 LLM graph/patch -> Kernel contract -> validator -> dry-run -> metrics -> D gate 这一条链能不能成立。
2. **token/cost 是 harness 自身硬门禁**：C/D harness 必须自带 token/cost/latency/model-call 输出，否则 C 成本门槛和 D effect-size 判断仍不成立。
3. **Kernel interpreter 活体执行是产品路径挂起缺口**：`StageGraphInterpreter` 当前只证明静态解释，不证明 `ExecutionPlan` 能驱动真实 RuntimeStep live loop；它不阻断独立 C/D harness，但阻断产品路径接入。
4. **A2 runtime loop 是产品路径挂起缺口**：steer / interrupt / resume / subagent 当前没有完整 live 状态机验收，baseline-task-06 的 subagent 缺口是真实失败；它不阻断独立 C/D harness，但阻断“完整 agent loop 可行”结论。

因此第二次验收顺序固定为：

```text
S0 基线锁定
-> S1 独立 C/D harness 指标与证据底座
-> S2 C runner / schema / mapper / patch apply
-> S3 C graph / patch live 验收
-> S4 D strategy evolution
-> S5 产品路径挂起缺口复审
-> S6 最终结论
```

任一阶段未通过时，所有依赖阶段暂停，不允许用下游 smoke 替代上游门禁。

依赖关系：

- S1 是 S2/S3/S4 的硬前置；没有 token/cost，不能进入 C full matrix 和 D。
- S2 是 S3 的硬前置；没有 mapper/patch apply/validator dry-run，不能跑 C live。
- S3 是 S4 的硬前置；C 未通过，不得启动 D。
- S5 不阻断 S1-S4，但会约束最终结论：若活体 interpreter 或 A2 仍未通过，最终不得输出 `feasible-controlled`，也不得建议接入产品主路径。

## 3. 涉及项目和代码边界

| 项目或路径 | 第二次验收职责 |
| --- | --- |
| `tools/Run-TianShuAdaptiveKernelFeasibility.ps1` | 继续作为验收 runner 入口；需要增加二次验收阶段参数、指标采集、C/D 输出。 |
| `src/Contracts/TianShu.Contracts.Kernel` | `StageGraph`、`StageGraphPatchProposal`、`RecoveryProposal`、`StrategyPromotionProposal` 等正式 contract 的映射目标。 |
| `src/Core/TianShu.Kernel` | `KernelValidator`、`StageGraphInterpreter`、state machine、trace emission 的真实边界。 |
| `src/Core/TianShu.Kernel.Adaptive` | `KernelTool`、`AdaptiveOrchestrator`、proposal 生成边界。 |
| `src/Core/TianShu.Kernel.Strategies` | D 阶段 strategy evaluation、promotion、rollback 的目标边界。 |
| `src/Contracts/TianShu.Contracts.Execution` | `ExecutionPlan`、`RuntimeStep`、`RuntimeStepResult` 的执行 contract。 |
| `src/Execution/TianShu.Execution.Runtime` | live RuntimeStep 执行、`ValidateStep`、metrics envelope、bridge governance。 |
| `src/Hosting/TianShu.AppHost.Tools.Runtime` | 当前真实 turn loop、provider/tool continuation、steer/interrupt/subagent 事实来源。 |
| `src/Presentations/TianShu.Cli` | 第二次验收唯一消费层入口；其他宿主暂不纳入。 |
| `tests/TianShu.Kernel.Tests` | Kernel validator/interpreter/state-machine 守护测试。 |
| `tests/TianShu.Kernel.Adaptive.Tests` | Adaptive proposal 边界守护测试。 |
| `tests/TianShu.Execution.Runtime.Tests` | RuntimeStep、metrics、bridge、live execution 守护测试。 |
| `tests/TianShu.Execution.Integration.Tests` | 必要时承载 CLI/AppHost/RuntimeComposition 端到端验收。 |

第二次验收允许修改源码，但必须优先改验收 runner、测试 harness、metrics/reporting 和必要的 Kernel/Execution 边界，不应直接把 AI graph/patch 接入正式产品主路径。

## 4. 缺少的步骤清单

### 4.1 基础测量缺口

| 编号 | 缺口 | 为什么必须补 | 最小完成标准 |
| --- | --- | --- | --- |
| M1 | runner token/cost metrics | C 成本门槛、D effect-size 和 promotion gate 依赖 token/cost；第一次验收只有 latency/model-call。 | Baseline、C0、C、D 输出统一 `tokenUsage`、`estimatedCost`、`priceModelVersion`、`latencyMs`、`modelCallCount`；`estimatedCost` 必须绑定模型 id、输入/输出 token 单价和价格表版本，缺价格表时只报告 raw token，不报告 cost，不启动 D。 |
| M2 | 统一 run metrics schema | 第一轮各阶段结果字段不完全一致，二次审查成本高。 | 定义 `run-metrics.schema.json` 或等价 contract，所有阶段 `runs.json` / `summary.json` 使用同一字段族。 |
| M3 | trace/cost 关联 | D 需要知道成本来自哪个 graph、stage、runtime step 或 revise round。 | 每条指标至少关联 `runId`、`taskId`、`graphId`、`stageId?`、`stepId?`、`attemptIndex`、`reviseRound?`。 |
| M4 | Baseline 成本方差 | 没有 Baseline cost/stddev，D 的 30% 成本门槛和 effect-size 判断不可用。 | Baseline 每个任务至少 5 次采样并输出 success、latency、token、cost、model-call 的 mean/stddev。 |

### 4.2 产品路径挂起缺口：活体执行

| 编号 | 缺口 | 为什么必须补 | 最小完成标准 |
| --- | --- | --- | --- |
| L1 | `kernel-interpreter-loop` 活体执行 | 当前只证明 `StageGraph -> ExecutionPlan`，没有证明 `ExecutionPlan -> RuntimeStep live execution`。 | 固定 StageGraph 能通过 interpreter 生成 plan，并由 Execution Runtime 执行至少一条 read-only `ModelInvocationStep` 或等价 live step，产生 `RuntimeStepResult` 和 trace。 |
| L2 | RuntimeStep 到 Module/Provider bridge 的真实调用边界 | AI graph 如果最终不能驱动真实 provider/tool/memory 等 bridge，StageGraph 仍只是静态 IR。 | 每类纳入验收的 RuntimeStep 必须能证明经过 `ValidateStep`，且成功/失败均有可审计 trace。 |
| L3 | checkpoint / recovery 活体路径 | C graph/patch 会包含 checkpoint/recovery，必须证明不是纯字段。 | 至少一个 live run 能写入 checkpoint evidence，并在模拟失败后生成 recovery decision 或明确 fail-closed。 |
| L4 | trace replay / audit | 自适应演化必须可复盘，否则无法受控。 | 从 run evidence 能重建 graph、stage、runtime step、model call、tool call、checkpoint、recovery 和 metrics 的关系。 |

说明：L1-L4 不阻断独立 C/D harness 建设，但阻断 AI graph/patch 接入正式产品主路径。

### 4.3 产品路径挂起缺口：A2 runtime loop

| 编号 | 缺口 | 为什么必须补 | 最小完成标准 |
| --- | --- | --- | --- |
| A2-1 | steer live loop | 用户中途 steer 是真实 agent loop 的核心能力。 | steer 能进入正式 operation/intent 或 runtime step，影响后续执行，并留下 trace。 |
| A2-2 | interrupt live loop | interrupt 不能只作为 CLI 表象，必须能停止或挂起执行状态。 | interrupt 能让 run 进入可审计 interrupted/suspended 状态，不继续产生越权副作用。 |
| A2-3 | resume live loop | resume 必须能恢复上下文、checkpoint 和未完成执行。 | resume 能从 interrupted/suspended 状态继续或明确 fail-closed，并保留前后 trace 连续性。 |
| A2-4 | subagent live loop | baseline-task-06 已证明 subagent 缺口真实存在。 | 至少一个 subagent 抽象任务能进入受控边界；若仍不支持，必须给出正式降级策略，不得把 C/D 成功解释为完整 agent loop 成功。 |

说明：A2-1 到 A2-4 不阻断独立 C/D harness 建设，但阻断 `feasible-controlled` 和产品路径接入。subagent 当前是已观测失败，不是未知风险。

### 4.4 C graph/patch 缺口

| 编号 | 缺口 | 为什么必须补 | 最小完成标准 |
| --- | --- | --- | --- |
| C1 | `-RunC` runner | 当前 runner 只有 `RunBaseline` / `RunC0`，没有 C 阶段任务入口。 | `RunC` 是明确参数，不依赖 PowerShell 缩写；dry-run 与 live run 均可输出 C task set manifest。 |
| C2 | C task set | C0 只验证简单 graph 形状，不能代表 graph/patch 生成能力。 | 覆盖 simple graph、single tool、multi tool、model route、human gate、recovery、checkpoint、context policy、subagent abstract、steer/interrupt/resume abstract。 |
| C3 | graph / patch schema | C0 schema 不能覆盖 patch、recovery、policy、checkpoint。 | 每类候选都有 schema，且 schema 测试覆盖合法/非法 fixture。 |
| C4 | LLM JSON -> contract mapper | 只做 PowerShell JSON shape 校验不足以证明真实 Kernel 兼容。 | 候选输出必须反序列化或构造为强类型 `StageGraph`、`StageGraphPatchProposal`、`RecoveryProposal`、`CheckpointProposalOperation` 或等价正式 contract；最终验收对象不能停留在 `JsonObject`、`Dictionary`、`JObject` 或字符串拼接结果。 |
| C5 | patch apply / dry-run | patch 候选必须证明能应用到 base graph。 | patch apply 后得到 candidate graph，并通过 `KernelValidator.ValidateGraphAsync` 和 `StageGraphInterpreter.InterpretAsync` dry-run。 |
| C6 | structured rejection revise | C 阶段必须证明 rejection reason 能收敛。 | 最多 3 轮 revise；只把结构化 rejection reason 返回给模型；记录首轮合法率、最终合法率、平均 revise 轮数、震荡率。 |
| C7 | 成本与 Baseline 对照 | 合法但成本过高不能进入 D。 | C 每个任务输出 token/cost/latency/model-call 增量，并与同 `baselineExecutionPath` 的 Baseline 方差对照。 |

### 4.5 D strategy evolution 缺口

| 编号 | 缺口 | 为什么必须补 | 最小完成标准 |
| --- | --- | --- | --- |
| D1 | D runner | 第一次验收因 C 未通过未启动 D。 | `RunD` 或等价 harness 只能在 C 通过后运行，并记录前置门禁引用。 |
| D2 | effect-size 预注册 | D 不能靠主观“看起来更好”，也不能把二项成功率当作正态连续变量处理。 | 运行前按指标类型写死 promotion 规则：success 等二项指标使用比例差、置信区间或保守贝叶斯规则；latency/token/cost 等连续指标才可使用 stddev / effect-size；强退化阈值按同一指标类型定义。 |
| D3 | strategy candidate materialization | C 产出的 graph/patch 需要变成可评估 strategy。 | 候选 strategy 关联 graph/patch、risk profile、rollback plan、evaluation plan。 |
| D4 | promotion gate | 自动 promotion 是高风险能力。 | 缺 evidence、缺 human gate、高风险或收益不足时必须拒绝 promotion。 |
| D5 | rollback gate | 自适应系统必须可回退。 | promoted/trial strategy 能基于失败 evidence 回退到上一个稳定策略。 |
| D6 | human gate | 高风险策略必须人审。 | 任一高风险 policy/side-effect/strategy promotion 无 human gate 时 fail-closed。 |

## 5. 第二次验收阶段设计

### S0：基线锁定与证据清理

目标：

- 建立第二次验收证据根目录。
- 锁定分支、提交、CLI 路径、runner 版本和模型参数。
- 明确第一次证据只作为输入，不作为第二次通过证据。

执行步骤：

1. 创建或确认第二次验收分支，建议命名：`codex/adaptive-kernel-second-acceptance`。
2. 记录 `git status --short --branch`、`git rev-parse HEAD`、`git log -1 --oneline`。
3. 构建当前分支 CLI，记录绝对路径、hash、last write time。
4. 建立证据根：`docs/audit/evidence/adaptive-kernel-second-acceptance/`。
5. 建立 live 根：`Test/TianShuAdaptiveKernelSecondAcceptance.__live/`。

通过条件：

- 后续所有 live runner 命令显式传入当前分支 CLI。
- 不使用用户级 `C:\Users\Example\.tianshu\bin\tianshu.exe`。
- 第二次证据目录不混入第一次 live 输出。

判死线：

- 任一命令使用用户级已安装 CLI，当前阶段及其下游证据废弃。

### S1：独立 C/D harness 指标与证据底座

目标：

- 为独立 C/D harness 建立统一 token/cost/latency/model-call 指标。
- 让 C/D 验收不依赖产品主路径也能判断成本和 effect size。
- 如果 provider 当前无法返回 token/cost，必须明确记录缺失来源；该状态下不得启动 D automatic promotion。
- `estimatedCost` 只能由明确价格模型计算；价格模型必须记录 `priceModelVersion`、`modelId`、输入 token 单价、输出 token 单价、币种和生效日期。

执行步骤：

1. 定义二次验收 `RunMetrics` 字段族。
2. runner 对每次 CLI exec 输出解析 token usage、model calls、latency。
3. runner 读取二次验收 price model，把 token usage 转换为 `estimatedCost`；缺价格模型时只输出 raw token，并将 `estimatedCost` 标为 missing。
4. C/D harness 的每个 attempt、revise round 和 strategy evaluation 都写入同一 metrics 字段族。
5. Baseline 和 C0 重跑最小 smoke，确认 metrics 字段存在。
6. 补测试覆盖 metrics schema：字段存在、缺失原因、price model 版本、聚合 mean/stddev。

输出证据：

- `metrics-schema.md` 或 `run-metrics.schema.json`
- `price-model.md` 或 `price-model.json`
- `baseline-smoke-summary.json`
- `c0-smoke-summary.json`
- metrics 测试结果

通过条件：

- Baseline/C0 smoke 均输出统一 metrics 字段。
- C/D harness dry-run 输出 metrics envelope。
- token 缺失时有机器可读 `missingReason`，且最终报告能据此阻断 C full matrix 与 D。
- cost 缺失或 price model 缺失时有机器可读 `missingReason`，允许继续 C 合法性验证，但不得进入 D automatic promotion。

判死线：

- runner 仍只输出 latency，无法判断 token/cost，也没有缺失原因字段。
- `estimatedCost` 没有绑定 `priceModelVersion`、模型 id 和输入/输出 token 单价，却被用于 C/D 成本判断。

### S2：C runner / schema / mapper / patch apply 准备验收

目标：

- 补齐 C 阶段执行所需源码级 harness，但本阶段不以 live LLM 成功率为目标。
- 建立 LLM JSON -> Kernel contract -> validator -> interpreter dry-run 的闭环。

执行步骤：

1. runner 增加明确 `RunC` 参数。
2. 定义 C task manifest。
3. 定义 graph、patch、recovery、context policy、checkpoint proposal schema。
4. 实现或接入 JSON -> contract mapper；mapper 的输出类型必须是正式 Kernel contract 类型或显式的 typed candidate 类型。
5. 实现或接入 patch apply。
6. 增加 schema、mapper、patch apply、validator/interpreter dry-run 测试。
7. 增加测试防止 `-RunC` 被 PowerShell 缩写解析为 `-RunC0`。

通过条件：

- 所有 C fixture 能 dry-run。
- 非法 fixture fail-closed。
- patch apply 后 candidate graph 可进入真实 validator/interpreter。
- mapper 测试断言输出对象的 CLR 类型，例如 `StageGraph`、`StageGraphPatchProposal`、`RecoveryProposal`、`CheckpointProposalOperation` 或正式 typed candidate；不能只断言 JSON 字段存在。
- validator/interpreter 调用必须使用编译期类型签名，例如 `KernelValidator.ValidateGraphAsync(StageGraph, ...)`，不能通过 `object`、反射或动态字典绕过类型系统。

判死线：

- C runner 仍无法区分 `RunC` 和 `RunC0`。
- mapper 只做字符串拼接，不能生成真实 contract object。
- mapper 最终输出仍是 `JsonObject`、`Dictionary<string, object?>`、`JObject` 或未类型化 payload，却被计为 contract 验收通过。

### S3：C graph / patch live 验收

目标：

- 验证 LLM 是否能在 schema、structured rejection 和真实 Kernel 边界下稳定产 graph/patch。

执行步骤：

1. 先跑 C smoke：每类任务 1 次。
2. 在 full matrix 前预注册 C acceptance profile，至少声明 prompt profile、schema profile、task set、模型、temperature、seed 是否固定、RepeatCount 和阈值。
3. smoke 通过后每类任务至少 5 次。
4. 每次最多 3 轮 revise。
5. 每轮输出 candidate、validation result、rejection reason、metrics。
6. 汇总首轮合法率、最终合法率、平均 revise、震荡率、越权率、成本增量。

通过条件：

- 验收阈值只应用于预注册的 acceptance profile；`schema-minimal`、`schema-full` 等裸 schema 组默认是诊断组，不能直接套用 acceptance 阈值，也不能直接否定 C。
- 默认 acceptance profile 的最终合法率达到 85%；若使用其他阈值，必须在 full run 前预注册，并说明与 C0 结果、任务复杂度和模型能力的关系。
- 默认 acceptance profile 的平均 revise 小于等于 1.5；若高于 1.5，必须证明任务复杂度合理、交互成本可接受，并明确是否降级为离线候选。
- 高风险越权必须全部被 validator 拒绝。
- 不能全部退化为单个 `core_loop` stage。
- 成本增量不得超过同 Baseline 路径可接受门槛；默认门槛为 30%。

判死线：

- 首轮低合法率且 revise 不收敛。
- 高频越权且 rejection reason 无法纠正。
- 合法 graph 无法映射为真实 Kernel contract 或无法通过 validator/interpreter dry-run。
- 成本不可接受。
- 未预注册 acceptance profile，却在结果出来后挑选 prompt/schema 组合套用阈值。

### S4：D strategy evolution 受控收益验收

目标：

- 仅在 S3 通过后，验证 strategy evolution 是否有收益且可控。
- D 仍运行在独立 harness 内，不接入产品主路径。

执行步骤：

1. 预注册 effect-size、promotion gate、rollback gate、human gate。
2. 从 C 产出的候选 graph/patch 或人工候选中生成 strategy candidate。
3. 运行 Baseline 对照采样。
4. 按指标类型计算 success、quality、latency、token、cost、model-call 的 effect size。
5. 尝试 promotion，验证缺 evidence / 缺 human gate / 高风险 / 收益不足时拒绝。
6. 验证 rollback。

通过条件：

- 至少一个候选策略在多个任务上超过自然方差，且没有核心任务强退化。
- success / pass-rate 等二项指标必须使用比例差、置信区间、精确检验或预注册的保守贝叶斯规则；不得用 `2 * baselineStdDev` 作为主判据。
- latency / token / cost / model-call 等连续指标可以使用均值差、stddev、置信区间或非参数规则；使用 stddev 时必须说明样本量限制。
- quality 若来自人工或模型评分，必须先定义量表、方差口径和最小有意义差异，不能和 success 二项指标混算。
- promotion gate 能拒绝缺证据、缺 human gate、高风险或收益不足策略。
- rollback 能恢复稳定策略。

判死线：

- C 未通过却启动 D。
- 样本量不足却自动 promotion。
- 对二项 success 指标使用正态连续指标的 `2 * stddev` 规则作为 promotion 主判据。
- 缺 human gate 的高风险 strategy 被 promotion。
- rollback 无法恢复。

### S5：产品路径挂起缺口复审

目标：

- 复审 `kernel-interpreter-loop` 活体执行和 A2 runtime loop，不把它们误认为已由 C/D harness 解决。
- 明确它们对最终结论和产品路径接入的限制。

复审项目：

| 项目 | 当前第一次验收状态 | 二次验收要求 |
| --- | --- | --- |
| `kernel-interpreter-loop` 活体执行 | 只证明静态 `StageGraph -> ExecutionPlan`。 | 若仍未实现 live execution，最终不得声称产品主路径可接入 AI graph/patch。 |
| steer live loop | A2 部分通过。 | 给出 `live-pass` / `trace-only` / `blocked-missing-runtime` / `blocked-unsafe` 判定。 |
| interrupt live loop | A2 部分通过。 | 给出状态机、trace 和副作用停止证据；否则阻断产品路径。 |
| resume live loop | A2 部分通过。 | 给出恢复原 run/trace/checkpoint 的证据；否则阻断产品路径。 |
| subagent live loop | baseline-task-06 为 5/5 `a2_delta_subagent_unavailable`。 | 若仍失败，最终必须明确“完整 agent loop 未通过”。 |

判定枚举：

- `live-pass`：进入正式 runtime loop，行为生效，可审计。
- `trace-only`：能表达和记录，但不影响 live execution。
- `blocked-missing-runtime`：缺 runtime path。
- `blocked-unsafe`：有越权或不可控风险。

通过条件：

- 每个挂起缺口都有上述枚举判定、证据路径、后续实施项。
- 若未通过，最终结论降级，不得输出 `feasible-controlled`。

判死线：

- C/D harness 通过后，最终报告遗漏这些挂起缺口。
- subagent 仍失败却宣称完整 agent loop 可行。

### S6：最终结论

最终结论必须使用以下枚举之一：

| 结论 | 含义 |
| --- | --- |
| `feasible-controlled` | S1-S5 全部通过，且产品路径挂起缺口已解决；可进入受控产品化设计。 |
| `feasible-with-limits` | 基础和部分自适应能力可行，但限制必须分类说明：`harness-only` 表示 C/D harness 成立但产品路径挂起缺口未清；`adaptive-limited` 表示 C 或 D 仍有限制；`runtime-limited` 表示 interpreter/A2 live loop 未通过。 |
| `fixed-graph-only` | 固定 graph / interpreter loop 可行，但 AI graph/patch 不可行或成本不可接受。 |
| `reactive-runtime-required` | StageGraph 静态编排不能承载真实 live loop，需要 reactive runtime loop 优先。 |
| `not-controlled` | 安全门或 human gate 关不住。 |
| `not-feasible` | 关键命题失败，且无法通过降级保留核心价值。 |

最终报告必须引用第二次验收证据路径，不得把第一次验收证据直接当作第二次通过证据。

## 6. 第二次验收工作清单

以下清单应同步拆入 `docs/tianshu-implementation-tracker.md` 的后续实施项：

1. 建立第二次验收分支与证据目录。
2. 统一 `RunMetrics` 字段族。
3. 定义 price model，包含 `priceModelVersion`、模型 id、输入/输出 token 单价、币种和生效日期。
4. 让 Baseline/C0/C/D harness 输出 token/cost/latency/model-call；缺 price model 时只输出 raw token，不输出 cost。
5. 补 metrics schema、price model schema 与聚合测试。
6. runner 增加明确 `RunC` 参数。
7. 增加 C task manifest。
8. 增加 C graph / patch / recovery / context policy / checkpoint schema。
9. 增加 LLM JSON -> Kernel contract 强类型 mapper。
10. 增加 patch apply。
11. 增加 C fixture schema/mapper/patch/validator/interpreter 测试，断言输出对象不是动态 JSON 或字典。
12. 增加 `-RunC` 不被解析为 `RunC0` 的守护测试。
13. 预注册 C acceptance profile，明确 prompt profile、schema profile、阈值和是否允许裸 schema 诊断组参与结论。
14. 跑 C smoke。
15. 跑 C full matrix。
16. 汇总 C 合法率、revise、震荡、越权、成本增量。
17. C 通过后预注册 D effect-size 和 promotion gate，并按二项/连续/评分指标分别定义统计规则。
18. 跑 D strategy candidate 评估。
19. 验证 promotion gate、rollback gate、human gate。
20. 复审 `kernel-interpreter-loop` live harness 是否仍缺失。
21. 复审固定 StageGraph -> ExecutionPlan -> RuntimeStep live execution 是否仍缺失。
22. 复审 RuntimeStep trace replay 与 checkpoint/recovery evidence 是否仍缺失。
23. 复审 A2 task set：steer、interrupt、resume、subagent。
24. 明确 A2 每项判定：`live-pass`、`trace-only`、`blocked-missing-runtime`、`blocked-unsafe`。
25. 若产品路径挂起缺口仍存在，最终结论不得高于 `feasible-with-limits`，并必须标注 `runtime-limited` 或 `harness-only`。
26. 输出第二次最终报告和结论。

## 7. 不允许的捷径

- 不得把 C0 合法 graph 当作 C graph/patch 通过。
- 不得把 `StageGraphInterpreter.InterpretAsync` 静态生成 `ExecutionPlan` 当作 live execution 通过。
- 不得因为 Baseline 固定路径可跑，就宣称 `kernel-interpreter-loop` 可跑。
- 不得在 token/cost 不可用时启动 D automatic promotion。
- 不得在 subagent 仍失败时宣称完整 agent loop 可行。
- 不得用 `kernel-interpreter-loop` 或 A2 产品路径缺口作为不建设独立 C/D harness 的理由。
- 不得把独立 C/D harness 通过解释为产品主路径已可接入。
- 不得用人工修补 LLM 输出后的 graph/patch 计入合法率。
- 不得放宽 B 安全门来提升 C/D 通过率。

## 8. 与第一次验收的关系

第一次验收结论 `feasible-with-limits` 仍保留，作为第二次验收输入。

第二次验收需要补齐的关键差距是：

- 从 C0 prompt/schema 校准推进到完整 C graph/patch 独立 harness。
- 从“有 proposal 边界”推进到“proposal 可评估、可拒绝、可回滚”。
- 从 latency/model-call 粗指标推进到 token/cost/effect-size 可判定指标。
- 把 `kernel-interpreter-loop` 活体执行和 A2/subagent 作为产品路径挂起缺口单独复审。

只有 S1-S4 通过后，才能讨论 AI graph/patch 和 strategy promotion 的受控实验价值。只有 S5 挂起缺口也解决后，才能讨论把 AI graph/patch 或 strategy promotion 接入正式产品路径。
