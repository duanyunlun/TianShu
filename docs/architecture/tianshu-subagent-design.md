# TianShu 多 Agent / Sub-Agent 协同设计

## 1. 文档定位

本文是 TianShu **多 Agent / Sub-Agent 协同**能力的专项设计基线，用于细化 `docs/tianshu-architecture-spec.md` 的六层职责、可控演化内核与 ToolUse 模型在「一个父 turn 通过受治理请求派生子 agent」场景下的落地形态。

本文只描述当前正式设计、已落地串行基线和 v0.9 P29 目标形态，不记录历史方案、讨论过程或临时实现。它不替代主架构规范、`tianshu-kernel-core-loop-design.md`、`tianshu-builtin-stage-graph-design.md` 或 `tianshu-execution-runtime-design.md`，而是这些基线在「多 Agent」维度上的具体实例。

核心命题：**一个 sub-agent = 一次被治理收窄、结构有界、上下文隔离的嵌套 `CoreIntent(Turn)` run；一棵 multi-agent tree = 由父 turn 受治理派生、可 fanout、可 fan-in、可复盘的一组嵌套 run。** 多 Agent 不在稳定内核上新增第二套编排机制，而是把内核已有的 turn 能力递归地、受治理地组织成一棵有界执行树。

## 2. 设计原则

1. **认知独立，授权受限。** 子 agent 在「上下文、threadId、StageGraph、Model-Route、失败域」上完全独立；但在「权限上限、spawn 结构、副作用边界」上必须从父继承并收窄。独立的是*怎么想*，受约束的是*能动多大*。
2. **复用而非新建。** 子 agent 执行复用 `graph.turn.default`、`StableKernelCore`、`AdaptiveRuntimeExecutionLoop` 的完整反应式 loop，不为多 Agent 引入第二套编排路径或第二套验证器。
3. **结构有界优先于成本有界。** 防 fork 炸弹靠**离散结构闸门**（spawn 深度 / 扇出 / 树总节点数），在 spawn 那一刻可静态判定、超限 fail-closed。token / time 预算不作为 spawn admission 的结构闸；子 run 进入执行后仍受自身 `KernelBudget`、Runtime timeout、provider retry 和工具回边预算约束。
4. **权限单向收窄。** 子 `GovernanceEnvelope` 必须是父 envelope 的子集（allow-list、module、副作用上限、human gate 只能更严不能更宽）。这是主架构「副作用层层收窄」不变量在多 Agent 维度上的必然推论。
5. **Orchestrator–Worker，父级 fanout / fan-in。** 父 run 可以按治理允许的结构闸门派生多个子 run；子只对父负责，结果只能回流父级 fan-in 节点。子 agent 之间不互相通信、不辩论、不共享状态。peer/debate 范式明确排除。
6. **失败隔离和整树复盘。** 每个子 run 都是独立失败域：子失败必须投影为结构化 `SubAgentRunResult`，不得污染父 run 状态机。父 trace 中的 spawn/fanout/fan-in step 引用子 run 的 runId、diagnostics 和 replay summary，整棵 agent 树必须可从根重建。

## 3. 适用范围

| 项 | 本版处理 |
| --- | --- |
| 父 turn 通过 `spawn_agent` 串行派生子 agent | **已落地基线**：模型在治理允许时可请求 `spawn_agent`，系统将其物化为子 `CoreIntent(Turn)` → 同步执行到终态 → 结果回流。该能力已实现，不等同于 live 观察中已经发生模型自主触发。 |
| 父 turn 通过 `spawn_agent` 并行 fanout（多子并发） | **P29 主线**：并行 fanout 是 v0.9 的正式目标，但默认关闭；只有配置和治理同时启用并发闸门时才允许多子并发执行。 |
| fan-in 结果归并 | **P29 主线**：父级必须等待或收敛一组子 run 的终态，生成结构化汇总、冲突标记、失败列表和证据引用；模型裁判只能作为辅助，不得成为唯一判断标准。 |
| 子 agent 再 spawn 孙 agent | **受深度上限约束**：默认 `maxSpawnDepth = 1`（父→子，子不可再 spawn）；后续若配置放宽到 `2` 或以上，仍必须受全树预算、fanout 和并发闸门约束，超限 fail-closed。 |
| 子 agent 之间通信 / 辩论 / 共享上下文 | **明确不支持**：peer/debate 反模式，与可复盘、fail-closed 哲学冲突。 |
| 宿主/人工管理的 agent job / roster | **不在本文范围**：由 `ControlPlane.Workflows` / `ControlPlane.Agents` 承担（见 tracker 条目 34），与模型自主 sub-agent 是两条独立路径。 |

> 与条目 31.8 / 34.4 的关系：agent job / roster 仍属于人工或宿主管理 runtime surface，不进入 provider-directed tool surface。模型可请求的 sub-agent 能力由本文单独定义：`spawn_agent` 只在 governance 显式授予时开放，未授予时继续 fail-closed；执行不复用旧 AppHost agent job 工具。当前 live 观察结论为 `SPAWN_OBSERVED=0/27`，只能说明机制已就绪但当前非诱导任务下模型未主动使用该能力。

## 4. 涉及项目与代码归属

| 项目 | 状态 | 归属内容 |
| --- | --- | --- |
| `src/Contracts/TianShu.Contracts.Modules` | 现有 | 新增 `ModuleKind.SubAgentOrchestration`（或等价命名的 Agent orchestration module kind）与 `BuiltInModuleDescriptors.SubAgent` 模块身份声明。 |
| `src/Contracts/TianShu.Contracts.Agents` | 现有 + P29 扩展 | 承载 sub-agent 公共契约：`SubAgentSpawnRequest`、`SubAgentRunResult`、`SubAgentLineage`、`SubAgentSpawnQuota`、`SubAgentFanoutPolicy`、`SubAgentTreeBudget`、`SubAgentBudgetSplit`、`SubAgentFailure`。P29 继续新增或扩展 fanout request、fanout item、fan-in summary、tree diagnostics、failure isolation projection。 |
| `src/Contracts/TianShu.Contracts.Kernel` | 现有 | 子 `CoreIntent(Turn)` 的派生构造规则（envelope 收窄、subject 派生、lineage metadata）落在 Kernel 契约层；不新增 IntentKind。 |
| `src/Core/TianShu.SubAgent` | 现有 | `ISubAgentModule` 默认实现 `SubAgentOrchestrationModule`：接收 spawn 请求 → 派生收窄子 intent → 调用 `IKernelRuntimeExecutionLoop.RunReactiveAsync` → 投影 `SubAgentRunResult`。 |
| `src/Core/TianShu.RuntimeComposition` | 现有 + P29 扩展 | `AdaptiveRuntimeExecutionLoop` 中 `tool-exec` 物化 spawn 请求为 `ModuleCapabilityStep`；`SubAgentSpawnLedger` 维护树级 lineage 与结构闸门计数。P29 扩展 fanout 调度器、fan-in 汇总器、子树预算 ledger、取消/超时传播和整树 diagnostics。 |
| `src/Execution/TianShu.Execution.Runtime` | 现有 | 新增或扩展 module bridge（如 `ExecutionRuntimeSubAgentModuleBridge` 或通用 `ExecutionRuntimeModuleBridge`）路由 `ModuleCapabilityStep(sub_agent.spawn)` 到 `ISubAgentModule`，复用既有 module bridge governance 校验。 |
| `tests/TianShu.SubAgent.Tests`、`tests/TianShu.Execution.Runtime.Tests` | 现有 + P29 扩展 | envelope 收窄、结构闸门 fail-closed、结果回流、lineage 复盘的验收测试；P29 补并发限制、预算耗尽、子任务失败、取消传播、结果归并、fork bomb 防护。 |

归属规则：
- sub-agent 公共数据结构与 `ISubAgentModule` 接口默认进入 `TianShu.Contracts.Agents`，避免 Execution Runtime 为调用模块接口而反向依赖 `TianShu.SubAgent` 实现项目。
- sub-agent 编排实现进入 `TianShu.SubAgent`，作为一个 **Module**，不进入 Kernel；它**消费** `IKernelRuntimeExecutionLoop`，不拥有 Kernel 编排权。
- 结构闸门计数器（lineage ledger）归 RuntimeComposition，不归 Kernel——它是组合层的运行时记账，和 turn 状态机同层。

## 5. 执行入口：spawn 物化为 ModuleCapabilityStep

sub-agent 的执行入口**分型为 `ModuleCapabilityStep`**，而不是 `ToolInvocationStep`，也不新增 step 类型。理由：spawn 不是「调一个工具拿一个值」，而是「启动一次嵌套 Kernel run」——这是模块能力语义，对应主架构 §10 的 `ModuleCapabilityStep`（"调用非工具形态的模块能力"）。这也避免侵入 `RuntimeStep` 核心分型与 `KernelValidator`/`Interpreter`/`Replay` 全链路。

模型在 `model-reason` 输出的 `spawn_agent` tool request，在 `tool-exec` 阶段被 RuntimeComposition 物化为：

```text
model-reason
  └ toolRequest { toolId: "spawn_agent", operation: "spawn", input: {...} }
stage.tool-exec
  └ ModuleCapabilityStep(moduleId="module.sub_agent", capabilityId="sub_agent.spawn")
        └ ExecutionRuntimeSubAgentModuleBridge（或通用 bridge 的 sub-agent 分支）→ ISubAgentModule.SpawnAsync
             ├ 派生收窄子 CoreIntent(Turn)
             ├ IKernelRuntimeExecutionLoop.RunReactiveAsync(子 intent)   ← 子跑完整 graph.turn.default
             └ 投影 SubAgentRunResult
        └ 子 run 终态 → toolResults[] 回流父下一轮 model-reason
```

`graph.turn.default` 的 `tool-exec` allow-list 增加模型可见的 `spawn_agent` 请求入口（仅在 governance 显式授予多 Agent 能力时）。该入口不是普通 tool bridge 执行，而是在 RuntimeComposition 中被改写为 `ModuleCapabilityStep(module.sub_agent / sub_agent.spawn)`；因此同一次治理还必须允许 `module.sub_agent`，且副作用等级声明为 `HostMutation`（spawn 子 run 是一次受治理的主机侧编排动作）。当前产品面只开放同步 `spawn`；P29 并行 fanout 仍复用多个同步 child run 的受控调度，不开放交互式 `wait` / `send_input` / `close_agent` 工具面。

CLI 当前只在 `send --kernel-runtime-loop --enable-subagents --approve-all` 组合下为真实 provider 暴露 `spawn_agent`，并注入 `module.sub_agent` module binding。未显式传入 `--enable-subagents` 时，即使底层 sub-agent 模块存在，也不得进入 provider tool surface；未传入 `--approve-all` 时必须 fail-closed，因为本版把 spawn 子 run 视为 `HostMutation` 级主机侧编排动作。

## 6. 子 CoreIntent 的派生规则（收窄不变量）

`ISubAgentModule.SpawnAsync` 从父 run context 派生子 `TurnIntent`。派生必须满足下列硬规则，全部由 Kernel validator 与 SubAgent module 双重校验，任一不满足 fail-closed：

### 6.1 Subject 派生（认知隔离）

```text
子 KernelSubjectRef:
  SessionId  = 父 SessionId            （同一会话域）
  ThreadId   = 新生成的子 threadId       （独立上下文，不继承父历史）
  WorkflowId = 父 WorkflowId            （同一工作流，用于聚合）
  TurnId     = 新生成的子 turnId
```

子 thread 是独立的上下文窗口；父 run 的完整对话历史**不**自动注入子 run。父只通过 `SubAgentSpawnRequest.taskBrief`（结构化任务说明）+ 显式传入的证据引用，把子任务"良界定"地交给子 agent。

### 6.2 Governance 收窄（授权受限）

子 `GovernanceEnvelope` 必须是父的子集：

| 字段 | 收窄规则 |
| --- | --- |
| `AllowedToolIds` | 子 ⊆ 父；默认 `maxSpawnDepth = 1` 时子无条件移除 `spawn_agent`（见下方说明）。 |
| `AllowedModuleIds` | 子 ⊆ 父。 |
| `MaxSideEffectLevel` | 子 ≤ 父（枚举值不高于父）。 |
| `RequiresHumanGate` | 子为 `父.RequiresHumanGate || spawnRequest.requiresHumanGate`（只能更严）。 |
| `PolicyIds` | 子 ⊆ 父。 |
| `ApprovalIds` | 默认继承父已有审批引用全集；若 spawn request 显式声明 `RequestedGovernance.ApprovalIds`，则只允许缩窄为 `父 ∩ requested`，子不能凭空获得新审批（审批继承语义见 §6.3）。 |

**关于 `spawn_agent` 的移除**：默认配置 `maxSpawnDepth = 1`（见 §7），子 agent 的 depth 已达上限、本就不允许再 spawn，因此子 envelope **无条件移除** `spawn_agent`。若后续显式把 `maxSpawnDepth` 放宽到 ≥ 2，则只能在 child lineage 的剩余深度仍允许时保留 `spawn_agent`，并且仍必须受全树 fanout、tree nodes、concurrency、budget 和 governance 闸门约束。

Kernel validator 复用既有的 `ValidateGraphPolicyAgainstGovernance` / `ValidateRuntimeStepAsync` 收窄逻辑——子 run 的图与 step 本来就会被验证不超过子 envelope，因此只要"子 envelope ⊆ 父 envelope"成立，整条收窄链自动闭合。

### 6.3 审批继承语义（用户审批覆盖整棵受治理子树）

子 run 默认继承父 `GovernanceEnvelope.ApprovalIds` 全集，意味着：**用户对父 turn 的一次人工审批，其授权范围覆盖结构闸门（§7）内的整棵子树。** 子 agent 的高风险操作复用父已获得的 approval，不会为每个子 run 的每次副作用重新弹出 human gate。

如果 spawn request 显式声明 `RequestedGovernance.ApprovalIds`，该声明只能作为**主动缩窄**：最终子 approval 为 `父 ApprovalIds ∩ RequestedGovernance.ApprovalIds`。这允许父/模型为某个子任务主动放弃部分父 approval，但不能新增 approval，也不能扩大已审批副作用范围。若 request 未声明 approval，则使用父 approval 全集。

这是一个**有意识的、必须被显式声明**的安全决定，不是被默认掉的假设：

- **为什么这样选**：若 approval 不可继承（每个子 run 高风险操作单独再 gate），多 Agent 在交互上不可用——子 agent 每写一个文件都打断用户。因此审批以「任务」为粒度，而非「单次副作用」为粒度。
- **安全边界来自哪里**：审批可继承之所以安全，前提是子树被两道硬约束框死——子 envelope 单向收窄（§6.2，子能动的副作用 ≤ 父）+ 结构闸门有界（§7，子树的形状封死）。**用户批准父时，他授权的不是"父这一个 agent"，而是"这棵权限不超过父、形状不超过结构闸门的子树"。** 子树无法获得父没有的权限，也无法长成无界的规模，所以"批准父 = 批准子树"在这两道约束下是可控的。
- **必须保留的不变量**：子继承的 approval 严格 ⊆ 父的 `ApprovalIds`；默认场景为父 approval 全集，显式 request 场景为父 approval 子集。子**不能**凭空产生新 approval，也不能用继承的 approval 去覆盖一个父 envelope 本就不允许的副作用等级。审批继承只放宽"是否需要重新询问用户"，不放宽"能做什么"。

### 6.4 Budget 传递（不把 token/time 当作结构闸）

```text
子 KernelBudget:
  TokenBudget    = 从父预算显式切分或由配置给出子预算；不参与 spawn admission 的结构判定
  TimeBudgetMs   = 从父预算显式切分或由配置给出子预算；不参与 spawn admission 的结构判定
  ToolCallBudget = 子 run 自身的回边闸门（防子 run 内部工具循环失控，与父独立）
  RetryBudget    = 透传
```

token/time 不参与 `spawn_agent` 是否允许物化的 fail-closed 判定；结构安全只能由 §7 的离散闸门保证。子 run 一旦被允许启动，仍必须按自身预算、provider timeout、Runtime cancellation 和工具调用预算执行，不能因为“token 不作结构闸”而绕过执行层预算治理。`ToolCallBudget` 仍保留——它约束的是「单个子 run 内部 model⇄tool 回边次数」，与「整棵树的 spawn 繁殖」是两件正交的事，前者防单 run 失控，后者防树失控（见 §7）。

## 7. 结构闸门：防 fork 炸弹（核心安全机制）

agent 树的安全来自约束它的**形状**，不是能耗。所有 admission 都必须执行前三个离散、可静态判定、超限 fail-closed 的结构闸门；P29 并发启用时额外启用 `maxConcurrentAgents`：

| 闸门 | 含义 | 计数性质 | 默认值 | fail-closed code |
| --- | --- | --- | --- | --- |
| `maxSpawnDepth` | 从根 turn 算起的最大嵌套层数 | 血缘链上的位置（非计数器） | `1`（父→子，子不可再 spawn） | `subagent.spawn_depth_exceeded` |
| `maxFanoutPerAgent` | 单个 agent 一生可 spawn 的直接子数上限 | **单调累计**（按 agent，只增不减） | `8` | `subagent.fanout_exceeded` |
| `maxTreeNodes` | 整棵 agent 树历史上创建过的 agent 总数上限（含根） | **单调累计**（按树，只增不减） | `32` | `subagent.tree_node_budget_exhausted` |
| `maxConcurrentAgents` | 整棵树同时活跃的子 agent 数上限（仅在 `> 0` 时启用；当前串行基线设为 `0`/不启用） | **可回收**（按树，随终止递减；仅启用时记账） | `0`（默认关闭并发）／`4`（P29 显式启用时建议默认） | `subagent.concurrency_exceeded` |

**关键区分——单调累计 vs 可回收，两者语义不同、不可共用一个计数器：**

- `maxFanoutPerAgent` / `maxTreeNodes` 是**单调累计**：它们记录"历史上创建过多少"，**只增不减**。一个子 run 终止后，"它曾被创建过"这个事实不能回收——否则"终止一个、再 spawn 一个"就能无限循环绕过总量上限。这两个闸门防的是**累计繁殖失控**。
- `maxConcurrentAgents` 是**可回收**：它记录"此刻同时运行的子 agent 数量"，随子 agent 终止递减。它防的是**瞬时并发过载**，与累计无关。当前串行执行（spawn 后同步跑到终态再回流）完全不启用该闸门：admission 路径既不检查它、也不递增 `ActiveAgents`。P29 显式启用并发时，bounded scheduler 必须在启动 child run 时递增，并在 child terminal/cancel/timeout 后成对回收。

这些量由 RuntimeComposition 的 `SubAgentSpawnLedger` 跨整棵树维护，并随 `SubAgentLineage` 透传到每个子 run。每次 spawn 物化前检查：

```text
spawn 物化前（在 ModuleCapabilityStep 生成前）：
  if lineage.Depth + 1 > maxSpawnDepth                       → fail-closed (spawn_depth_exceeded)
  if ledger.CumulativeFanoutOf(lineage.CurrentRunId) + 1 > maxFanoutPerAgent
                                                            → fail-closed (fanout_exceeded)
  if ledger.CumulativeTreeNodes + 1 > maxTreeNodes          → fail-closed (tree_node_budget_exhausted)

  // 并发闸门仅在显式启用时检查；MaxConcurrentAgents = 0 表示「未启用」（当前串行基线即如此）。
  // 这样可避免：未启用并发时 ActiveAgents(0)+1 > 0 误拒第一个子 agent。
  if maxConcurrentAgents > 0 and ledger.ActiveAgents + 1 > maxConcurrentAgents
                                                            → fail-closed (concurrency_exceeded)

  else → 允许 spawn：
           CumulativeTreeNodes += 1（永不回收）
           CumulativeFanoutOf(caller) += 1（永不回收）
           if maxConcurrentAgents > 0: ActiveAgents += 1（终止时回收）   // 未启用并发时不碰 ActiveAgents
```

启用规则：`ActiveAgents` 只有在 `MaxConcurrentAgents > 0`（即显式启用并发记账）时才递增；递增了就必须由 `OnChildTerminated` 回收。当前串行基线 `MaxConcurrentAgents = 0`，admission 不递增 `ActiveAgents`，因此**不存在「串行基线 `ActiveAgents` 只增不减导致假性并发超限」的问题**，也不依赖串行基线调用 `OnChildTerminated`。负数不是"关闭并发"的等价写法，而是非法配额，必须由契约构造或配置校验 fail-closed。

子 run 终止时只回收 `ActiveAgents`（仅当并发启用时），**绝不回收** `CumulativeTreeNodes` 或 `CumulativeFanoutOf`。

`SubAgentLineage` 是不可变的传递链：父把自己的 `lineage`（含 depth、root runId、current runId、parent runId、ledger 引用）注入子 spawn 请求；ledger admission 通过后生成 child lineage。child run id 必须由 `ledgerRef + rootRunId + parent currentRunId + siblingIndex` 稳定派生，而不是随机生成；这样同一个父 run、同一个 spawn 序列在 replay 时可重新得到相同的子 run id。若父 run id 本身由宿主随机生成，则该随机性属于根 run 的既定事实；sub-agent 层只保证在同一根 run 事实下确定派生。子若再 spawn（depth 允许时），用自己的 lineage 继续派生下一层。**深度上限是砍断指数爆炸的主闸**；累计扇出和累计总节点数是宽度与绝对兜底；并发活跃数是 P29 显式并发的瞬时过载保护。

## 8. 完整落地接口基线

接口命名是设计基线，不要求实现时逐字使用同一 namespace；但职责、输入输出和边界不得弱化。

### 8.1 Sub-agent 公共契约

归属：`src/Contracts/TianShu.Contracts.Agents`。

```csharp
/// <summary>父 turn 发起的一次受治理 sub-agent spawn 请求。</summary>
/// <summary>A governed sub-agent spawn request issued by a parent turn.</summary>
public sealed record SubAgentSpawnRequest(
    string SpawnCallId,                      // 对应模型 toolRequest 的 callId
    SubAgentLineage ParentLineage,           // 父的血缘链（含 current run / depth / root / ledger 引用）
    KernelSubjectRef ParentSubject,          // 父 subject；用于派生同 Session/Workflow、隔离 Thread/Turn 的子 subject
    string TaskBrief,                        // 良界定的子任务说明（结构化文本引用）
    IReadOnlyList<string> EvidenceRefs,      // 父显式传给子的证据引用（不传完整历史）
    GovernanceEnvelope RequestedGovernance,  // 父请求的子治理（必须 ⊆ 父，由 module 再收窄校验）
    KernelBudget RequestedBudget,            // 透传/放大的预算（token 不作结构闸）
    bool RequiresHumanGate,                  // 父可强制要求子更严的 human gate
    MetadataBag Metadata);
    // 注意：子血缘（ChildLineage）不在本请求内。
    // SubAgentSpawnRequest 只表达模型请求与父上下文；ChildLineage 是 ledger admission 的决策产物，
    // 经 ModuleCapabilityStep.Metadata 传递，并由 sub-agent module bridge 解析后作为 SpawnAsync 独立参数传入。

/// <summary>不可变血缘链；spawn 结构闸门的判定依据。</summary>
/// <summary>Immutable lineage chain; the basis for spawn structural-gate decisions.</summary>
public sealed record SubAgentLineage(
    string RootRunId,
    string CurrentRunId,
    string? ParentRunId,
    int Depth,                               // 根为 0；每 spawn 一层 +1
    int SiblingIndex,                        // 本 agent 在父的子序列中的序号
    string LedgerRef)                        // 指向树级 SubAgentSpawnLedger
{
    public SubAgentLineage Descend(string childRunId, int siblingIndex)
        => this with { ParentRunId = CurrentRunId, CurrentRunId = childRunId, Depth = Depth + 1, SiblingIndex = siblingIndex };
}

/// <summary>本次 spawn 适用的结构配额；全树共享同一组上限。</summary>
public sealed record SubAgentSpawnQuota(
    int MaxSpawnDepth,          // 血缘链最大深度
    int MaxFanoutPerAgent,      // 单 agent 累计直接子数上限（单调）
    int MaxTreeNodes,           // 整棵树历史累计 agent 总数上限（单调）
    int MaxConcurrentAgents);   // 整棵树同时活跃子 agent 数上限（可回收）；0 表示不启用并发记账，当前串行基线设为 0；负数非法

/// <summary>子 run 终态投影，作为 toolResult 回流父 turn 的证据。</summary>
public sealed record SubAgentRunResult(
    string SpawnCallId,
    string ChildRunId,
    string ChildThreadId,
    SubAgentRunStatus Status,                // Completed / Failed / Blocked / Cancelled
    string? ResultText,                      // 子 agent 最终回复（回流父的核心证据）
    string? ReplaySummaryRef,                // 子 run 的 replay 引用，供整树复盘
    IReadOnlyList<string> DiagnosticsRefs,
    SubAgentFailure? Failure);

public enum SubAgentRunStatus
{
    Unspecified = 0,
    Completed = 1,
    Failed = 2,
    Blocked = 3,        // 结构闸门或治理拒绝
    Cancelled = 4,
}

public sealed record SubAgentFailure(
    string Code,                             // 如 subagent.spawn_depth_exceeded
    string Message);
```

### 8.2 Sub-agent Module 接口

归属：接口位于 `src/Contracts/TianShu.Contracts.Agents`，默认实现位于 `src/Core/TianShu.SubAgent`；模块身份经 `BuiltInModuleDescriptors.SubAgent` 声明，模块类型使用 `ModuleKind.SubAgentOrchestration` 或等价的 Agent orchestration module kind。Execution Runtime 只引用 Contracts 层接口，并把 `ExecutionRuntimeContext` 投影成 `SubAgentModuleInvocationContext` 传入，避免 Execution Runtime 与默认实现项目形成循环依赖。`SubAgentModuleInvocationContext` 必须携带父 `GovernanceEnvelope`，因为默认实现需要用它证明子 envelope 是父 envelope 的子集。`module.sub_agent` 是高风险编排能力：若 Execution Runtime 没有真实 `ISubAgentModule` 绑定，必须以 `subagent_module_not_bound` fail-closed，不得落到通用 module placeholder 成功路径。

```csharp
public interface ISubAgentModule : IModuleHealthCheck
{
    /// <summary>
    /// 派生收窄子 CoreIntent(Turn)、执行嵌套反应式 loop、投影子 run 终态。
    /// Derives a narrowed child CoreIntent(Turn), runs the nested reactive loop, and projects the child terminal result.
    /// 结构闸门 admission 与计数递增由 RuntimeComposition/SubAgentSpawnLedger 在物化 ModuleCapabilityStep 前完成；
    /// admission 通过后生成的 childLineage 作为独立参数传入本方法。
    /// childLineage 不在 request 内；ExecutionRuntimeSubAgentModuleBridge 必须从 ModuleCapabilityStep.Metadata 解析它。
    /// 本模块用 childLineage 派生子 intent；若 childLineage 缺失、越界或与 request.ParentLineage 不匹配，返回 Blocked。
    /// 本模块不得再次调用 admission 或递增任何单调计数，避免双重记账。
    /// </summary>
    ValueTask<SubAgentRunResult> SpawnAsync(
        SubAgentSpawnRequest request,
        SubAgentLineage childLineage,        // 由 ledger admission 生成；admission 之后才存在
        SubAgentSpawnQuota quota,
        SubAgentModuleInvocationContext context,
        CancellationToken cancellationToken);
}
```

实现约束：
- `SpawnAsync` **只消费** `IKernelRuntimeExecutionLoop`，不直接构造 `StableKernelCore` / `StageGraph` / `RuntimeStep`。
- 子 intent 的 `GovernanceEnvelope` 必须经 `SubAgentGovernanceNarrowing.Narrow(parentEnvelope, request.RequestedGovernance)` 产出，该函数保证结果 ⊆ 父。`RequestedGovernance` 越界时先取交集而非直接报错；如果交集后已无法满足子任务声明的最小工具/模块/human gate/副作用边界，则 fail-closed。默认 `maxSpawnDepth = 1` 时，子 envelope 必须移除 `spawn_agent`，不能因为父调用了 `module.sub_agent` 而让子获得再次 spawn 能力；放宽深度后也只能在剩余深度允许时保留。
- 结构闸门 admission 必须在派生子 intent **之前**完成，且为同步、确定、可静态判定；不依赖子 run 的运行时结果。默认唯一写入点是 `ISubAgentSpawnLedger.TryAdmitSpawn`，`SpawnAsync` 只校验传入的 `childLineage` 与 `request.ParentLineage` 一致性，不重复递增 `CumulativeTreeNodes` / `CumulativeFanoutOf`。

### 8.3 结构闸门记账器

归属：`src/Core/TianShu.RuntimeComposition`。

```csharp
/// <summary>树级 spawn 记账器，跨整棵 agent 树维护结构闸门计数。</summary>
/// <summary>Tree-level spawn ledger maintaining structural-gate counters across the whole agent tree.</summary>
public interface ISubAgentSpawnLedger
{
    /// <summary>
    /// 在 spawn 物化前判定是否允许；不允许时返回结构化拒绝原因，不递增任何计数。
    /// 允许时同步递增单调计数（CumulativeTreeNodes、调用方 agent 的累计 fanout）；
    /// 仅当 quota.MaxConcurrentAgents > 0（并发记账启用）时才递增可回收计数 ActiveAgents。
    /// 当 quota.MaxConcurrentAgents == 0（当前串行基线）时，admission 不检查也不递增 ActiveAgents；
    /// quota.MaxConcurrentAgents < 0 是非法配额，必须在契约构造或配置校验处 fail-closed，
    /// 因此不会出现「ActiveAgents 只增不减导致假性并发超限」或「误拒第一个子 agent」。
    /// </summary>
    SubAgentSpawnDecision TryAdmitSpawn(SubAgentLineage parentLineage, SubAgentSpawnQuota quota);

    /// <summary>
    /// 子 run 终止后仅回收「可回收」计数（ActiveAgents -= 1），且仅当并发记账启用时才需要调用。
    /// 绝不回收单调计数：CumulativeTreeNodes 与 CumulativeFanout 一经递增永不回退，
    /// 否则「终止一个、再 spawn 一个」可绕过累计上限。当前串行基线未启用并发记账，可不调用本方法。
    /// </summary>
    void OnChildTerminated(string childRunId);
}

public sealed record SubAgentSpawnDecision(
    bool Admitted,
    string? FailureCode,          // subagent.spawn_depth_exceeded / fanout_exceeded / tree_node_budget_exhausted / concurrency_exceeded
    string? FailureMessage,
    SubAgentLineage? ChildLineage); // Admitted 时给出已 Descend 的子血缘；child run id 由父 lineage + siblingIndex 稳定派生
```

### 8.4 RuntimeComposition 物化点

在 `AdaptiveRuntimeExecutionLoop` 的 `tool-exec` 物化逻辑中，对 `toolId == "spawn_agent"` 的请求走专用分支（与普通 `ToolInvocationStep` 分流）：

```text
if request.ToolId == "spawn_agent":
    decision = ledger.TryAdmitSpawn(currentLineage, quota)
    if !decision.Admitted:
        → toolResults[] 中该 callId 投影为 status=blocked + failure=decision.FailureCode
        → 不物化 ModuleCapabilityStep，不发起子 run（fail-closed）
    else:
        → 物化 ModuleCapabilityStep(moduleId="module.sub_agent", capabilityId="sub_agent.spawn",
              inputEnvelope = SubAgentSpawnRequest{ ParentLineage = currentLineage, ... },
              metadata.childLineage = decision.ChildLineage)   // childLineage 走 step metadata，不塞进 request 体
        → ExecutionRuntimeSubAgentModuleBridge 从 step metadata 解析 childLineage
        → ISubAgentModule.SpawnAsync(request, childLineage, quota, ...)
        → SubAgentRunResult 投影回 toolResults[]（status / resultText / replaySummaryRef）
```

`decision.ChildLineage` 由 ledger admission 生成，经 `ModuleCapabilityStep.Metadata` 传递，并由 `ExecutionRuntimeSubAgentModuleBridge` 解析为 `SpawnAsync` 的独立 `childLineage` 参数。它不进入 `SubAgentSpawnRequest` 体；`SubAgentSpawnRequest` 只保留模型请求、父 lineage、任务说明、证据引用、治理请求和预算请求，避免把 ledger 的结构决策混入模型请求载荷。

子结果通过既有的 `toolResults[] → ToolResultProviderInputItem` 回流机制进入父下一轮 `model-reason`，**无需新建回流通道**。

### 8.5 P29 并行 fanout 合同

P29 不引入第二个“多 Agent 引擎”。并行 fanout 是对 `spawn_agent` 同一语义的批量调度：父 run 在同一个 `tool-exec` 回合内提交多个可独立执行的子任务，RuntimeComposition 为每个子任务执行同一套 admission、governance narrowing、budget split 和 module bridge，然后在父级 fan-in 点等待终态。

归属：公共合同进入 `src/Contracts/TianShu.Contracts.Agents`；默认调度实现进入 `src/Core/TianShu.RuntimeComposition`；执行仍通过 `src/Core/TianShu.SubAgent` 和 `src/Execution/TianShu.Execution.Runtime` 的现有 sub-agent module bridge。

```csharp
/// <summary>父 turn 请求的一组可独立执行的 sub-agent fanout 子任务。</summary>
/// <summary>A parent-turn request for a bounded set of independently executable sub-agent fan-out items.</summary>
public sealed record SubAgentFanoutRequest(
    string FanoutCallId,
    SubAgentLineage ParentLineage,
    KernelSubjectRef ParentSubject,
    IReadOnlyList<SubAgentFanoutItem> Items,
    SubAgentFanoutPolicy Policy,
    SubAgentBudgetSplit BudgetSplit,
    GovernanceEnvelope RequestedGovernance,
    MetadataBag Metadata);

public sealed record SubAgentFanoutItem(
    string ItemId,
    string TaskBrief,
    IReadOnlyList<string> EvidenceRefs,
    GovernanceEnvelope? RequestedGovernanceOverride,
    KernelBudget? RequestedBudgetOverride,
    MetadataBag Metadata);

public sealed record SubAgentFanoutPolicy(
    int MaxConcurrentAgents,
    int MaxSubTasks,
    TimeSpan ItemTimeout,
    SubAgentFailureMode FailureMode,
    bool RequireAllItemsToReport);

public enum SubAgentFailureMode
{
    Unspecified = 0,
    ContinueOnFailure = 1,
    CancelSiblingsOnCriticalFailure = 2,
}
```

fanout admission 规则：

- `Items.Count` 必须 `> 0` 且 `<= MaxSubTasks`，否则 fail-closed，failure code 为 `subagent.fanout_item_count_invalid` 或 `subagent.fanout_item_count_exceeded`。
- 每个 item 都必须逐一经过 `SubAgentSpawnLedger.TryAdmitSpawn`；某个 item admission 被拒时，不得启动该 item 的子 run。
- `MaxConcurrentAgents > 0` 时，RuntimeComposition 必须用 bounded worker queue 或等价机制限制同时运行的子 run 数；不得只做静态字段记录。
- cancellation token 从父 run 传播到所有子 run；父 run 取消时，未开始 item 标记为 `cancelled_before_start`，已开始 item 通过子 run cancellation 终止并投影为 `Cancelled`。
- 单个 item timeout 只取消该 item；是否取消兄弟 item 由 `FailureMode` 决定。

当前 P29.3 落地边界：`AdaptiveRuntimeExecutionLoop` 只在同一 `tool-exec` 回合中存在多个 `spawn_agent` 请求、且 `SubAgentSpawnQuota.MaxConcurrentAgents > 0` 时启用并行 fanout scheduler；单个 spawn、未启用并发闸门、或混合普通工具请求仍走既有 RuntimeStep 执行路径。fanout scheduler 为每个 item 在启动前执行结构 admission、预算分配和 child lineage 派生，使用 bounded worker queue 限制实际并发数，并为每个 child 生成独立单 step `ExecutionPlan` 调用现有 `ModuleCapabilityStep(module.sub_agent / sub_agent.spawn)` bridge。每个 fanout 回合额外投影 `subagent.fanout.diagnostics` diagnostic step，记录 `fanoutCallId`、计划子任务数、并发上限、item timeout、每个 child 的 `callId / childRunId / status / failureCode / allocatedBudget / startedAt / completedAt`，供后续 P29.6 整树 diagnostics 扩展消费。

### 8.6 fan-in 结果归并合同

fan-in 是父 run 对一组 `SubAgentRunResult` 的结构化归并，不是自然语言拼接。它必须保留每个子 run 的 status、failure、diagnostics 和 replay ref，并给出可供父模型继续推理的摘要。

归属：合同进入 `src/Contracts/TianShu.Contracts.Agents`；默认归并实现进入 `src/Core/TianShu.RuntimeComposition`。

```csharp
public sealed record SubAgentFanInSummary(
    string FanoutCallId,
    SubAgentFanInStatus Status,
    IReadOnlyList<SubAgentRunResult> Results,
    IReadOnlyList<SubAgentConflict> Conflicts,
    IReadOnlyList<string> EvidenceRefs,
    IReadOnlyList<string> DiagnosticsRefs,
    string? SummaryText,
    MetadataBag Metadata);

public enum SubAgentFanInStatus
{
    Completed = 1,
    CompletedWithFailures = 2,
    Blocked = 3,
    Cancelled = 4,
}

public sealed record SubAgentConflict(
    string ConflictId,
    IReadOnlyList<string> ChildRunIds,
    string ConflictKind,
    string Summary,
    IReadOnlyList<string> EvidenceRefs);
```

归并规则：

- `Results` 必须保留全部计划 item 的终态，不能只返回成功项。
- 任一子失败不导致父 run 自动失败；父收到 `CompletedWithFailures` 后继续决策。
- 冲突检测先使用确定性规则：同一 schema 字段互斥值、同一 artifact ref 的不兼容修改、同一检查项不同结论。模型裁判只能作为辅助 `ConflictKind=model_reviewed` 证据，不得覆盖确定性冲突。
- fan-in 输出给父模型时必须以 `toolResults[]` 回流，内容包含 `summaryText`、每个 child 的 `childRunId / status / resultText / replaySummaryRef / diagnosticsRefs / failure`，不得泄漏子 run 内部 raw provider request、secret 或未经授权 workspace 内容。

当前 P29.4 落地边界：`TianShu.Contracts.Agents` 已提供 `SubAgentFanInSummary`、`SubAgentFanInStatus` 与 `SubAgentConflict` 公共合同；`AdaptiveRuntimeExecutionLoop` 在并行 fanout 全部 job 结束后执行确定性 fan-in 汇总。fan-in 会从每个 child `toolResults[]` 投影 `SubAgentRunResult`，保留失败、blocked、cancelled、diagnostics ref 与 replay ref，并按确定性 claim/check/artifact 规则标记冲突。父模型下一轮收到的既有 sub-agent tool result 的 `output.subAgentFanInSummary` 携带完整归并结果；同时 runtime 追加 `subagent.fanin.summary` diagnostic step，便于整树复盘。该实现只预留 `modelJudgeReserved` metadata，不调用模型裁判，也不允许模型裁判覆盖确定性失败和冲突。

### 8.7 预算拆分和全树预算

P29 的预算不是只看单个子 run。父 run 必须持有一份树级预算 ledger，用于限制整棵 multi-agent tree 的 token、cost、time、tool call 和 sub-task 数量。结构闸门仍然是 spawn admission 的第一道硬闸；预算 ledger 是执行期资源闸门，不能替代结构闸门。

归属：合同进入 `src/Contracts/TianShu.Contracts.Agents` 或 `src/Contracts/TianShu.Contracts.Kernel` 的预算扩展；默认 ledger 进入 `src/Core/TianShu.RuntimeComposition`。

```csharp
public sealed record SubAgentTreeBudget(
    KernelBudget RootBudget,
    int MaxSubTasks,
    int MaxDepth,
    int MaxConcurrentAgents,
    KernelBudget? MaxBudgetPerAgent,
    decimal? MaxCost,
    MetadataBag Metadata);

public sealed record SubAgentBudgetSplit(
    SubAgentBudgetSplitMode Mode,
    int? MaxTokensPerAgent,
    decimal? MaxCostPerAgent,
    TimeSpan? MaxTimePerAgent,
    int? MaxToolCallsPerAgent,
    int? MaxRetriesPerAgent);

public enum SubAgentBudgetSplitMode
{
    Unspecified = 0,
    EqualShare = 1,
    ExplicitPerItem = 2,
    ConservativeMinimum = 3,
}
```

预算规则：

- fanout 之前必须通过 `SubAgentTreeBudgetLedger.TryAllocateForChild(...)` 为每个 item 分配 `KernelBudget`；缺预算、预算为负、子预算总和超过父剩余预算时 fail-closed，`ConservativeMinimum` 只能在父剩余预算、显式请求预算和每 agent 上限之间取更小值，不能扩大预算。
- 子 run 的 provider usage / estimated usage 必须回写树级 ledger；若 provider usage 缺失，可使用估算值，但必须投影 `estimated=true` 和 source。
- 全树预算耗尽时，未开始 item 标记为 `blocked`，failure code 为 `subagent.tree_budget_exhausted`；已运行 item 按 cancellation 语义收敛。
- 父 run 的预算不得被子 run 扩大；子预算只能来自父剩余预算切分或显式配置上限，两者取更保守值。

### 8.8 子树治理、取消传播与失败隔离

每个子 run 都继承父治理并单向收窄。P29 并发只改变“同时有多个子 run”，不改变治理不变量。

必须保持以下边界：

- 子 run 不可绕过父级 policy、tool budget、workspace sandbox、human gate、module descriptor 或 trace policy。
- 子 run 写 workspace 时仍走正常 RuntimeStep / ToolUse / human gate；父级 approval 可按 §6.3 覆盖结构闸门内的整棵子树，但不能扩大副作用等级。
- 父 cancellation 必须传播到所有子 run；子 run cancellation 不自动取消父 run，除非 fanout policy 指定 `CancelSiblingsOnCriticalFailure` 且该 failure 被标记为 critical。
- 子 run 的 exception、provider failure、tool failure、approval denied、budget exhausted 都必须投影为 `SubAgentRunResult.Failure`，不得抛出到父 run 导致父状态机崩溃。
- 子 run 之间不共享 mutable state；共享证据只能通过 artifact/evidence ref 传递，并由父 fan-in 节点显式归并。

当前 P29.5 落地边界：`SubAgentGovernanceNarrowing` 对 `PolicyIds` 采用与 tool/module 相同的默认继承语义，spawn request 未显式声明 policy 时，子 envelope 继承父 policy 集合；显式声明时只能取父集合交集，避免子 run 通过空 policy 绕过父级 model route policy。`ExecutionRuntimeSubAgentModuleBridge` 在调用 `ISubAgentModule` 前校验 `SubAgentSpawnRequest.RequestedBudget` 不得超过父 `ModuleCapabilityStep.Budget` 的 token/time/cost/retry/tool-call 任一维度，超出时以 `subagent_requested_budget_exceeds_step_budget` fail-closed。bridge 还把父 `GovernanceEnvelope`、`TracePolicy`、父 step budget、permission、side effect、execution/run id 与工作目录投影到 `SubAgentModuleInvocationContext.Metadata`；`SubAgentOrchestrationModule` 再将这些内部治理链信息写入子 `ExecutionRuntimeContext.Metadata`，并保持子 runtime context 的 `WorkingDirectory` 与父 context 一致。现有测试证明：父 human gate 与 approval 会传入子 envelope，子 runtime context 保留父 trace/budget/governance metadata，且超预算请求不会启动子 run。

### 8.9 多 Agent diagnostics、artifact 与整树复盘

P29 必须把每个子树作为可复盘对象，而不是只保留最终自然语言摘要。

归属：diagnostics / projection 字段进入 `src/Contracts/TianShu.Contracts.Agents`、`src/Contracts/TianShu.Contracts.Projections` 和 RuntimeComposition diagnostics；最终验收 evidence 由 `tools/Run-TianShuFinalAcceptance.ps1` 或 v0.9 gate 读取。

```csharp
public sealed record SubAgentTreeDiagnostics(
    string RootRunId,
    string LedgerRef,
    SubAgentSpawnQuota Quota,
    SubAgentTreeBudget Budget,
    IReadOnlyList<SubAgentNodeDiagnostics> Nodes,
    IReadOnlyList<SubAgentEdgeDiagnostics> Edges,
    IReadOnlyList<string> ReplayRefs,
    IReadOnlyList<string> AuditRefs);

public sealed record SubAgentNodeDiagnostics(
    string RunId,
    string? ParentRunId,
    int Depth,
    int SiblingIndex,
    SubAgentRunStatus Status,
    string? ReplaySummaryRef,
    IReadOnlyList<string> DiagnosticsRefs,
    SubAgentFailure? Failure);

public sealed record SubAgentEdgeDiagnostics(
    string ParentRunId,
    string ChildRunId,
    string SpawnCallId,
    string FanoutCallId,
    IReadOnlyList<string> EvidenceRefs);
```

复盘规则：

- 每个子 run 必须有 `ReplaySummaryRef` 或明确 unavailable failure code。
- fanout/fan-in 的父 step 必须引用所有 child run id，不能只记录数量。
- diagnostics 必须能回答：谁 spawn 了谁、每个子用了多少预算、哪些子失败、失败是否取消兄弟、fan-in 如何处理冲突、哪些 evidence 支撑最终结论。
- artifact 可由子 run 生成，但父 fan-in 只消费 artifact ref 和摘要；完整内容读取仍受 workspace/artifact 权限治理。

当前 P29.6 落地边界：`TianShu.Contracts.Agents` 已提供 `SubAgentTreeDiagnostics`、`SubAgentNodeDiagnostics`、`SubAgentEdgeDiagnostics`，并在 `SubAgentRunResult` 中保留 `ArtifactRefs`。`SubAgentOrchestrationModule` 会从子 runtime output 收集 `artifactRefs` / `artifacts` 并回写到子 run result。`AdaptiveRuntimeExecutionLoop` 在并行 fanout 完成后，以 root lineage、quota、tree budget、fan-in summary 和每个 child result 生成同一份整树诊断：根节点、子节点、spawn/fanout 边、replay refs、audit refs、failure code/message、artifact refs 与 `reportText` 都会投影到每个 `spawn_agent` tool result 的 `output.subAgentTreeDiagnostics`，同时投影到 fanout diagnostics step 与 fan-in summary step。当前实现仍是内存内结构化投影，不等同于持久 artifact store；P29.9 最终验收案例必须读取这些结构化字段，而不是只看自然语言摘要。

## 9. 与其他设计的接口

| 层 / 文档 | 交互 |
| --- | --- |
| `tianshu-kernel-core-loop-design.md` | 子 run 完整复用 `StableKernelCore` + `StageGraphInterpreter`；Kernel 不感知"父子"，只看到又一个被治理的 `TurnIntent`。收窄不变量复用既有 validator。 |
| `tianshu-builtin-stage-graph-design.md` | 子 run 跑 `graph.turn.default`，与顶层 turn 同一张图。`spawn_agent` 作为模型可见请求入口进入 `tool-exec` allow-list 上界，但只有 governance 同时授予 `spawn_agent` 与 `module.sub_agent` 时才实际开放；未授予时仍 fail-closed。P29 fanout/fan-in 仍发生在 `tool-exec` 回合与后续 `model-reason` 回边，不新增独立 Stage。 |
| `tianshu-execution-runtime-design.md` | spawn 经 `ModuleCapabilityStep` + `ExecutionRuntimeSubAgentModuleBridge`（或通用 module bridge 的 sub-agent 分支）；遵守"RuntimeStep 分型边界固定"——不新增 step 类型。P29 并行只改变 RuntimeComposition 对多个 ModuleCapabilityStep 的调度和 fan-in 结果投影，不改变 RuntimeStep 分型。 |
| `tianshu-module-plane-design.md` | `ISubAgentModule` 是一个标准 Module，经 `ModuleDescriptor` / `ModuleKind.SubAgentOrchestration`（或等价命名）进入 discovery / 治理 / 健康检查。 |
| `tianshu-old-new-loop-parity-design.md` | 与旧/新 loop parity 文档分工：agent job 仍是宿主管理 runtime surface；模型可请求的 sub-agent 能力以本文的 `spawn_agent` / `module.sub_agent` 受治理路径为准。P29 fanout 不复用旧 AppHost fanout CSV / agent job 工具。 |

## 10. 落地与迭代约束

1. **串行 spawn 是已落地基线，并发 fanout 是 P29 目标。** 单个 `spawn_agent` 同步执行到终态并回流继续保留；P29 在同一模型和治理语义上扩展多个子 run 的 bounded parallel fanout、fan-in 和整树 diagnostics。
2. **并发默认关闭，显式启用。** `MaxConcurrentAgents = 0` 仍表示不启用并发记账；只有配置、治理和 runtime composition 都显式允许时，才可把 `MaxConcurrentAgents` 设为正数并启用 bounded worker queue。不得因为合同字段存在而默认并发。
3. **结构闸门不可关闭。** `maxSpawnDepth` 必须为正且有限；`maxFanoutPerAgent`、`maxTreeNodes`、`maxSubTasks` 必须有有限上限；任何无界、多余或负数配置由 validator/ledger fail-closed。
4. **token/time 不作 spawn admission 结构闸。** 严禁用 token/time 预算替代 `maxSpawnDepth / maxFanoutPerAgent / maxTreeNodes / maxConcurrentAgents / maxSubTasks`。预算是执行期资源闸门；结构安全只能来自离散结构闸门。
5. **全树预算必须可投影。** P29 以后每个子 run 的 token / cost / time / tool call 消耗必须归属到 root run 的 tree budget；provider usage 缺失时允许估算，但必须投影 source 和 estimated 标记。
6. **子能力默认更严。** 子 envelope 收窄是单向的；`SubAgentGovernanceNarrowing` 只能取交集。越界请求可以被夹紧，但任何"夹紧后仍越界"或"夹紧后缺失 sub-agent module 最小执行能力"的派生必须 fail-closed。
7. **不引入子间通信。** 并发 fanout 不意味着子 agent 可以互相通信。子 run 之间只能通过父 fan-in 节点汇总结构化结果；共享事实只能以 evidence/artifact ref 进入父级归并。
8. **失败隔离是硬要求。** 子 run 的失败、取消、超时、budget exhausted、approval denied 都必须落到 `SubAgentRunResult` 或 `SubAgentFanInSummary`，不得直接污染父 run 状态机。
9. **模型裁判不是唯一 fan-in 判据。** 结果冲突、缺项、失败项必须先由确定性规则标记；模型可以生成归纳性 summary，但不能吞掉冲突和失败证据。
10. **整树复盘优先于自然语言摘要。** 任一 fanout/fan-in 验收不能只看最终回复；必须能从 root run 追踪到每个 child run、budget、failure、artifact、diagnostics 和 replay ref。

## 11. v0.9 multi-agent release gate

v0.9 发布前必须通过 `tools/Test-TianShuV09MultiAgentReleaseGate.ps1`。该 gate 是发布门禁，不替代完整 live 最终验收；它只证明可发布版本的多 Agent 能力默认安全、可关闭、可审计，并且最终验收报告具备公开记录所需字段。

门禁口径：

- 默认受限：CLI 未显式传入 `--enable-subagents` 时，provider tool surface 不得暴露 `spawn_agent`；底层模块存在不等于模型可见。
- 显式启用：`send --kernel-runtime-loop --enable-subagents` 必须同时传入 `--approve-all`，否则 CLI parse fail-closed；启用后还必须由 governance 同时授予 `spawn_agent` 和 `module.sub_agent`。
- 可关闭：不传 `--enable-subagents` 即为关闭模型自主 sub-agent 请求入口；`MaxConcurrentAgents = 0` 仍表示不启用并发记账。
- 可审计：确定性多 Agent harness 必须输出同一证据链，覆盖并行 fanout、子树治理、预算切分、fan-in、失败隔离和整树 diagnostics。
- 公开记录：最终验收输出必须包含 `SubAgentLiveObservationProtocol`、整体与 cell 级触发率、有效率、误触发率、观察矩阵、artifact root 和结论字段。公开记录只说明真实模型在冻结任务矩阵下的观察结果，不承诺模型一定自主触发 sub-agent。
- 非承诺式结论：live 矩阵若完整执行但未观察到有效自主 `spawn_agent`，结论只能是“机制可用，当前任务/模型/tool surface 下未观察到自主触发”；不得改写为产品级自主多 Agent 已通过。

因此，v0.9 gate 的通过条件是**机制与安全边界可发布**，不是“真实模型在任意任务下都会自动拆分多 Agent”。后者只能由最终验收的 live 观察矩阵公开记录，不能作为默认发布门禁的硬性成功条件。

## 12. 验收基线

- 父 turn 模型输出 `spawn_agent` 请求 → 物化为 `ModuleCapabilityStep(sub_agent.spawn)` → 子 run 完整跑通 `graph.turn.default` → 子 `resultText` 作为 `toolResult` 回流父下一轮 `model-reason`。
- P29 fanout 验收必须证明：同一父 run 在一次 fanout 中创建多个 child run；`MaxConcurrentAgents > 0` 时实际并发受 bounded worker queue 限制；超过并发、深度、扇出、树节点或子任务数上限时 fail-closed。
- P29 fan-in 验收必须证明：所有计划 item 都进入 `SubAgentFanInSummary.Results`；成功、失败、blocked、cancelled 都有结构化结果；冲突、缺项和失败列表不能被 summaryText 覆盖。
- P29 预算验收必须证明：父预算被拆分为每个子 run 的 `KernelBudget`；子 usage / estimated usage 回写 tree budget；预算耗尽时未开始 item 被 blocked，已运行 item 按 cancellation 收敛。
- P29 治理验收必须证明：子 run 不可绕过父级 policy、tool budget、workspace sandbox、human gate、module descriptor 或 trace policy；父 approval 继承仍然只覆盖权限不超过父、结构有界的子树。
- P29 失败隔离验收必须证明：单个子 run provider/tool/approval/budget 失败不会使父状态机崩溃；父可在 `CompletedWithFailures` 后继续决策。
- P29 diagnostics 验收必须证明：整棵 agent tree 可从 `SubAgentTreeDiagnostics`、lineage、spawn/fanout/fan-in step、child replay refs 和 audit refs 重建。
- 当前阶段收口结论：确定性机制门禁已证明 `spawn_agent -> ModuleCapabilityStep(module.sub_agent/sub_agent.spawn) -> child run -> toolResults[] 回流`；v0.5 串行基线三协议 live 观察矩阵已证明 turn 完成 `27/27`、provider tool surface 暴露 `spawn_agent` `27/27`、模型自主触发 `spawn_agent` 为 `0/27`。因此当前只能宣称机制完整、工具面就绪和非诱导 live 观察未触发，不得宣称产品级模型自主 Sub-Agent 已通过。
- 最终验收必须分两层记录：确定性机制门禁证明 `spawn_agent -> ModuleCapabilityStep(module.sub_agent/sub_agent.spawn) -> ExecutionRuntimeSubAgentModuleBridge -> SubAgentOrchestrationModule -> child run -> toolResults[] 回流`；live 自主触发是预冻结观察矩阵，用于记录真实模型在固定任务、固定模型协议 cell 与固定每格轮数下是否自主请求 `spawn_agent`。
- live 观察矩阵的提示词本体只允许描述问题域与交付要求，不得包含 agent、子任务、并行、委托、派生、拆分、协作、执行轨道、`spawn_agent` 等方法诱导词。若提示词含方法诱导，本轮 live 观察证据无效。
- live 观察矩阵必须在执行前冻结任务、模型协议 cell 和每格轮数，默认以 `3 tasks x 3 model cells x 3 runs` 形成 27 次观察；专门行为研究可显式调整 N，但结果必须记录实际 N。执行中不得因观察到或未观察到 `spawn_agent` 而早停、追加/删除样本或改写判读规则。
- P29.7 之后 live 观察矩阵必须同时输出整体和每个 `task x model` cell 的 `TriggerRate`、`EffectiveRate`、`FalsePositiveRate`。`SpawnObserved` 只表示观察到真实或弱 `spawn_agent` 信号；`EffectiveSpawnObserved` 必须同时具备 bridge/module、`toolResults`/fan-in/tree diagnostics 回流和 completed disposition 证据；`FalsePositiveSpawnObserved` 表示出现弱信号但缺少有效回流证据。
- live 观察矩阵的结论分三类：任一计划 run 观察到有效自主 `spawn_agent` 且回流成功时，产品级自主 Sub-Agent 证据成立；完整矩阵未观察到时，结论为“机制可用，当前任务/模型/tool surface 下未观察到自主触发”，这是有效工程观察但不能冒充产品级自主 Sub-Agent 通过；矩阵未完整执行、证据不足、提示词含方法诱导或无法确认 provider request 中 tool surface 时，本轮 live 观察无效，需要按同一冻结协议重跑。
- 子 `GovernanceEnvelope` 经断言确认 ⊆ 父：工具/模块/副作用上限/human gate 任一越界派生时 fail-closed。
- 三个基础 admission 结构闸门各有 fail-closed 负例：`maxSpawnDepth` 超限（孙 agent 被拒）、`maxFanoutPerAgent` 超限、`maxTreeNodes` 超限，分别命中对应 failure code，且**未发起**越界子 run。
- `maxTreeNodes` / `maxFanoutPerAgent` 的单调性有专项断言：子 run 终止后再 spawn，累计计数**不回退**——"终止一个再 spawn 一个"不得绕过累计上限（`OnChildTerminated` 只回收 `ActiveAgents`，不回收单调计数）。
- 当前串行基线（`MaxConcurrentAgents = 0`）有专项断言：连续 spawn 多个子 agent 不会因 `ActiveAgents` 累积而被 `concurrency_exceeded` 误拒，且第一个子 agent 不被误拒——证明并发记账在串行基线完全不参与 admission。
- 审批继承（§6.3）有断言：父 envelope 带 `ApprovalIds` 时，子继承的 approval ⊆ 父；子不能凭空获得新 approval，也不能用继承 approval 覆盖父本不允许的副作用等级。
- approval 显式缩窄有断言：未声明 requested approval 时继承父 approval 全集；声明 requested approval 时只取 `父 ∩ requested`，不能新增。
- child run id 稳定派生有断言：同一个 parent lineage 与相同 sibling index 在新 ledger 中得到同一个 child run id；同一父的不同 sibling index 得到不同 child run id。
- 子 run fail / blocked 时，父收到结构化 `SubAgentRunResult{ Status, Failure }`，父状态机不被子失败污染（父可继续决策）。
- 整棵 agent 树可从根 runId + `SubAgentLineage` + 各子 `ReplaySummaryRef` 完整复盘 spawn 关系。
- `ISubAgentModule` 不直接构造 `StableKernelCore` / `StageGraph` / `RuntimeStep`，只消费 `IKernelRuntimeExecutionLoop`（源码边界守护）。
- token/time 预算不参与 `spawn_agent` admission 的结构判定；同时验证子 run 启动后仍受自身预算、timeout、cancellation 与工具回边预算约束。
