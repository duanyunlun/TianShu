# TianShu 自演化基础设施设计规范

## 1. 文档定位

本文是 TianShu v0.10.0 自演化基础设施的专项设计基线，细化 `docs/tianshu-architecture-spec.md` 中的可控演化内核和 `docs/architecture/tianshu-kernel-core-loop-design.md` 中的 Adaptive Orchestration Layer。

自演化在 TianShu 中是探索性能力，不承诺成功，也不承诺一定能产生长期正收益。当前目标是建设一套可验证、可审计、可回滚的实验基础设施，让模型可以在受控边界内提出编排候选、接受验证、进入 trial、被评价、被晋升或被回滚。是否真正具备稳定收益，必须由后续可行性报告基于证据给出结论。

本文只记录当前有效设计，不保留历史方案、讨论过程或临时验证产物。

## 2. 当前代码归属

| 项目 | 归属内容 |
| --- | --- |
| `src/Contracts/TianShu.Contracts.Kernel` | `CoreIntent`、`StageGraph`、`KernelProposal`、`KernelOperation`、`KernelTrace`、`KernelEvaluationResult`、`StrategyRecord` 等公共契约。 |
| `src/Core/TianShu.Kernel.Abstractions` | `IStableKernelCore`、`IAdaptiveOrchestrator`、`IAdaptiveStageGraphCandidateGenerator`、`IAdaptiveCandidateValidationService`、`IAdaptiveCandidateTrialService`、`IKernelValidator`、`IStageGraphInterpreter`、`IKernelTraceStore`、`IKernelEvaluator`、`IKernelCrossReviewExperimentService`、`IKernelObjectiveAnchorCalibrationService`、`IKernelStrategyEvaluationAggregationService`、`IStrategyRegistry` 抽象。 |
| `src/Core/TianShu.Kernel` | Stable Kernel Core、StageGraph interpreter、validator、candidate validation / trial service、状态机、trace store 和默认 graph。 |
| `src/Core/TianShu.Kernel.Adaptive` | Adaptive Orchestration Layer、StageGraph 候选生成器和 KernelTool 家族，负责生成 `KernelProposal` / `KernelOperation`。 |
| `src/Core/TianShu.Kernel.Strategies` | strategy registry、strategy lifecycle、evaluation metric projection、heterogeneous cross-review aggregation、objective anchor calibration、promotion / rollback 证据管理。 |
| `src/Core/TianShu.RuntimeComposition` | 将已批准 `ExecutionPlan` 交给 Execution Runtime 的组合层，不拥有自演化决策语义。 |
| `src/Execution/TianShu.Execution.Runtime` | 执行已批准 RuntimeStep，不生成 strategy、StageGraph 或 KernelProposal。 |

对应测试项目包括：

- `tests/TianShu.Kernel.Tests`
- `tests/TianShu.Kernel.Adaptive.Tests`
- `tests/TianShu.Kernel.Strategies.Tests`
- `tests/TianShu.Kernel.Abstractions.Tests`
- `tests/TianShu.Execution.Integration.Tests`

## 3. 探索性质与非承诺边界

自演化能力的正式表述必须保持以下口径：

- 它是探索性基础设施，不是稳定收益承诺。
- 它只能证明 TianShu 具备受控生成、验证、试运行、评价、晋升和回滚候选策略的能力。
- 它不能仅凭单次模型判断证明某个策略更优。
- 它不能把模型偏好、自然语言理由或一次 live 结果直接等同为稳定收益。
- 它不能以“自适应”或“自进化”为理由绕过稳定内核、治理边界、人工 gate、审计和回滚要求。
- 它的当前阶段性结论以 `docs/audit/tianshu-self-evolution-feasibility-report.md` 为准：部分可行。控制链路、可观测性和回滚边界已具备基础设施证据；真实长期收益和可靠自主进化仍未证明。

因此，文档、README、release note 和验收报告不得宣称 TianShu 已实现可靠自主进化；只能声明已提供受控自演化实验链路和可观测边界。

## 4. 不可绕过边界

Stable Kernel Core 是自演化链路不可被模型绕过的稳定边界。任何 AI、KernelTool、StrategyRegistry、RuntimeComposition、Execution Runtime、Module 或宿主入口都不得直接修改或绕过以下对象：

- Stable Kernel Core 本身。
- Kernel validator。
- StageGraph interpreter。
- Kernel run lifecycle 状态机。
- governance envelope。
- RuntimeStep approval。
- tool / module permission 和 side effect policy。
- human gate。
- module trust / loading boundary。
- trace、audit、diagnostics、replay invariants。
- strategy promotion gate。
- rollback requirement。

模型可以提出候选，但候选必须先成为结构化 `KernelProposal` 或 `KernelOperation`，再由 Stable Kernel Core 验证。验证失败必须 fail closed，并输出结构化 rejection reason。

## 5. AI 允许参与的对象

AI 可以在受控边界内影响以下对象：

| 对象 | 允许行为 | 生效条件 |
| --- | --- | --- |
| Stage | 提出或修正 stage 定义。 | 必须形成 StageGraph 或 StageGraph patch proposal。 |
| StageGraph | 组合多个候选 graph。 | 必须通过 schema、确定性、治理、预算和能力校验。 |
| Model route policy | 提出 route 候选或排序建议。 | 必须由 Kernel validator 和 Runtime route bridge 接受。 |
| Tool strategy | 提出工具选择策略。 | 必须不超过 StageGraph allow-list、governance envelope 和 module descriptor。 |
| Context policy | 提出上下文裁切、压缩、引用策略。 | 必须经过 ContextPolicy bridge 验证，保护段不得丢失。 |
| Recovery plan | 提出失败恢复路径。 | 必须绑定失败 stage、error signal、预算和 rollback plan。 |
| Checkpoint strategy | 提出 checkpoint 点。 | 必须不扩大副作用，不绕过 host / runtime state 边界。 |
| Evaluation plan | 提出评价维度。 | 必须被 evaluator 解释为结构化指标，不得只保存自然语言判断。 |
| Strategy lifecycle 建议 | 建议 trial、promotion、rollback。 | 必须通过 lifecycle gate、trace、评价证据、回滚方案和必要人工确认。 |

## 6. AI 禁止自动改变的对象

以下对象不能由 AI 自动改变：

- Stable Kernel Core 的代码、状态机和验证规则。
- governance 默认值和 human gate 要求。
- RuntimeStep approval 规则。
- Module loading、module trust、third-party allow-list。
- secret、credential、private endpoint 和本地路径脱敏策略。
- provider/tool/memory 模块的副作用声明。
- 默认发布门禁。
- 高风险策略的长期晋升状态。
- release、CI、打包、安装脚本的发布安全边界。

如需改变这些对象，只能进入人工设计、代码审查、测试和发布流程；模型最多能提出 `propose_kernel_policy_change` 这类候选，不得自动生效。

## 7. 候选生成链路

自演化候选的标准链路如下：

```text
CoreIntent
  -> Stable Kernel Core admits intent
  -> Adaptive Orchestration Layer proposes candidates
  -> KernelTool returns KernelProposal / KernelOperation
  -> Stable Kernel Core validates candidate
  -> Candidate becomes validated strategy or rejected record
```

候选生成必须满足：

- 输出为结构化对象，不是自然语言 plan。
- `IAdaptiveOrchestrator` 只能返回 `KernelProposalSet`。
- `IAdaptiveStageGraphCandidateGenerator` 只能返回多个结构化 `StageGraphProposal`。
- `IKernelTool` 只能返回 `KernelProposal` 或 `KernelOperation`，不得返回 `RuntimeStep`。
- Capability call 必须经 `request_capability_call -> KernelOperation -> RuntimeStep approval -> Execution Runtime`。
- 任何候选必须声明风险、预算影响、评估计划和回滚方案。

当前 `TianShu.Kernel.Adaptive` 的默认工具目录包括 stage、graph、route、tool strategy、context、checkpoint、recovery、evaluation、promotion、rollback 和 policy change 相关 KernelTool；这些工具是候选生成入口，不是直接执行入口。P30.2 起，默认 StageGraph 候选生成器至少生成 `direct`、`context_guarded`、`recovery_checked` 三类结构化候选，用于分别覆盖直接只读执行、上下文优先保护和失败恢复预案；这些候选仍只是 proposal，P30.3 起必须先经过 `IAdaptiveCandidateValidationService` 生成结构化候选验证报告。

## 8. 验证门禁

候选进入 trial 前必须通过以下验证：

| 验证 | 最低要求 |
| --- | --- |
| schema validation | proposal、operation、StageGraph、Stage、edge、policy、budget 字段完整且类型有效。 |
| deterministic kernel checks | graph 有唯一入口、可达终态、循环有界、状态转移合法。 |
| governance checks | 不超过 `GovernanceEnvelope` 的 allow-list、副作用上限和 human gate 要求。 |
| budget checks | token、时间、成本、重试、工具调用预算有界且不为负。 |
| capability checks | tool / module 已声明、已发现、已允许、健康状态满足当前策略。 |
| runtime-step checks | RuntimeStep 必须有 graph、stage、operation、permission、side effect、budget 来源。 |
| trace checks | 候选必须能产出可回放 trace 和 diagnostics ref。 |

当前 P30.3 已落地的验证闭环如下：

- `IAdaptiveCandidateValidationService` 接收 `KernelProposalSet` 和 `KernelValidationContext`，逐个候选输出 `AdaptiveCandidateValidationReport`。
- `AdaptiveCandidateValidationReport` 由 `AdaptiveCandidateValidationRecord` 组成，记录 `proposalId`、`proposalKind`、`graphId`、候选状态和检查记录。
- 检查记录必须按 `schema`、`deterministic kernel`、`governance`、`budget`、`capability` 分类，复用 `IKernelValidator` 的 fail-closed 规则和 rejection issue code。
- `StableKernelCore` 在 adaptive 模式下会生成 proposal、运行候选验证服务、写入 `ProposalReviewed` trace，并把验证报告放入 `KernelRunResult.Metadata["adaptiveCandidateValidationReport"]`。
- P30.3 不执行候选、不做 shadow run、不把候选替换为默认执行图、不晋升策略；这些行为只能由 P30.4 及后续阶段实现。

验证失败时：

- 不得进入 Execution Runtime。
- 不得自动降级为旧 loop 或不受控路径。
- 不得把 blocked / rejected 伪造成 completed。
- 必须记录结构化 rejection reason。

## 9. Trial 与 Shadow Run

validated 候选不得直接成为默认策略。它只能进入受限 trial 或 shadow run：

| 模式 | 用途 | 约束 |
| --- | --- | --- |
| shadow run | 对比候选和当前基线的计划、预算、风险、预期输出，不产生真实外部副作用。 | 默认只读或无副作用；用于采集差异，不用于直接晋升。 |
| bounded plan trial | 在限定 intent、限定工具、限定预算和限定副作用内物化候选 ExecutionPlan，并重新验证 RuntimeStep。 | 不调用 Execution Runtime；必须记录 trace、diff、validation issue code；高风险后续真实 trial 需要 human gate。 |
| canary strategy | 低比例、可撤回地使用候选策略。 | 必须保留基线对照、失败阈值和自动回滚条件。 |

当前 P30.4 已落地的 trial / shadow run 机制如下：

- `IAdaptiveCandidateTrialService` 接收 `KernelProposalSet`、P30.3 `AdaptiveCandidateValidationReport`、基线 `StageGraph` 和基线 `ExecutionPlan`。
- 只有 P30.3 已接受的 `StageGraphProposal` 才能进入 trial；未接受候选必须生成 skipped 记录。
- `ShadowRun` 只生成候选 plan 与基线 plan 的结构化 diff，包括 step count、budget、side effect 和 step kind 差异。
- `BoundedPlanTrial` 只物化候选 `ExecutionPlan` 并重新验证 RuntimeStep，不调用 `IExecutionRuntime.ExecuteAsync`。
- `StableKernelCore` 在默认 graph 的 ExecutionPlan 获批后运行 trial 服务，写入 `ProposalReviewed` trace，并把报告放入 `KernelRunResult.Metadata["adaptiveCandidateTrialReport"]`。
- P30.4 报告必须显式保持 `executedRuntime=false`、`promotedStrategy=false`；该报告不能单独作为 promotion 依据。

trial 的目标是收集证据，不是证明成功。单次 trial 成功不能直接 promotion。

## 10. Evaluation 与度量边界

Evaluator 的职责是把 trace、runtime result、diagnostics、测试结果、用户反馈和客观锚点投影为结构化评价。评价可以包含：

- success / failure。
- replay compatibility。
- policy violation attempt。
- provider / tool / runtime error。
- token、cost、latency。
- test result。
- recovery success。
- user correction。
- objective anchor result。
- model judge score。
- disagreement / uncertainty。

度量边界如下：

- provider 未返回 usage 时必须记录 `provider_usage_missing`，不得静默置零。
- estimated token 只能作为 diagnostics 和降级参考，不得冒充真实 provider usage 或真实 cost。
- 模型裁判只能作为信号之一，不得单独决定 promotion。
- 客观锚点与模型判断冲突时，必须记录分歧并进入人工 gate 或继续 trial。
- 策略级比较必须聚合多次结果，不能用单轮模型偏好驱动晋升。

P30.5 起，评价基础设施的正式合同如下：

| 合同 | 归属 | 语义 |
| --- | --- | --- |
| `KernelEvaluationEvidenceSet` | `TianShu.Contracts.Kernel` | 保存 trace、runtime metrics、diagnostics、objective anchor、model judge、human feedback 的引用集合，不保存敏感原文。 |
| `KernelEvaluationMetricObservation` | `TianShu.Contracts.Kernel` | 单个指标观测，必须声明 `metricKind`、`signalKind`、`evidenceRef`、`confidence`、是否 `estimated`，可携带 score、observed value、unit 和 metadata。 |
| `KernelEvaluationDisagreement` | `TianShu.Contracts.Kernel` | 记录客观锚点、模型裁判或指标之间的冲突，必须引用相关 metric id，并声明 severity 与是否需要 human gate。 |
| `KernelEvaluationResult` | `TianShu.Contracts.Kernel` | 兼容旧 `metricScores`，同时携带 evidence、observations、disagreements、overall confidence 和 disagreement score。 |

当前默认 `KernelEvaluator` 只从 run result 与 trace 生成确定性基础指标：`success`、`replay_compatible`、`policy_violation_attempt`。这些指标分别投影为 observable / objective anchor 信号，并保留 trace / replay evidence ref。provider usage、真实 cost 和人工反馈仍属于后续工作；在接入前不得伪造这些证据。

P30.6 起，异质交叉评审实验的正式边界如下：

| 合同 | 归属 | 语义 |
| --- | --- | --- |
| `KernelCrossReviewReviewerSpec` | `TianShu.Contracts.Kernel` | 声明评审者 B/C 的 reviewer id、model route、provider、model 和待评审指标。 |
| `KernelCrossReviewMetricScore` | `TianShu.Contracts.Kernel` | 单个评审者对单个指标的 score、confidence、uncertainty、reason 和 evidence ref。 |
| `KernelCrossReviewSubmission` | `TianShu.Contracts.Kernel` | 单个评审者的结构化提交，必须至少包含一个评分。 |
| `KernelCrossReviewExperimentRequest` | `TianShu.Contracts.Kernel` | 将执行者 A 的 run / trace / baseline evaluation 与至少两个不同 provider/model 的评审者提交绑定为一次实验。 |
| `KernelCrossReviewExperimentReport` | `TianShu.Contracts.Kernel` | 聚合评审者报告、model judge observations、平均分、平均置信度、平均不确定性、分歧和 human gate 信号。 |
| `IKernelCrossReviewExperimentService` | `TianShu.Kernel.Abstractions` | 交叉评审实验服务抽象。 |

当前默认 `KernelCrossReviewExperimentService` 只聚合已提交的 B/C 结构化评审结果，不直接调用 provider，不生成新的模型输出。它会把每个评分投影为 `KernelEvaluationMetricObservation(metricKind=ModelJudge, signalKind=ModelJudge)`，把评分理由、评审者、model route、provider、model、source metric id 和 uncertainty 写入 metadata。若同一 source metric 的评分差异达到 `disagreementThreshold`，服务必须生成 `KernelEvaluationDisagreement(kind=ModelJudgeDisagreement)` 并设置 `requiresHumanGate=true`。该报告只能作为 evaluation evidence，不得直接 promotion。

P30.7 起，客观锚点校准的正式边界如下：

| 合同 | 归属 | 语义 |
| --- | --- | --- |
| `KernelObjectiveAnchorKind` | `TianShu.Contracts.Kernel` | 客观锚点类型：`BuildSucceeded`、`TestsPassed`、`GoldenAnswer`、`HumanLabel`。 |
| `KernelObjectiveAnchorObservation` | `TianShu.Contracts.Kernel` | 单个锚点的 anchor id、kind、source metric id、score、confidence、reason 和 evidence ref。 |
| `KernelObjectiveAnchorCalibrationRequest` | `TianShu.Contracts.Kernel` | 将模型裁判 observations 与客观锚点绑定为一次校准请求。 |
| `KernelObjectiveAnchorCalibrationReport` | `TianShu.Contracts.Kernel` | 输出 objective anchor observations、校准后 model judge observations、锚点/模型分数、原始/校准后置信度、分歧和 human gate 信号。 |
| `IKernelObjectiveAnchorCalibrationService` | `TianShu.Kernel.Abstractions` | 客观锚点校准服务抽象。 |

当前默认 `KernelObjectiveAnchorCalibrationService` 只消费已采集的 build/test/golden answer/human label 锚点，不直接执行 build、test 或外部命令。它会将锚点投影为 `KernelEvaluationMetricObservation(metricKind=ObjectiveAnchor, signalKind=ObjectiveAnchor)`，并按 `sourceMetricId` 比较模型裁判与客观锚点分数。若差异达到 `conflictThreshold`，服务必须生成 `KernelEvaluationDisagreement(kind=ObjectiveAnchorConflict)`、降低模型裁判 calibrated confidence，并设置 `requiresHumanGate=true`。校准结果只能作为 evaluation evidence，不得直接 promotion。

P30.8 起，策略级统计聚合的正式边界如下：

| 合同 | 归属 | 语义 |
| --- | --- | --- |
| `KernelStrategyEvaluationSample` | `TianShu.Contracts.Kernel` | 单个策略评价样本，绑定 strategy、run、trace、evaluation，并可携带 cross-review 与 objective-anchor calibration 报告。 |
| `KernelStrategyMetricAggregate` | `TianShu.Contracts.Kernel` | 单个 source metric 的样本数、均值、最小值、最大值、平均置信度、estimated 数量和信号来源集合。 |
| `KernelStrategyComparison` | `TianShu.Contracts.Kernel` | 单个策略的统计比较结果，输出 sample count、平均分、平均置信度、分歧数量、客观锚点冲突、缺失证据、model-judge-only 状态和 gate 结论。 |
| `KernelStrategyEvaluationAggregationRequest` | `TianShu.Contracts.Kernel` | 将多个策略评价样本绑定为一次聚合请求，并声明每个策略的最低样本数。 |
| `KernelStrategyEvaluationAggregationReport` | `TianShu.Contracts.Kernel` | 输出所有策略 comparison、best candidate ref、promotion-ready 是否存在和 human gate 信号。 |
| `IKernelStrategyEvaluationAggregationService` | `TianShu.Kernel.Abstractions` | 策略级评价聚合服务抽象。 |

当前默认 `KernelStrategyEvaluationAggregationService` 只消费已生成的 `KernelEvaluationResult`、`KernelCrossReviewExperimentReport` 和 `KernelObjectiveAnchorCalibrationReport`，不调用 provider，不执行 Runtime，不执行 registry transition。它按 strategy 聚合多次样本，并按 source metric 计算均值、范围、置信度和 estimated 数量。样本数低于 `minimumSamplesPerStrategy` 时 gate 为 `InsufficientEvidence`；存在需要人工处理的分歧或客观锚点冲突时 gate 为 `BlockedByDisagreement`；只有模型裁判且没有客观锚点 / 可观测证据时 gate 为 `RequiresHumanGate`；只有多样本、无阻断分歧、无缺失证据且具备非模型裁判证据时才可标记 `PromotionReady`。该标记仍只是 promotion evidence，不得绕过 strategy registry 的 promotion gate、metric refs、rollback plan 和 human approval 要求。

## 11. Promotion 与 Rollback

策略生命周期固定为：

```text
Candidate -> Trial -> Promoted -> Deprecated
                         \-> RolledBack
```

`Draft` / `Validated` 只作为既有代码的兼容边界保留，不属于正式自演化主链；新的策略资产必须以 `Candidate` 进入 registry，并携带 lifecycle audit record。

promotion 的最低条件：

- 候选已通过 Stable Kernel Core 验证。
- 有完整 run trace。
- 有 evaluation evidence。
- 有 metric refs。
- 有 rollback plan。
- 没有未解释的 policy violation。
- 高风险策略或长期默认策略必须有 human gate。

rollback 的最低条件：

- 记录 rollback reason。
- 记录触发 rollback 的 run、trace、evaluation 或人工决策。
- 保留被回滚版本，支持 replay 和审计。
- 从 strategy registry 中移出默认选择路径。

当前 `TianShu.Kernel.Strategies` 的 strategy registry 已要求 promotion evidence 携带 metric refs，并对 promotion 路径保留人工批准要求。后续 P30 只能在此边界内扩展，不得弱化。

P30.9 起，strategy registry 生命周期的正式边界如下：

| 合同 / 接口 | 归属 | 语义 |
| --- | --- | --- |
| `StrategyLifecycleState.Candidate` | `TianShu.Contracts.Kernel` | 经过 Stable Kernel Core / evaluator 证据约束后登记的候选策略状态，是正式主链入口。 |
| `StrategyLifecycleAuditRecord` | `TianShu.Contracts.Kernel` | 记录 strategy id、previous state、target state、evidence refs、metric refs、human approval、reason ref 和 occurred at。 |
| `IStrategyRegistry.SaveCandidateAsync` | `TianShu.Kernel.Abstractions` | 注册候选策略，必须携带 evidence，并写入 candidate audit record。 |
| `IStrategyRegistry.ListAuditRecordsAsync` | `TianShu.Kernel.Abstractions` | 查询某个 strategy 的生命周期审计记录。 |
| `IStrategyRegistry.TransitionAsync` | `TianShu.Kernel.Abstractions` | 执行 candidate / trial / promoted / deprecated / rolled_back 状态转换，必须先通过 transition validation。 |

当前默认 `StrategyRegistry` 的正式转换规则是：`Candidate -> Trial -> Promoted -> Deprecated`，以及任一已登记策略可进入 `RolledBack`。`Promoted` 必须携带 metric refs 和 human approval；`Deprecated` 与 `RolledBack` 必须携带 evidence ref；非法转换必须 fail closed。每次 candidate 注册和状态转换都会追加 `StrategyLifecycleAuditRecord`，并同步到 `StrategyRecord.LifecycleAuditRecords`。该审计记录只是 registry 事实，不代表模型可以绕过 Stable Kernel Core 或 promotion gate。

## 12. P30 实施映射

| 项目 | 目标 |
| --- | --- |
| P30.1 | 明确探索性质、非承诺边界和 Stable Kernel Core 不可绕过。 |
| P30.2 | 让 Adaptive orchestrator 输出多个结构化 StageGraph 候选。 |
| P30.3 | 建立候选验证闭环：候选报告覆盖 schema、deterministic kernel、governance、budget、capability 检查，但不执行、不试运行、不晋升。 |
| P30.4 | 建立 trial / shadow run：候选可 plan-only 试运行、记录差异，不执行 Runtime、不直接晋升。 |
| P30.5 | 定义 evaluator 度量基础设施：结构化 evidence、metric observation、confidence、estimated signal 与 disagreement。 |
| P30.6 | 实现异质交叉评审实验：A 执行、至少两个异质 B/C 评审者提交结构化评分，输出理由、分歧、不确定性与 human gate 信号。 |
| P30.7 | 接入客观锚点校准：build/test/golden answer/human label 作为已采集证据校准模型裁判置信度，并输出锚点冲突。 |
| P30.8 | 从单轮评价上升到策略级统计聚合：多样本聚合、统计比较、promotion readiness 与阻断原因。 |
| P30.9 | 完整 strategy registry lifecycle：candidate、trial、promoted、deprecated、rolled_back 与可审计变更记录。 |
| P30.10 | 补齐非法候选、分歧、锚点冲突、promotion gate、rollback 测试。 |
| P30.11 | 产出阶段性可行性报告草案。 |

P30.11 阶段性草案位于 `docs/audit/tianshu-self-evolution-feasibility-draft.md`。该草案只总结当时基础设施能度量、不能度量、已知失败样例和下一步实验边界，不是 P31.8 正式自演化可行性结论。

P31.8 正式报告位于 `docs/audit/tianshu-self-evolution-feasibility-report.md`。当前结论为部分可行：候选生成、验证、plan-only trial、评价、统计聚合、审计和 rollback 的受控实验链路可行；真实 benchmark、真实 usage / cost、真实交叉评审质量、长期 canary 收益和可靠自主进化仍未证明。

P30 的交付标准不是“证明自演化成功”，而是证明自演化实验链路可验证、可控、可回滚。

## 13. 验收基线

P30.1 起，自演化相关文档、代码和测试必须满足：

- 正式文档明确“探索性能力，不承诺成功”。
- 正式文档明确 Stable Kernel Core 不可被模型修改或绕过。
- Adaptive Orchestration Layer 只能返回结构化 `KernelProposalSet`。
- StageGraph candidate generator 必须返回多个结构化 `StageGraphProposal`。
- Adaptive candidate validation service 必须为每个候选输出结构化验证报告，并明确接受或拒绝原因。
- Adaptive candidate trial service 必须输出 shadow / bounded plan trial 差异报告，且不得执行 Runtime 或晋升策略。
- Kernel evaluation 必须携带结构化 evidence、metric observation、confidence 和 disagreement；估算值、模型裁判和单轮评价不得直接驱动 promotion。
- 异质交叉评审必须至少包含两个不同 provider/model 的评审者，必须输出结构化 score、reason、uncertainty、model judge observation 和 disagreement，不得直接调用 provider 或晋升策略。
- 客观锚点校准必须支持 build succeeded、tests passed、golden answer、human label 四类锚点；锚点冲突必须降低模型裁判置信度并进入 human gate，不得直接晋升策略。
- 策略级统计聚合必须聚合多次样本；样本不足、只有模型裁判、存在 human-gate 分歧或客观锚点冲突时不得输出 promotion-ready；聚合报告不得执行 strategy promotion。
- Strategy registry 必须以 candidate / trial / promoted / deprecated / rolled_back 为正式主链；candidate 注册、trial、promotion、deprecation 和 rollback 都必须写入 lifecycle audit record。
- KernelTool 只能返回 `KernelProposal` 或 `KernelOperation`，不得直接返回 RuntimeStep。
- RuntimeStep 只能由 Stable Kernel Core 批准后交给 Execution Runtime。
- promotion 必须有 trace、evaluation evidence、metric refs、rollback plan 和必要 human gate。
- failed / rejected / blocked 不得伪造成 completed。
- 自演化可行性结论以 P31.8 正式报告为准，当前只能表述为部分可行，不得宣称可靠自主进化已经实现。
