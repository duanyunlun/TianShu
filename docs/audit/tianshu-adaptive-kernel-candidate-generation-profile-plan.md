# TianShu 自适应内核候选生成能力与第 4 层噪声探针计划

## 1. 文档定位

本文是 `docs/audit/tianshu-adaptive-kernel-gap-closure-plan.md` 完成后的下一轮专项计划，目标是拆开“工程接线可行”和“AI 真的具备 StageGraph / patch 候选生成能力”这两个问题。

上一轮补缺结论为 `adaptive-candidate-pass-with-estimated-token`，它只能说明：

- Kernel -> Runtime 固定图执行、trace、checkpoint/recovery、metrics 接线具备工程基础。
- C full matrix 在 `--output-schema` 强约束下达到 50/50 最终合法。
- CLI / runner 能端到端读取 `estimated=true/source=text_length_estimate` 的候选生成 token。
- D strategy evolution 因真实 provider usage、price model/cost、effect-size/promotion/rollback/human gate 缺失而 fail-closed。

该结论不能说明：

- 模型已经在弱约束下理解并稳定生成 StageGraph / patch。
- StageGraph 已经可替代产品默认 agent turn loop。
- 系统已经具备自演化、自优化或自动 promotion 能力。

本文规划下一轮两段验证：

1. **第 3 层候选生成 profile 对照**：验证 AI 生成 StageGraph / patch 的能力是否只是被 `--output-schema` 强约束掩盖。
2. **第 4 层轻量噪声探针**：不做 D promotion，只判断在同一候选集合与同一任务下，策略选择信号是否可能被自然方差和噪声支配。

本文不启动 D，不替换产品主路径，不修改产品默认 turn loop。

## 2. 四层判读口径

下一轮必须把自适应内核拆成四层判断，避免把底层工程进展误判为自演化能力。

| 层级 | 当前判断 | 下一轮处理 |
| --- | --- | --- |
| 治理内核 | 可行且有价值。P1-P5 已证明 Stable Kernel Core、validator、fail-closed、trace/replay、checkpoint/recovery 的工程骨架成立。 | 保留为基础，不在本轮重复证明。 |
| StageGraph IR | 可行但不能高估。StageGraph 可作为可审计、可验证、可回放的中间表示，但尚未证明能替代 reactive agent turn loop。 | 本轮只验证候选生成质量，不证明产品 loop 替换。 |
| AI 生成 graph/patch | 有希望但证据不足。上一轮 50/50 发生在 `--output-schema` 强约束下，只能判定 `schema-enforced candidate pass`。 | 本轮通过 profile 对照验证 schema 约束强度对结果的影响。 |
| 自演化/自优化 | 当前不成立。缺真实 provider usage/cost、baseline 对照、strategy trial、effect-size、rollback、human gate、长期稳定性观测。 | 本轮只做轻量噪声探针，不做 D，不做 promotion，不输出 `feasible-controlled`。 |

## 3. 核心问题

本轮回答两个问题：

1. AI 到底是在理解并生成 StageGraph / patch，还是只是被 output schema 强制填表？
2. 即使候选生成存在信号，策略选择是否稳定到足以支撑后续第 4 层自演化研究，还是会被重复运行的自然方差、latency/token 波动和成功率噪声支配？

第一个问题必须控制变量，只改变 schema 约束方式。第二个问题必须固定候选策略集合与任务集，只观察同一批任务重复运行下的胜率、退化、波动和拒绝行为；它不是 D，不做 promotion，也不需要真实 provider cost。

## 4. 涉及项目与文件

| 路径 | 角色 |
| --- | --- |
| `tools/Run-TianShuAdaptiveKernelFeasibility.ps1` | 下一轮 profile 对照与噪声探针 runner，需增加 profile 参数、统一 prompt/task、输出分组 summary 与噪声探针 verdict。 |
| `src/Presentations/TianShu.Cli` | 当前唯一优先消费层；继续使用当前分支构建产物，不使用用户级安装 CLI。 |
| `src/Core/TianShu.Kernel/AdaptiveAcceptance` | typed candidate mapper 与 patch apply 事实基础。 |
| `src/Core/TianShu.Kernel/Validation` | `KernelValidator` / StageGraph 合法性验证事实基础。 |
| `tests/TianShu.Kernel.Tests` | mapper、validator、candidate fixture 守护测试。 |
| `tests/TianShu.Cli.Tests` | CLI token usage 输出与架构边界守护测试。 |
| `docs/audit/evidence/adaptive-kernel-candidate-generation-profile/` | 下一轮候选生成 profile 证据根目录。 |
| `docs/audit/evidence/adaptive-kernel-evolution-noise-probe/` | 下一轮第 4 层轻量噪声探针证据根目录。 |
| `Test/TianShuAdaptiveKernelCandidateGenerationProfile.__live/` | 下一轮 live 临时输出根目录。 |
| `Test/TianShuAdaptiveKernelEvolutionNoiseProbe.__live/` | 下一轮噪声探针 live 临时输出根目录。 |

## 5. 固定变量

三组 profile 必须共享以下变量；否则不能归因到 schema 控制方式。

| 变量 | 固定要求 |
| --- | --- |
| 任务集 | 使用同一批 C task，至少覆盖 simple graph、single tool graph、multi tool graph、model route graph、human gate graph、patch、recovery、checkpoint、context policy、A2 abstract graph。 |
| prompt | 同一 prompt contract。任务目标、字段要求、风险要求、输出语言要求必须一致；G2 不能把完整 JSON Schema 原文搬进 prompt，只允许使用同等字段/形状描述和少量示例。 |
| 模型 | 同一个 `Model` 参数。 |
| temperature | 若可固定则固定；若不可固定，记录为自然方差。 |
| seed | 若可固定则固定；若不可固定，记录为自然方差。 |
| repeat count | 每组每类任务至少 5 次；若预算允许，建议 10 次。 |
| revise 规则 | 每次最多 3 轮 revise，只回传结构化 rejection reason。 |
| validator | 同一 `Test-CCandidate` / mapper / validator / interpreter dry-run 路径。 |
| token 口径 | estimated token 只用于 P6 诊断；不得进入 D 成本或 promotion。 |

### Prompt 等价规则

为避免 G2 只是“把 `--output-schema` 搬进 prompt”，本轮必须预注册 prompt contract：

- G1、G2、G3 的任务目标、候选类型、风险要求、禁止事项、revise 指令必须一致。
- G2 可以描述字段名、数组/对象形状、必要风险约束，但不得粘贴完整 JSON Schema 原文。
- G2 可以包含最多一个极简 JSON skeleton；该 skeleton 只能表达字段层级，不得包含完整枚举、完整 validation schema 或大量固定值。
- G1/G3 使用的 schema path、schema hash、schema profile 必须记录。
- 如果 G2 prompt 比 G1/G3 更详细或更宽松，结果只能作为诊断，不得纳入 profile verdict。

### JSON 抽取规则

三组必须使用同一 JSON 抽取规则：

- 只接受完整 JSON 对象，或一个 fenced `json` code block 中的完整 JSON 对象。
- 不允许从自然语言中截取多个片段后拼装。
- 不允许根据上下文自动补字段、改字段名、改枚举值或补默认 budget。
- 如果输出多个 JSON 对象，默认判为 ambiguous，除非预注册规则明确只取第一个 fenced JSON block。
- JSON parse 失败、ambiguous、字段缺失、字段大小写不匹配均计入 rejection reason。

## 6. Profile 定义

### G1：schema-enforced

目的：保留上一轮强约束基线，证明结构化输出通道、validator、revise、summary gate 可用。

执行要求：

- 调用 CLI `exec --json --output-schema <schema>`。
- prompt 中仍包含同一任务描述和字段要求。
- 结果只能解释为 `schema-enforced candidate pass`。

不可得出的结论：

- 不能说模型已经自主理解 StageGraph。
- 不能说弱约束候选生成能力已通过。

### G2：schema-hinted-no-output-schema

目的：验证在不传 `--output-schema` 时，模型是否仍能根据同一 prompt 和 schema hint 生成合法候选。

执行要求：

- 不传 `--output-schema`。
- prompt 内包含与 G1 等价的字段要求、JSON 形状、风险约束。
- 不能人工修补模型输出。
- revise 只允许使用结构化 validation errors。

判读重点：

- 如果 G2 最终合法率接近 G1，才有“弱约束候选生成能力”的初步证据。
- 如果 G2 显著低于 G1，则上一轮高合法率主要来自 schema 强约束。

### G3：schema-full-strict

目的：验证候选生成能否迁移到更接近正式 contract 的严格 schema，而不是依赖最小宽松 schema。

执行要求：

- 传 `--output-schema <strict-schema>`。
- strict schema 应更接近正式 `StageGraph` / patch / recovery / checkpoint / context policy contract。
- strict schema 不得通过降低风险字段、移除 budget、移除 policy、放宽 side effect 来提高合法率。

判读重点：

- 如果 G3 稳定，说明候选生成对正式合同迁移有更强外推价值。
- 如果 G3 明显低于 G1，说明当前 schema-enforced 结果仍偏向演示级最小 schema。

## 7. 预注册指标

每个 profile 必须输出同一组指标。

| 指标 | 说明 |
| --- | --- |
| `firstLegalRate` | 首轮合法率。 |
| `finalLegalRate` | 最多 3 轮 revise 后最终合法率。 |
| `averageReviseRounds` | 平均 revise 轮数。 |
| `maxReviseRounds` | 最大 revise 轮数。 |
| `oscillationRate` | revise 过程中合法性状态反复率。 |
| `overreachRate` | 高风险 stage 未正确要求 human gate 或越权比例。 |
| `degenerateCoreLoopRate` | 合法 graph 是否退化为单个或全量 `core_loop` stage。 |
| `tokenAvailableRate` | 候选生成 token 是否可归因。 |
| `estimatedTokenRate` | estimated token 比例。 |
| `costAvailableRate` | 成本是否可用；P6 不要求可用，D 才要求。 |
| `modelCallMean` | 每次任务平均模型调用数。 |
| `latencyMeanMs` | 平均候选生成耗时。 |
| `rejectionDistribution` | rejection reason 分布。 |

## 8. 预注册阈值与结论映射

本轮不得事后挑选 profile 或修改阈值。

| 结论 | 条件 |
| --- | --- |
| `schema-enforced-pass-only` | G1 达到 `finalLegalRate >= 0.85` 且 `averageReviseRounds <= 1.5`，但 G2 未达到弱约束阈值。 |
| `weak-schema-candidate-signal` | G2 达到 `finalLegalRate >= 0.75` 且 `averageReviseRounds <= 2.0`，同时 overreach/degenerate 为 0 或有明确安全拒绝证据。 |
| `weak-schema-gray-zone` | G2 位于 `0.65 <= finalLegalRate < 0.75`，或与 G1 差距过大但仍有可解释成功样本；只能输出弱信号待复核，不能作为能力证明。 |
| `strict-contract-candidate-signal` | G3 达到 `finalLegalRate >= 0.85` 且 `averageReviseRounds <= 1.5`。 |
| `candidate-generation-profile-signal` | G1 通过、G2 达到弱约束阈值、G3 通过，并且三组都没有高风险越权或 core-loop 退化。该结论只代表第 3 层候选生成信号。 |
| `candidate-generation-not-proven` | G1 未通过，或 G2/G3 结果无法解释，或存在未被 validator 拒绝的高风险越权。 |

说明：

- `candidate-generation-profile-signal` 仍不等于 `feasible-controlled`，也不等于第 4 层自演化可行。
- `schema-enforced-pass-only` 仍有工程价值，但只能说明 tool/schema contract 路径可用。
- G2 阈值低于 G1/G3，是因为弱约束自然更难；但它只能作为“能力信号”，不能作为 D promotion 前置。
- G2 与 G1 的差距必须单独报告；若 G1 接近 1.0 而 G2 仅略过 0.75，必须标注“强 schema 依赖仍显著”。

## 9. D 的硬前置限制

本轮无论 profile 结果多好，都不得启动 D promotion。

D strategy evolution 的硬前置仍是：

- 真实 provider usage，不是 `estimated=true/source=text_length_estimate`。
- price model 与 cost 可归因。
- P2B Runtime provider bridge metrics event 可用。
- effect-size、promotion gate、rollback gate、human gate 已预注册。
- baseline 对照采样已完成。
- strategy candidate 关联 risk profile、rollback plan、evaluation plan。

如果真实 usage 未补齐，D 必须继续 fail-closed。

## 10. 第 4 层轻量噪声探针

### 10.1 目标

该探针只回答一个问题：

> 在候选生成存在信号的情况下，系统是否能在同一批任务与同一候选策略集合中稳定选择更优策略，还是会被自然方差和噪声支配？

该探针不是 D strategy evolution：

- 不做 automatic promotion。
- 不写入长期策略。
- 不需要真实 provider cost。
- 不把 estimated token 当真实成本。
- 不输出 `feasible-controlled`。

### 10.2 前置条件

至少满足其一才启动探针：

- G2 达到 `weak-schema-candidate-signal`。
- G1/G3 通过但 G2 未通过，且用户明确只验证 schema-enforced tool-use 路线的策略选择噪声。

如果 G1 都未通过，不启动第 4 层探针。

### 10.3 探针设计

固定一组小型 candidate strategy set，建议 2 到 3 个策略：

- `strategy-baseline-fixed`：固定保守策略，作为对照。
- `strategy-schema-enforced-candidate`：来自 G1/G3 的合法候选图或 patch。
- `strategy-weak-schema-candidate`：仅当 G2 有合法候选时加入。

探针必须把“候选生成阶段”和“候选执行阶段”分开记录：

- 候选生成阶段只用于形成并冻结候选 strategy，记录 token、latency、model-call count、revise rounds、rejection reason 等诊断指标。
- 候选执行阶段才用于比较 strategy 优劣。探针比较的是同一任务在不同 strategy 下的最终输出质量、成功率、退化率与执行成本，不比较“生成该候选花了多少 token”。
- 若每次重复运行都重新生成候选，则该轮只能作为候选生成方差诊断，不能纳入第 4 层 selection signal verdict。

对同一批任务重复执行已冻结的 strategy，每个 strategy 每个任务至少 10 次，记录：

- final success / fail / blocked。
- final output quality score 或等价 rejection reason。
- execution latency。
- execution estimated token；仅用于同环境相对波动，不作为真实成本。
- execution model-call count。
- overreach / unsafe / human-gate missing。
- regression count：相对 baseline 明显劣化的次数。
- candidate generation diagnostics：候选生成阶段的 token、latency、model-call count、revise rounds、rejection reason。

如果产品 Runtime 级 strategy executor 尚不可用，本轮允许使用 `frozen_candidate_validator_dry_run` 作为 harness-level 探针：

- baseline strategy 使用固定保守 contract baseline。
- candidate strategy 使用 profile 阶段已经冻结的最终候选，不重新调用模型生成候选。
- 执行阶段只做本地 JSON 抽取、contract validation、risk / degenerate 检查和质量评分。
- 每条样本必须记录 `executionKind=frozen_candidate_validator_dry_run`、`sampleSource`、`reusedFrozenCandidate` 和是否进入产品 Runtime。
- 该路径只能回答“冻结候选相对固定 baseline 是否存在可测优化信号”，不能证明产品 Runtime live execution、真实成本或可 promotion。

### 10.4 判读口径

| 结论 | 条件 |
| --- | --- |
| `evolution-noise-too-high` | 候选 strategy 与 baseline 存在可测差异，但 winner 在重复运行中频繁翻转，或差异方向被自然方差覆盖。 |
| `evolution-no-measurable-delta` | 候选 strategy 与 baseline 没有达到预注册最小有意义差异；这代表缺少可优化信号，不等同于噪声过高。 |
| `evolution-risk-too-high` | 候选 strategy 出现未被拒绝的高风险越权、human gate 缺失或 regression rate 超阈值。 |
| `evolution-selection-signal` | 候选 strategy 在同任务重复运行中稳定优于 baseline，且没有安全 regression；该结论只允许进入真实 usage / D gate 前置讨论。 |
| `evolution-probe-not-run` | 第 3 层前置不满足或用户选择不运行。 |

默认阈值：

- 每个 strategy 每个任务至少 10 次。
- 必须预注册最小有意义差异，例如 final success rate 提升至少 5 个百分点，或 final output quality score 达到预注册提升阈值；未达到则输出 `evolution-no-measurable-delta`。
- candidate success rate 不低于 baseline。
- candidate regression rate 小于等于 5%。
- candidate winner flip rate 小于等于 20%；只在超过最小有意义差异的 strategy 之间计算。
- high-risk overreach 必须为 0。

说明：

- 该探针可以使用 execution estimated token，因为它比较的是同一运行环境下的波动与相对稳定性，不计算真实成本。
- candidate generation diagnostics 只能解释候选生成成本与方差，不能单独证明 strategy 更优。
- 如果 token 波动过大，只能说明“estimated token 对第 4 层成本判断无效”，不能据此推进 D。
- 探针通过后仍不能启动 D，必须先补真实 provider usage / price model / gates。

### 10.5 探针失败后的路线

如果第 3 层通过但第 4 层探针失败，结论应为：

> 保留 StageGraph-as-trace / schema-enforced tool-use，不推进自演化；D 只保留人工策略或固定策略方向。

如果探针输出 `evolution-no-measurable-delta`，结论应为：

> 当前候选 strategy 与 baseline 没有证明出可优化空间；应先改进任务集、质量评分或候选策略设计，而不是继续扩大自演化范围。

## 11. Runner 改造清单

实施前必须先将本计划同步到 `docs/tianshu-implementation-tracker.md` 的“正在做”。

建议 runner 增加：

- [ ] `-RunCProfiles`：运行 profile 对照。
- [ ] `-CProfiles schema-enforced,schema-hinted-no-output-schema,schema-full-strict`：选择 profile。
- [ ] `-CProfileRepeatCount` 或复用 `-RepeatCount`。
- [ ] `-CProfilePromptTemplateVersion`：记录 prompt 模板版本。
- [ ] `-CProfileAcceptancePath`：输出 profile acceptance manifest。
- [ ] 对 G2 禁止传 `--output-schema` 的守护。
- [ ] 对 G1/G3 强制记录 schema path 与 schema hash。
- [ ] summary 增加 `byProfile`、`byProfileAndTask`、`profileComparison`。
- [ ] 输出 `profileVerdict.json`，给出本轮结论枚举。
- [ ] `-RunEvolutionNoiseProbe`：运行第 4 层轻量噪声探针。
- [ ] `-EvolutionProbeRepeatCount`：每个 strategy / task 的重复次数，默认 10。
- [ ] `-EvolutionProbeStrategySetPath`：预注册候选策略集合。
- [ ] `-EvolutionProbeMinDeltaPath`：预注册最小有意义差异阈值。
- [ ] 输出 `evolution-noise-probe-summary.json` 与 `evolution-noise-probe-verdict.json`。
- [ ] summary 明确分开 `candidateGenerationDiagnostics` 与 `strategyExecutionComparison`。
- [ ] verdict 输出 `measurableDelta`、`effectSizeSummary` 与 winner flip 计算口径。

## 12. 证据要求

下一轮必须保存：

- `manifest.json`
- `profile-acceptance-manifest.json`
- 每组 `runs.json`
- 每组 `summary.json`
- 总汇总 `profile-comparison-summary.json`
- 判读 `profile-verdict.json`
- 噪声探针 `evolution-noise-probe-summary.json`
- 噪声探针 `evolution-noise-probe-verdict.json`
- 噪声探针最小差异阈值 `evolution-noise-probe-min-delta.json`
- 命令记录 `commands.md`
- 结果摘要 `results.md`

证据目录：

```text
docs/audit/evidence/adaptive-kernel-candidate-generation-profile/
```

live 临时目录：

```text
Test/TianShuAdaptiveKernelCandidateGenerationProfile.__live/
```

第 4 层噪声探针证据目录：

```text
docs/audit/evidence/adaptive-kernel-evolution-noise-probe/
```

live 临时目录：

```text
Test/TianShuAdaptiveKernelEvolutionNoiseProbe.__live/
```

## 13. 禁止事项

- 不允许降低 validator 严格度来提升合法率。
- 不允许隐藏失败样本。
- 不允许人工修补模型输出后计入成功。
- 不允许 G2 偷传 `--output-schema`。
- 不允许 G1/G3 使用与 G2 不同难度的任务或 prompt。
- 不允许把 estimated token 作为真实 cost。
- 不允许把候选生成阶段 token / revise 成本直接当作 strategy 执行优势或劣势。
- 不允许把本轮结果用于 D automatic promotion。
- 不允许把旧 AppHost turn loop 的能力计入 adaptive StageGraph runtime loop live-pass。
- 不允许把第 3 层 profile verdict 当作第 4 层 go/no-go 结论。
- 不允许把 `evolution-no-measurable-delta` 误写成 `evolution-noise-too-high`。
- 不允许在噪声探针失败时继续推进自演化路线。

## 14. 推荐执行顺序

1. 审核本文。
2. 将本文拆成 tracker “正在做”清单。
3. 改造 runner profile 参数与 summary 输出。
4. 先跑 dry-run，确认三组命令差异只有 schema 控制方式。
5. 跑 G1/G2/G3 smoke，每组每类任务 1 次。
6. smoke 通过后，跑 full matrix，每组每类任务至少 5 次。
7. 输出 profile verdict。
8. 若 profile verdict 满足探针前置，运行第 4 层轻量噪声探针。
9. 输出 evolution noise probe verdict。
10. 根据两个 verdict 决定下一步：
   - 若只是 `schema-enforced-pass-only`：回到 prompt/schema/IR 设计，不推进 D。
   - 若噪声探针输出 `evolution-no-measurable-delta`：先改进任务集、质量评分或候选策略设计，不推进自演化。
   - 若达到 `weak-schema-candidate-signal` 但噪声探针失败：保留 StageGraph-as-trace / schema-enforced tool-use，不推进自演化。
   - 若达到 `candidate-generation-profile-signal` 且噪声探针出现 `evolution-selection-signal`：补真实 provider usage、price model 和 gates，再考虑 D。

## 15. 下一步之后的路线

本轮完成后有三条合理路线：

- 若候选生成能力未证明：继续收敛 StageGraph IR、prompt/profile、schema 设计，不推进自演化。
- 若候选生成有信号但噪声探针失败或没有可测差异：保留 StageGraph-as-trace、schema-enforced tool-use、人工策略或固定策略方向；不推进自演化。
- 若候选生成有信号且噪声探针显示稳定 selection signal：优先补真实 provider usage / cost / price model / gates，再重启 D fail-closed trial。

在真实 usage 与 D gates 补齐前，最终状态最多只能是 `feasible-with-limits`、`candidate-generation-profile-signal` 或 `evolution-selection-signal-without-promotion`，不能是 `feasible-controlled`。
