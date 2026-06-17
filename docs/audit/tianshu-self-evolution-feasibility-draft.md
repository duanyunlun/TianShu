# TianShu 自演化阶段性可行性报告草案

## 1. 报告定位

本文是 P30.11 的阶段性草案，用于记录 TianShu 自演化基础设施当前能证明什么、不能证明什么，以及 P31.8 正式可行性报告需要继续收集哪些证据。

本文不是最终结论，不用于宣称 TianShu 已经实现可靠自主进化。当前可以确认的是：P30 已建立一条受 Stable Kernel Core 约束的自演化实验链路，候选策略可以被结构化生成、验证、plan-only trial、评价、统计聚合、进入受控生命周期，并在需要时回滚。是否能产生稳定收益，仍必须通过 P31.8 的正式证据报告判断。

## 2. 当前证据范围

当前证据来自代码、合同、测试和正式设计文档，不依赖历史临时证据目录。

| 证据面 | 当前项目 / 文件 |
| --- | --- |
| Kernel 合同 | `src/Contracts/TianShu.Contracts.Kernel` |
| Kernel 抽象 | `src/Core/TianShu.Kernel.Abstractions` |
| Stable Kernel Core、validator、candidate validation、trial | `src/Core/TianShu.Kernel` |
| Adaptive orchestrator 与 KernelTool | `src/Core/TianShu.Kernel.Adaptive` |
| Strategy registry、evaluator、cross-review、objective anchor、aggregation | `src/Core/TianShu.Kernel.Strategies` |
| 自演化设计基线 | `docs/architecture/tianshu-self-evolution-design.md` |
| Kernel loop 设计基线 | `docs/architecture/tianshu-kernel-core-loop-design.md` |
| 主要测试 | `tests/TianShu.Contracts.Kernel.Tests`、`tests/TianShu.Kernel.Abstractions.Tests`、`tests/TianShu.Kernel.Tests`、`tests/TianShu.Kernel.Adaptive.Tests`、`tests/TianShu.Kernel.Strategies.Tests`、`tests/TianShu.Execution.Integration.Tests` |

P30.11 只总结当前基础设施，不要求重新跑 live provider 验收。live provider 成本、真实 usage、真实任务收益和多轮长期稳定性属于 P31.8 正式报告的证据范围。

## 3. 当前能度量什么

### 3.1 候选生成可以度量

当前可以度量 Adaptive Orchestration Layer 是否只生成结构化候选：

- `IAdaptiveOrchestrator.ProposeAsync` 返回 `KernelProposalSet`。
- 默认候选生成器至少生成 `direct`、`context_guarded`、`recovery_checked` 三类 `StageGraphProposal`。
- 每个候选带有 `StageGraph`、`RiskProfile`、`KernelBudgetImpact`、`RollbackPlan` 和 `EvaluationPlan`。
- Adaptive 层和 KernelTool 不返回 `RuntimeStep`。

这能证明 AI 参与入口被约束在 proposal / operation 层，不能直接驱动外部副作用。

### 3.2 候选验证可以度量

当前可以度量每个候选是否通过 Stable Kernel Core 的验证门禁：

- schema 检查。
- deterministic kernel 检查。
- governance 检查。
- budget 检查。
- capability 检查。
- 接受 / 拒绝状态。
- 结构化 issue code。

已覆盖的失败样例包括 capability 超出治理 allow-list、无界预算、治理副作用上限越界、非法 graph、缺失 route candidate、context policy 非法等。

### 3.3 Plan-only trial 可以度量

当前可以度量候选在不执行 Runtime 的条件下能否被物化为候选 `ExecutionPlan`：

- `ShadowRun` 记录候选 plan 和基线 plan 的差异。
- `BoundedPlanTrial` 重新验证候选 RuntimeStep。
- trial 报告显式记录 `executedRuntime=false`。
- trial 报告显式记录 `promotedStrategy=false`。
- 未通过验证的候选会生成 skipped trial record。

这能证明 trial 目前只收集计划级证据，不产生外部副作用，也不晋升策略。

### 3.4 基础 evaluator 可以度量

当前默认 `KernelEvaluator` 可以从 run result 和 trace 投影确定性基础信号：

- success / failure。
- replay compatible。
- policy violation attempt。
- trace evidence refs。
- metric observations。
- overall confidence。
- disagreement score。

这些指标证明 evaluator 已有结构化证据载体，但还不是完整收益评估体系。

### 3.5 异质交叉评审可以度量

当前 `IKernelCrossReviewExperimentService` 可以聚合已经提交的 B/C 评审结果：

- reviewer id。
- model route ref。
- provider / model。
- metric score。
- confidence。
- uncertainty。
- reason。
- evidence ref。
- `ModelJudgeDisagreement`。
- `requiresHumanGate`。

这能证明模型裁判信号可以被结构化收集和冲突检测。当前服务不直接调用 provider，因此它证明的是聚合能力，不证明真实模型评审链路已经完整自动化。

### 3.6 客观锚点校准可以度量

当前 `IKernelObjectiveAnchorCalibrationService` 可以消费已采集的客观锚点：

- build succeeded。
- tests passed。
- golden answer。
- human label。
- objective anchor score。
- model judge score。
- original / calibrated confidence。
- `ObjectiveAnchorConflict`。
- `requiresHumanGate`。

这能证明模型裁判可以被客观锚点约束。当前服务不执行 build / test，也不采集 human label；它只消费已提供的锚点证据。

### 3.7 策略级统计聚合可以度量

当前 `IKernelStrategyEvaluationAggregationService` 可以按 strategy 聚合多次样本：

- sample count。
- metric mean / min / max。
- average confidence。
- estimated count。
- disagreement count。
- objective anchor conflict count。
- missing evidence count。
- model-judge-only 状态。
- promotion-ready 信号。
- blocking reasons。

这能证明单次模型偏好不会直接驱动 promotion。样本不足、只有模型裁判、存在 human-gate 分歧或客观锚点冲突时，聚合不会输出可晋升候选。

### 3.8 策略生命周期可以度量

当前 `IStrategyRegistry` 可以度量和审计策略生命周期：

- candidate 注册。
- trial。
- promoted。
- deprecated。
- rolled_back。
- evidence refs。
- metric refs。
- human approval。
- reason ref。
- lifecycle audit record。

这能证明 registry 已具备受控 promotion / rollback 通道。聚合报告不会自动修改 registry；promotion 仍必须显式走 registry transition gate。

## 4. 当前不能度量什么

### 4.1 不能度量真实长期收益

当前没有固定 benchmark corpus、长期 canary 数据、真实用户任务统计或多轮回归样本。因此不能证明某个自演化策略相对基线有稳定收益。

### 4.2 不能度量真实 provider 评审质量

交叉评审服务只聚合已提交的评审结果，不直接调用 provider。当前可以验证结构化评审合同和分歧处理，不能证明真实 B/C 模型评审链路已经可靠。

### 4.3 不能度量真实 cost

provider usage 和 cost 必须来自 provider 返回的真实 usage 与 price model。estimated token 只能作为 diagnostics 和降级参考，不能冒充真实 usage 或真实 cost。

### 4.4 不能度量自动 promotion 正确性

当前没有自动 promotion。`PromotionReady` 只是 evidence signal，不会自动修改 strategy registry。长期默认策略晋升仍需要 trace、evaluation evidence、metric refs、rollback plan 和必要 human gate。

### 4.5 不能度量高风险外部副作用安全性

当前 trial 是 plan-only，不调用 Execution Runtime，不产生真实外部副作用。因此不能把 P30 结果解释为高风险工具、shell、写入或远端能力已经可自动演化。

### 4.6 不能度量模型自主改写稳定内核

Stable Kernel Core、Kernel validator、governance 默认值、RuntimeStep approval、module trust、release gate 和发布脚本都不允许由模型自动改变。当前设计有意不让模型演化这些对象，因此不能把它们计入“自主进化能力”。

## 5. 当前失败样例

当前失败样例用于证明链路 fail closed，而不是证明模型能力强弱。

| 类别 | 失败样例 | 期望结果 |
| --- | --- | --- |
| 候选 schema | proposal 为空或不是 `StageGraphProposal` | 候选 rejected，输出 schema / deterministic issue |
| graph deterministic | graph 无终态、不可达、有无界循环 | `ValidateGraphAsync` rejected |
| governance | graph 副作用超过 `GovernanceEnvelope` | 候选 rejected，分类为 governance |
| budget | graph budget 为零或无界 | 候选 rejected，分类为 budget |
| capability | stage capability tool 超出治理 allow-list | 候选 rejected，分类为 capability |
| trial | 候选未通过 validation | trial skipped，不执行 Runtime，不 promotion |
| model judge | B/C 同一指标评分差异超过阈值 | 生成 `ModelJudgeDisagreement`，要求 human gate |
| objective anchor | 模型裁判分数与客观锚点冲突 | 生成 `ObjectiveAnchorConflict`，降低 calibrated confidence，要求 human gate |
| aggregation | 样本不足 | gate 为 `InsufficientEvidence` |
| aggregation | 只有模型裁判，无客观锚点或可观测证据 | gate 为 `RequiresHumanGate` |
| aggregation | 存在 human-gate 分歧或锚点冲突 | gate 为 `BlockedByDisagreement` |
| lifecycle | candidate 缺 evidence | registry 拒绝登记 |
| lifecycle | candidate 直接 promoted | illegal transition rejected |
| promotion | promotion 缺 metric refs | rejected |
| promotion | promotion 缺 human approval | rejected |
| rollback | promoted strategy rolled back | 移出 promoted 查询和 candidate 列表，保留 audit record |

## 6. 阶段性判断

截至 P30.11，当前结论是：

- 可控性：阶段性可行。Stable Kernel Core、validator、governance、RuntimeStep approval、trace、promotion gate 和 rollback 边界没有被模型绕过。
- 可观测性：阶段性可行。候选、验证、trial、评价、分歧、聚合和 lifecycle 都有结构化报告或审计记录。
- 可回滚性：阶段性可行。strategy registry 已具备 rolled_back 状态和审计记录，rolled back strategy 不再作为 promoted / candidate 被选中。
- 自主收益：尚不能证明。当前没有足够 live benchmark、长期样本、真实 cost、真实 provider 评审和 canary 数据。
- 自主进化：尚不能证明。当前系统允许模型提出受控编排候选，但不允许模型自动改写稳定内核或自动晋升长期默认策略。

因此，P30 的阶段性成果应表述为：TianShu 已具备受控自演化实验基础设施的第一版闭环；它还没有证明“自演化一定有效”。

## 7. P31.8 正式报告需要补齐的证据

P31.8 正式自演化可行性报告至少需要补齐以下证据：

1. 固定任务集。
   - 定义稳定 benchmark corpus。
   - 每个任务必须有输入、预期输出、客观锚点和可复现运行命令。
2. 基线与候选对照。
   - 每个候选 strategy 必须和固定 baseline 比较。
   - 同一任务至少运行多次，避免单轮模型偏好。
3. 真实 runtime evidence。
   - 记录 run id、trace id、execution id、plan id、graph id、stage id 和 step id。
   - 真实执行仍必须经过 `ExecutionPlan` / `RuntimeStep`。
4. 真实 usage 与 cost。
   - provider 返回 usage 时记录真实 usage。
   - provider 缺失 usage 时记录 `provider_usage_missing`。
   - estimated token 不得计入真实 cost。
5. 真实交叉评审链路。
   - B/C 评审 provider 调用必须通过受治理 RuntimeStep / Module 路径。
   - evaluator / cross-review service 不得私自调用 provider。
6. 客观锚点采集链路。
   - build、test、golden answer、human label 必须有 evidence ref。
   - 服务只消费锚点；采集动作必须由受治理 runtime / tool path 执行。
7. canary 与 rollback 边界。
   - canary 默认只允许低风险、可回滚策略。
   - 高风险策略不得自动进入默认路径。
   - rollback 触发条件必须明确并可审计。
8. 人工 gate 证据。
   - promotion、policy change、高风险副作用必须保留审批引用。
   - 缺审批不得通过 release gate。
9. 失败样本。
   - 正式报告必须保留失败样本和阻断原因。
   - 不允许只展示成功样本。

## 8. 下一步实验边界

下一步实验必须保持以下边界：

- 不允许模型修改 Stable Kernel Core、Kernel validator、governance 默认值、module trust、release gate 或发布脚本。
- 不允许 evaluator、cross-review、objective-anchor calibration 或 aggregation 服务直接调用 provider、tool、shell、build、test 或 Runtime。
- 不允许 estimated token / cost 伪装成真实 usage / cost。
- 不允许单次模型裁判、单次客观锚点或单次统计报告直接 promotion。
- 不允许失败、拒绝、blocked 或缺证据样本被记录为 completed。
- 不允许把 P30.11 草案当成 P31.8 正式可行性结论。

P31.8 可以给出的结论类型包括：

- 可行：有稳定 benchmark、可复现证据和多样本统计支持，并满足治理和 rollback 边界。
- 部分可行：某些低风险策略可控有效，但高风险或长期默认策略仍需人工 gate。
- 当前无法测量：缺真实任务样本、usage、cost、客观锚点或长期 canary 数据。
- 不可行：证据显示候选策略无法稳定优于基线，或治理成本高于收益。

## 9. 当前草案结论

当前最保守、最准确的结论是：TianShu 的自演化基础设施在 P30 阶段已经具备可验证、可审计、可回滚的实验闭环；但它还没有证明 AI 可以稳定自主提升内核运作机制。后续工作应优先补齐真实 benchmark、真实 runtime evidence、真实 usage / cost、真实交叉评审路径和 canary rollback 数据，再发布 P31.8 正式报告。
