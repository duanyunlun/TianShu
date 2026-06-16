# 天枢 / TianShu 架构可行性审计

## 0. 本文定位与使用方式

本文是对 `docs/` 下全部架构与设计文档的一次独立审计，面向 **Codex 执行验证**。

本文不修改任何架构结论，只做两件事：

1. 标出当前设计中**尚未被证明、却已被当成既定前提**的风险点。
2. 把"这套自适应架构是否可行"拆成**可证伪的命题**，并为每个命题给出**最小实验**和**判死线（kill-criterion）**，供 Codex 逐条验证。

使用原则：

- Codex 必须按本文第 3 节的**验证阶梯顺序**执行，不得跳序。任一前置命题被判死，其依赖命题不再验证。
- 每个实验的产物必须落成**可审计证据**（代码、测试、trace、统计输出），不接受"我认为可行"式结论。
- 验证实验允许是脏的、一次性的 spike，**故意放在正式分层之外**，目的只是尽快回答"行不行"，不得反过来要求先收敛架构再验证。
- 本文不替代 `docs/tianshu-architecture-spec.md`；spec 仍是架构基线。本文只判定基线中**自适应相关部分**的可行性。

---

## 1. 审计范围

已通读并纳入审计：

- `docs/tianshu-architecture-spec.md`（总体架构验收基线）
- `docs/architecture/*.md`（六层分层、Kernel、Control、Execution、Module、Contracts、Experience、Host Gateway）
- `docs/context/`、`docs/model/`、`docs/provider/`、`docs/policy/`、`docs/tools/`、`docs/memory/`、`docs/artifacts/`、`docs/diagnostics/`、`docs/workspace/`、`docs/config/`、`docs/cli/`、`docs/hosting/` 各专项设计
- `docs/天枢最终验收案例.md`（端到端验收门槛）

不纳入深入审计：`docs/tianshu-implementation-tracker.md`（仅作进度参照）、`docs/reference/`（外部参考资料）。

---

## 2. 风险登记（被当成前提、但尚未验证的假设）

下列风险是本次验证要针对性击穿的对象。每条标注**严重度**与**对应验证命题**（见第 3 节）。

### R1. 架构重心与验收案例错位（严重）

验收案例 `docs/天枢最终验收案例.md` 真正考察的能力是：连续工具调用、子代理协作、**回合中途引导（在"下一次模型工具调用边界"插入）**、**中途中断**、**中断后继续/改向**、构建运行。

但架构文档对这些**几乎没有专项设计**：

- steering（tool-call 边界插入引导）——无文档定义该边界如何确定、消息如何进入运行中的 turn。
- interrupt 语义——仅在 `HostInteractionStep`（spec §10）一笔带过，未说明旧 turn 如何停止尾流、状态如何落终态。
- 子代理生命周期（spawn / send_input / wait / close）——仅作为"多 agent 分工策略"被列为可演化资产（spec §6），无 turn 内编排设计。
- turn loop 本身——user → model → tool → model → … 的反应式循环由谁拥有、是否由某个内置 StageGraph 自循环承载，未说清。

**风险**：文档密度集中在治理/IR/演化层，真正决定 Agent 成败的运行时循环欠设计。验收案例很可能卡在文档没覆盖的地方。
**对应命题**：A、退化基线（第 3 节）。

### R2. "可控演化内核"是最重、最未经验证的赌注（严重）

Adaptive Orchestration Layer + StageGraph 提案 + 评估 + 策略晋升/回滚（spec §6/§12、kernel 文档 §6/§13）是研究级目标。核心前提——"LLM 能稳定产出可验证的 StageGraph IR，且这些策略能被自动评估、晋升"——**目前没有任何业界 Agent（含参考的 Claude Code / Codex）这么做**。

**风险**：为一个尚未证明能 pay off 的能力，已建/规划了很重的 contracts / lifecycle / registry（`TianShu.Kernel.Strategies` 等），而真正干活的 agent loop 反而欠设计。kernel 文档 §16 已分阶段，但语气仍把"完整形态"当成必须面向的终态。
**对应命题**：C、D。

### R3. StageGraph IR 与 Agent 反应式本质的张力（严重）

真实 turn 高度反应式：下一个工具调用取决于上一个结果，无法预先 plan 出完整 graph。kernel 文档有 `SelectNextStageAsync`、`revise_stage_graph`，但关键边界没钉死：

- 若"一次模型调用 = 一个 Stage"，则 graph 是动态单步生成，重 IR 近乎仪式性开销。
- 若 Stage 是粗粒度（plan 级），则真正的逐工具循环发生在 Stage *内部*，此时 **per-tool 治理/校验落在哪一层**未定义。

"一个 model turn（含 N 次 tool call）"如何映射到 `StageGraph / Stage / RuntimeStep / Kernel 状态机（Created→…→Executing→Completed）`——这是整套设计的命门，目前无明确映射。
**对应命题**：A。

### R4. 每步 propose→validate→approve→materialize→execute 的开销（中）

spec §8 规定 Kernel ToolUse 必须走 `propose → validate → approve → materialize → execute`。若 turn 内每次 file read 都走 `request_capability_call → KernelOperation → validate → RuntimeStep → Execution Runtime`，对交互式编码 Agent 是不小的间接成本。

**未澄清**：这条链路是纯进程内同步校验（可接受），还是会引入额外模型往返（不可接受）？文档未区分"AI 提案产生的 operation"与"已 plan 好的批量 step"在延迟上的差异。
**对应命题**：A（映射示例需附带延迟分析）。

### R5. AppHost 长期是个 god host（中）

多份文档（planes §2、kernel §2.1、hosting、context-slicing §2、artifacts §2/§7）反复承认"AppHost 仍承担较多运行时装配职责""Kernel 语义必须迁出 AppHost"。这是被承认的技术债。

**风险**：迁移永远收不了尾，AppHost 一直是事实上的内核宿主。
**对应命题**：见第 4 节"硬性完成判据"建议。

### R6. 文档间不一致（低，但应尽快修正）

- **`GovernanceEnvelope` 字段数冲突**：spec §15.1 与 experience/host 链路是 6 字段版本；`docs/policy/tianshu-approval-policy-strategy-design.md` §3 是 9 字段版本（多 `ApprovalIds`/`AuditRecordIds`/`PolicyDecisions`）。spec 是 source-of-truth，需统一。
- **`TianShu.Contracts.Kernel` 状态矛盾**：`docs/architecture/tianshu-kernel-core-loop-design.md` §2.2 标"未来新建"，但 context-slicing / model-route / approval 三份文档已把它当"当前项目"使用。kernel 主文档应更新为"已新建"。

**对应命题**：第 5 节"一致性核查"。

---

## 3. 验证阶梯（Codex 必须按序执行）

核心方法论：**"这套自适应架构是否可行"不是单一命题，而是四个独立赌注叠加，且有依赖顺序。按"最便宜能证伪"的顺序逐个击穿；任一倒下，其上者不必再验。**

依赖关系：

```text
B (validator 关得住)   ← 安全门，最先做，确定性最高
A (IR 无损且不空转)    ← 地基，决定 StageGraph 这层要不要保留
退化基线 (跑通验收案例) ← 证明骨架承重 + 拿到度量尺
C (LLM 能稳定产合法图)  ← 自适应是真命题还是伪命题的关键
D (演化有正收益)       ← 被 A/B/C 前置门控，最后且最贵
```

---

### 命题 B：Kernel Validator 真的 fail-closed

**断言**：spec §10、kernel §10、policy 文档全部依赖"Stable Kernel Core 是不可绕过的沙箱"。这个安全前提**当前就能验，且不需要 AI**。

**最小实验**：
1. 构造对抗性 StageGraph / KernelOperation / RuntimeStep 语料，至少覆盖：
   - 不可达入口、无终态、无循环上限的 graph（kernel §7.3 要求 fail closed）。
   - `allowedToolIds` 超出 `GovernanceEnvelope` 的 tool。
   - 副作用等级超过 envelope `MaxSideEffectLevel` 的 step。
   - 预算溢出（token / time / cost / retry / tool-call）。
   - 缺 `SourceGraphId`/`SourceStageId`/`SourceKernelOperationId`/`PermissionEnvelope` 的 step（execution §4 要求拒绝）。
   - `SideEffectLevel.Unspecified` 的 step 与 envelope（module §3 要求拒绝）。
   - human-gate 缺 `ApprovalIds` 仍尝试执行的 step（policy §4）。
2. 用 property-based / 模糊测试批量灌入 `IKernelValidator` 的 5 个验证点（kernel §15.7：intent / proposal / graph / operation / step）。
3. 同时验证 Execution Runtime 各 bridge 入口（provider / tool / memory / artifact / diagnostics / workspace）**都复用**同一 envelope 校验（policy §4 要求）。

**判死线（任一命中即 B 不通过）**：
- 任一类越界对象能通过 validator 进入下一阶段。
- 存在某个 Execution Runtime bridge 绕过了 envelope 校验。
- 验证失败时未 fail closed（静默降级为不受控执行）。

**B 不通过的结论**：沙箱假设破裂，**自适应层一行都不能上线**；必须先把 validator 补到全绿，否则 C、D 无意义。

---

### 命题 A：一次真实 turn 能被 StageGraph + RuntimeStep 无损表达，且 IR 不沦为仪式

**断言**：整套设计假设真实 agent 工作能映射到 StageGraph IR。这是地基；A 倒则上面全是空中楼阁。

**最小实验**（纸面 + 薄原型，不碰 AI、不碰演化）：
1. 取验收案例里的**一次真实 turn**：用户输入 → 模型调用 → N 次 tool call（含至少一次子代理 spawn）→ 模型收尾。
2. 手工逐步翻译成实例图：`CoreIntent → StageGraph → Stage → KernelOperation → RuntimeStep → Kernel 状态机迁移`。
3. 明确回答：
   - 一次"模型调用 + 它触发的 N 个工具"映射成**几个 Stage、几个 RuntimeStep**？
   - **per-tool 校验发生在哪一层**？（若在 Stage 内部循环，则不在 StageGraph 层，需指明替代校验点。）
   - steering（tool-call 边界插入）、interrupt、resume 三个动作分别落在状态机的**哪个迁移**上？（直接对应 R1）
   - 子代理 spawn/wait/close 在 IR 里是 Stage、是 RuntimeStep、还是嵌套 KernelRun？
4. 附带 R4 延迟分析：标出这条链路里**哪些步骤是纯进程内同步、哪些会触发额外模型往返**。

**判死线**：
- 若结论是"一次模型调用就是一个 Stage、graph 只能边跑边长一个节点"——则 StageGraph 这层 IR 基本空转。**结论：砍掉 graph 层，只保留 RuntimeStep + 一个反应式 turn 解释器。**（注意：这不是失败，是省掉一整层的重大收获，应明确记录。）
- 若无法把 steering / interrupt / resume / 子代理映射进现有状态机——则 R1 坐实，**必须先补一份与 Kernel 同等分量的"运行时循环专项设计"**，再谈 IR。

**产物**：一份端到端实例映射图 + 上述四问的书面回答 + 延迟分析。

---

### 退化基线：先建一个完全不自适应的版本，跑通验收案例

**这是贯穿全程的度量尺，必须在 C、D 之前完成。**

**构建要求**：
- 内置固定 StageGraph（或按命题 A 结论：干脆无 graph，只一个反应式 turn 解释器）。
- AI 只在固定 stage 内**选工具**，不产任何 `KernelProposal`。
- 演化闭环（draft→trial→promoted→rolled_back）**全部旁路**。

**目标**：这个退化版必须先能跑通 `docs/天枢最终验收案例.md`。

**一旦跑通，同时拿到两样东西**：
1. 证明六层骨架（契约、治理、provider 物化、turn loop）是**承重的**。
2. 一把 **measuring stick**——之后任何自适应能力，都必须证明自己比这个 baseline 更好（更省 token / 更准 / 更稳），否则即为负收益，不该晋升。

**判死线**：
- 若退化基线都跑不通验收案例——则问题不在"自适应"，而在骨架本身，**自适应能力一律暂停投入**，先修骨架。

---

### 命题 C：LLM 能稳定产出*合法*的 StageGraph（或 patch）

**断言**：这是整个"自适应"叙事成立与否的核心，也是真正的未知数。

**最小实验**（隔离实验，只验"模型 + schema + validator"三者）：
1. 搭 stub kernel：只要 `KernelTool schema`（spec §8 的 `compose_stage_graph` / `revise_stage_graph`）+ `IKernelValidator`。**不要 Execution Runtime、不要任何 Module、不产生真实副作用。**
2. 准备一批代表性任务（覆盖验收案例量级）。
3. 让模型走 `compose_stage_graph` / `revise_stage_graph`，过 validator，统计：
   - **首次产图合法率**。
   - **revise 修正循环收敛率**：给它 validator 的 rejection reason，它能否改对、几轮收敛。
   - **跨模型差异**：Opus / Sonnet / 小模型分别的合法率。
   - **修正震荡**：是否出现"改一个错冒出另一个错"的不收敛。

**判死线**：
- 首次合法率低**且**修正循环不收敛——则 **Adaptive Layer 不可行**。结论：**永远用内置固定 graph，AI 只在 graph 内部选工具，不产 graph**（即把退化基线固化为终态，关闭 §6 的"演化编排资产"中关于 graph 生成的部分）。

**这一步是回答"自适应到底是不是真命题"性价比最高的实验**，因为它完全不依赖真实执行链路。

---

### 命题 D：策略演化真的有正收益（promotion 优于 baseline）

**断言**：spec §12、kernel §13 的策略生命周期假设"能自动判定策略 A 优于 B 并晋升"。这是最贵、最后才能验的命题，且有一个常被低估的死结。

**前置门控**：D **不可能先验**，必须 A / B / C 全绿、且退化基线已就绪（作为 baseline）才能开始。

**死结（必须先回答，否则 D 无法验）**：
- LLM 回合间方差很大。若"策略带来的 delta < LLM 噪声方差"，则晋升/回滚就是在噪声里抛硬币。
- promotion 前提是 **replay + 确定性 evaluation**（kernel §12 "没 trace 不许晋升"）。但需回答：trace 在 LLM 非确定性下能否 replay 到**可比较**？

**最小实验**：
1. 先证明你有"**可打分任务套件 + 稳定打分 + delta > 方差**"这三样。拿不出 → D 直接判负。
2. 在三样齐备的前提下，对同一任务跑 baseline 策略 vs 候选策略，统计 success / cost / latency / recovery rate，做显著性检验。

**判死线**：
- 拿不出"任务套件 + 稳定打分 + delta > 方差"——则策略生命周期是**装饰性**的，**自动晋升直接关掉，只保留人工固化策略**。
- 候选策略 delta 在噪声范围内不可检出——同上，关闭自动晋升。

---

## 4. 针对 R5（AppHost god host）的硬性完成判据建议

不属于"可行性"验证，但属于本次审计发现，建议 Codex 在 tracker 中为"AppHost 去内核化"设硬判据，否则迁移会一直 80% 卡着：

- **架构依赖测试**：禁止 `TianShu.AppHost*` 引用任何 `TianShu.Kernel*` 实现类型（`IStableKernelCore`、`IAdaptiveOrchestrator`、`IStageGraphInterpreter`、`IKernelValidator`、`KernelRunStateMachine`、`StageGraphInterpreter` 等）。artifacts 文档 §7 已对 `AppHost.State` 列了禁止清单，应推广为全 AppHost 的依赖锁。
- 判据为二元：依赖测试通过 = 去内核化完成；否则未完成。不接受"基本迁完"。

---

## 5. 文档一致性核查（对应 R6）

请 Codex 核查并修正（不改架构结论，只消歧义）：

1. 统一 `GovernanceEnvelope` 字段定义：以 spec §15.1 还是 policy §3 为准，二选一并同步另一处。
2. 更新 `docs/architecture/tianshu-kernel-core-loop-design.md` §2.2 中 `TianShu.Contracts.Kernel` 的状态（从"未来新建"改为与其它专项文档一致的"已新建"）。
3. 校验：所有专项文档引用的 contract 项目"已建/未建"标注与代码现状一致（以解决方案实际项目为准）。

---

## 6. 审计结论摘要

- 这套架构的**边界纪律和契约化程度高**，治理/契约层已过设计；**运行时 agent loop 欠设计**（R1/R3）。
- "可控演化内核"是最重、最未验证的赌注（R2）；其可行性完全取决于命题 C，而 C 尚未做过隔离实验。
- 正确的验证顺序是 **B → A → 退化基线 → C → D**，按"最便宜能证伪"推进，任一前置倒下即停。
- 最该立刻动手的是 **命题 C 的 stub-kernel 产图实验**与**命题 B 的 validator 对抗测试**：前者回答"自适应是不是真命题"，后者是所有让 AI 参与的安全门。两者都不依赖完整执行链路，性价比最高。
- 警惕反模式：**别让"收敛 contracts"跑在"证伪核心命题"前面**。验证 spike 应是脏的、一次性的，放在正式分层之外。

---

## 附录：判死线汇总表

| 命题 | 一句话断言 | 判死线 | 死后结论 |
| --- | --- | --- | --- |
| B | Validator 关得住沙箱 | 任一越界对象通过 / 任一 bridge 绕过校验 / 未 fail closed | 自适应层不得上线，先补 validator |
| A | 真实 turn 能无损映射且 IR 不空转 | "一次模型调用=一个动态单步 Stage" | 砍掉 graph 层，保留 RuntimeStep + 反应式解释器 |
| A | steering/interrupt/resume/子代理可入状态机 | 无法映射 | 先补运行时循环专项设计 |
| 退化基线 | 不自适应版能跑通验收案例 | 跑不通 | 问题在骨架，暂停一切自适应投入 |
| C | LLM 能稳定产合法 graph | 首次合法率低且修正不收敛 | 永远用内置固定 graph，AI 不产图 |
| D | 策略演化有正收益 | 拿不出"任务套件+稳定打分+delta>方差" | 关闭自动晋升，只保留人工固化策略 |
