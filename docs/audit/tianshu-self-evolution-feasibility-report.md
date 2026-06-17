# TianShu 自演化可行性报告

## 1. 结论

P31.8 的正式结论是：**部分可行**。

当前代码、合同、测试和发布门禁可以证明 TianShu 已经具备一套受 Stable Kernel Core 约束的自演化实验基础设施：AI 可以在受控边界内提出结构化 StageGraph / strategy 候选，候选会经过 schema、确定性、治理、预算、能力、trial、评价、统计聚合、promotion gate 和 rollback 边界审查。

当前证据不能证明 TianShu 已经实现可靠自主进化，也不能证明某个 AI 生成策略能长期稳定优于固定基线。真实长期收益、真实 provider 评审质量、真实 cost、自动 promotion 正确性和高风险副作用下的自适应安全性仍然未被证明。

因此，TianShu 当前可以对外表述为：

- 已提供受控自演化实验链路。
- 已具备候选生成、验证、plan-only trial、评价、聚合、审计和 rollback 的结构化基础设施。
- 尚未证明可靠自主进化收益。
- 不允许模型绕过 Stable Kernel Core、governance、RuntimeStep approval、module trust、human gate、promotion gate 或 release gate。

## 2. 报告范围

本文替代 `docs/audit/tianshu-self-evolution-feasibility-draft.md` 作为当前 P31.8 正式结论入口。P30.11 草案仍保留为阶段性记录，但不得再作为最终可行性结论引用。

本报告只基于当前仓库内可查证的代码、合同、测试、设计文档和本地验证命令，不使用已清理的临时 evidence 目录，也不把公开仓库 Release 状态作为本地架构可行性证据。

| 证据面 | 当前证据 |
| --- | --- |
| 自演化设计基线 | `docs/architecture/tianshu-self-evolution-design.md` |
| 总架构边界 | `docs/tianshu-architecture-spec.md` |
| Kernel 合同 | `src/Contracts/TianShu.Contracts.Kernel` |
| Kernel 抽象 | `src/Core/TianShu.Kernel.Abstractions` |
| Stable Kernel Core / validation / trial | `src/Core/TianShu.Kernel` |
| Adaptive orchestrator / KernelTool | `src/Core/TianShu.Kernel.Adaptive` |
| Strategy registry / evaluator / cross-review / objective anchor / aggregation | `src/Core/TianShu.Kernel.Strategies` |
| Runtime 边界 | `src/Core/TianShu.RuntimeComposition`、`src/Execution/TianShu.Execution.Runtime` |
| 主要测试 | `tests/TianShu.Contracts.Kernel.Tests`、`tests/TianShu.Kernel.Abstractions.Tests`、`tests/TianShu.Kernel.Tests`、`tests/TianShu.Kernel.Adaptive.Tests`、`tests/TianShu.Kernel.Strategies.Tests`、`tests/TianShu.Execution.Integration.Tests` |

## 3. 已证明的能力

### 3.1 结构化候选生成可行

当前 Adaptive Orchestration Layer 的候选入口已经收敛为结构化对象：

- `IAdaptiveOrchestrator` 返回 `KernelProposalSet`。
- `IAdaptiveStageGraphCandidateGenerator` 返回多个 `StageGraphProposal`。
- 默认候选包含 `direct`、`context_guarded`、`recovery_checked` 等 profile。
- KernelTool 只能返回 `KernelProposal` 或 `KernelOperation`，不得直接返回 `RuntimeStep`。

这证明 AI 参与点被限定在 proposal / operation 层，不能直接绕过 Kernel 执行外部副作用。

### 3.2 候选验证可行

当前 `IAdaptiveCandidateValidationService` 已覆盖候选进入 trial 前的基础审查：

- schema validation。
- deterministic kernel checks。
- governance checks。
- budget checks。
- capability checks。
- 结构化 accepted / rejected / skipped 状态。
- issue code 和 check category。

失败样例包括非法 graph、能力越界、预算非法、治理副作用上限越界、缺失 route / context policy 等。验证失败必须 fail closed，不进入 Runtime，不替换默认 graph，不晋升 strategy。

### 3.3 Plan-only trial 可行

当前 `IAdaptiveCandidateTrialService` 支持候选 plan-only trial：

- `ShadowRun` 记录候选 plan 与基线 plan 的 step、budget、side effect、step kind 差异。
- `BoundedPlanTrial` 只物化候选 `ExecutionPlan` 并重新验证 RuntimeStep。
- trial 报告显式保持 `executedRuntime=false`。
- trial 报告显式保持 `promotedStrategy=false`。

这证明候选可以在不执行真实外部副作用的情况下收集计划级差异证据。

### 3.4 Evaluator 结构化证据可行

当前 `KernelEvaluationResult` 可以携带：

- `KernelEvaluationEvidenceSet`。
- `KernelEvaluationMetricObservation`。
- `KernelEvaluationDisagreement`。
- overall confidence。
- disagreement score。
- estimated signal 标记。

默认 `KernelEvaluator` 可以从 run result 与 trace 投影 `success`、`replay_compatible`、`policy_violation_attempt` 等基础指标。estimated token / cost 只能作为 diagnostics，不得冒充真实 provider usage 或真实 cost。

### 3.5 异质交叉评审聚合可行

当前 `IKernelCrossReviewExperimentService` 可以聚合 B/C 结构化评审提交：

- reviewer id。
- model route。
- provider / model。
- score / confidence / uncertainty。
- reason。
- evidence ref。
- `ModelJudgeDisagreement`。
- `requiresHumanGate`。

同一指标评分差异达到阈值时会生成分歧并要求 human gate。该服务只聚合已提交结果，不直接调用 provider，不直接 promotion。

### 3.6 客观锚点校准可行

当前 `IKernelObjectiveAnchorCalibrationService` 可以消费已采集的客观锚点：

- build succeeded。
- tests passed。
- golden answer。
- human label。
- model judge score。
- calibrated confidence。
- `ObjectiveAnchorConflict`。
- `requiresHumanGate`。

模型裁判与客观锚点冲突时，校准服务会降低模型裁判置信度并要求 human gate。服务只消费锚点，不直接执行 build/test/tool/provider。

### 3.7 策略级统计聚合可行

当前 `IKernelStrategyEvaluationAggregationService` 可以从多次样本生成策略级比较：

- sample count。
- metric mean / min / max。
- average confidence。
- estimated count。
- disagreement count。
- objective anchor conflict count。
- missing evidence count。
- model-judge-only 状态。
- gate conclusion。

样本不足、只有模型裁判、存在 human-gate 分歧或客观锚点冲突时不得输出 promotion-ready。即使输出 promotion-ready，也只是 promotion evidence，不能绕过 strategy registry gate。

### 3.8 Strategy lifecycle 与 rollback 可行

当前 `IStrategyRegistry` 支持：

- `Candidate`。
- `Trial`。
- `Promoted`。
- `Deprecated`。
- `RolledBack`。
- lifecycle audit record。
- evidence refs。
- metric refs。
- human approval。
- reason ref。

promotion 必须带 metric refs 和必要 human approval；rollback 后的 strategy 不再作为 promoted / candidate 被默认选择，同时保留 audit record。

## 4. 未证明的能力

### 4.1 未证明真实长期收益

当前没有稳定 benchmark corpus、长期 canary 观测、多轮真实用户任务统计或跨版本收益曲线。因此不能证明自演化策略长期优于固定基线。

### 4.2 未证明真实 provider 评审质量

交叉评审服务只聚合已经提交的评审结果，不直接驱动 provider 调用。当前可以证明评审结果结构化、分歧检测和 human gate，但不能证明真实 B/C provider 评审链路本身可靠。

### 4.3 未证明真实 usage / cost 优化

provider usage 与 cost 必须来自 provider 返回的真实 usage 与明确 price model。当前 estimated token 只能作为 diagnostics 和降级参考，不能证明真实成本收益。

### 4.4 未证明自动 promotion 正确性

当前没有自动 promotion。聚合报告的 promotion-ready 只是证据信号，最终仍必须通过 registry transition、metric refs、rollback plan 和必要 human approval。

### 4.5 未证明高风险副作用下的自主演化安全性

当前 trial 是 plan-only，不调用 Execution Runtime，不产生真实外部副作用。因此不能把当前结果解释为高风险写入、shell、MCP、远端控制或生产 side effect 已可自动演化。

### 4.6 未证明模型可自主改写稳定内核

Stable Kernel Core、Kernel validator、governance 默认值、RuntimeStep approval、module trust、release gate 和发布脚本均不允许模型自动修改。当前系统有意禁止模型演化这些对象，因此它们不属于已证明的自主进化能力。

## 5. 关键失败样例与边界

| 类别 | 失败样例 | 当前期望 |
| --- | --- | --- |
| schema | proposal 为空或类型不符 | rejected，记录 schema issue |
| deterministic | graph 无终态、不可达或循环无界 | rejected |
| governance | side effect 超过 envelope | rejected，分类 governance |
| budget | token / time / cost / retry 无界或非法 | rejected，分类 budget |
| capability | tool / module 不在 allow-list 或健康状态不满足 | rejected，分类 capability |
| trial | 候选 validation 未通过 | skipped，不执行 Runtime，不 promotion |
| model judge | B/C 评分差异超过阈值 | 生成 `ModelJudgeDisagreement`，要求 human gate |
| objective anchor | 模型裁判与 build/test/golden/human label 冲突 | 生成 `ObjectiveAnchorConflict`，降低 confidence，要求 human gate |
| aggregation | 样本不足 | `InsufficientEvidence` |
| aggregation | 只有模型裁判 | `RequiresHumanGate` 或非 promotion-ready |
| aggregation | 存在分歧或锚点冲突 | `BlockedByDisagreement` |
| lifecycle | candidate 缺 evidence | registry 拒绝 |
| lifecycle | candidate 直接 promoted 或 transition 非法 | fail closed |
| promotion | 缺 metric refs / rollback plan / human approval | rejected |
| rollback | promoted strategy rolled back | 移出默认选择路径，保留 audit |

## 6. 当前验收判定

| 判断项 | 结论 | 说明 |
| --- | --- | --- |
| 可控性 | 通过 | Stable Kernel Core、validator、governance、RuntimeStep approval、module trust、promotion gate 和 rollback 边界未被模型绕过。 |
| 可观测性 | 通过 | 候选、验证、trial、评价、交叉评审、客观锚点、统计聚合和 lifecycle 都有结构化报告或审计记录。 |
| 可回滚性 | 通过 | Strategy registry 支持 rolled_back 状态和 audit record。 |
| 收益可测性 | 未通过 | 缺 benchmark corpus、长期 canary、真实 usage / cost 和多轮真实任务统计。 |
| 自主进化收益 | 未证明 | 当前只证明受控实验基础设施，不证明 AI 稳定自主提升内核运作机制。 |
| 高风险自动演化 | 未证明 | 当前 trial 不执行高风险 RuntimeStep，也不允许绕过 human gate。 |

## 7. 后续准入条件

任何后续把“部分可行”提升为“可行”的工作，至少必须补齐以下证据：

1. 固定 benchmark corpus：每个任务有输入、预期输出、客观锚点、可复现命令。
2. 基线与候选对照：每个候选 strategy 和固定 baseline 多次对比。
3. 真实 runtime evidence：保留 run id、trace id、execution id、plan id、graph id、stage id、step id。
4. 真实 usage / cost：provider 返回 usage 时记录真实 usage；缺失时记录 `provider_usage_missing`；estimated token 不得计入真实 cost。
5. 真实交叉评审链路：B/C provider 调用必须通过受治理 RuntimeStep / Module 路径。
6. 客观锚点采集链路：build、test、golden answer、human label 必须有 evidence ref。
7. canary 与 rollback：低风险、可回滚、可审计；高风险策略不得自动进入默认路径。
8. human gate 证据：promotion、policy change、高风险副作用必须保留审批引用。
9. 失败样本：正式报告必须保留失败样本和阻断原因，不允许只展示成功样本。

## 8. 正式边界

以下边界在 P31.8 后继续有效：

- 不允许模型修改 Stable Kernel Core、Kernel validator、governance 默认值、module trust、release gate 或发布脚本。
- 不允许 evaluator、cross-review、objective-anchor calibration 或 aggregation 服务直接调用 provider、tool、shell、build、test 或 Runtime。
- 不允许 estimated token / cost 伪装成真实 usage / cost。
- 不允许单次模型裁判、单次客观锚点或单次统计报告直接 promotion。
- 不允许失败、拒绝、blocked 或缺证据样本被记录为 completed。
- 不允许文档、README、release notes 或验收报告宣称 TianShu 已经实现可靠自主进化。

## 9. 本轮验证命令

P31.8 正式报告落地时执行了以下验证：

```powershell
dotnet test tests\TianShu.Execution.Integration.Tests\TianShu.Execution.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~SelfEvolutionDocs_ShouldDeclareExploratoryBoundaryAndStableKernelCoreGate" /p:UseSharedCompilation=false
dotnet test tests\TianShu.Contracts.Kernel.Tests\TianShu.Contracts.Kernel.Tests.csproj -c Release /p:UseSharedCompilation=false
dotnet test tests\TianShu.Kernel.Abstractions.Tests\TianShu.Kernel.Abstractions.Tests.csproj -c Release /p:UseSharedCompilation=false
dotnet test tests\TianShu.Kernel.Tests\TianShu.Kernel.Tests.csproj -c Release /p:UseSharedCompilation=false
dotnet test tests\TianShu.Kernel.Adaptive.Tests\TianShu.Kernel.Adaptive.Tests.csproj -c Release /p:UseSharedCompilation=false
dotnet test tests\TianShu.Kernel.Strategies.Tests\TianShu.Kernel.Strategies.Tests.csproj -c Release /p:UseSharedCompilation=false
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Test-TianShuV10DocumentationReleaseGate.ps1 -Configuration Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Test-TianShuPublicReleaseSafetyScan.ps1 -Configuration Release -SkipRegressionTests
```

这些命令验证的是合同、抽象、默认实现、文档门禁和发布安全扫描，不验证真实 provider 评审质量、真实长期收益或真实 usage / cost。

## 10. 最终表述

TianShu 自演化当前的可接受表述是：

> TianShu 已具备受 Stable Kernel Core 约束的自演化实验基础设施，能够对 AI 提出的编排候选进行结构化生成、验证、plan-only trial、评价、统计聚合、审计和 rollback。当前结论是部分可行：控制链路可行，收益可测性和可靠自主进化仍未证明。

任何更强表述都必须等后续 benchmark、真实 runtime、真实 usage / cost、真实交叉评审、canary / rollback 和 human gate 证据补齐后再更新。
