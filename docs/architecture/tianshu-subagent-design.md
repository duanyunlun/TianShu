# TianShu 多 Agent / Sub-Agent 协同设计

## 1. 文档定位

本文是 TianShu **多 Agent / Sub-Agent 协同**能力的专项设计基线，用于细化 `docs/tianshu-architecture-spec.md` 的六层职责、可控演化内核与 ToolUse 模型在「一个父 turn 通过受治理请求派生子 agent」场景下的落地形态。

本文只描述当前正式设计与第一版可落地边界，不记录历史方案、讨论过程或临时实现。它不替代主架构规范、`tianshu-kernel-core-loop-design.md`、`tianshu-builtin-stage-graph-design.md` 或 `tianshu-execution-runtime-design.md`，而是这些基线在「多 Agent」维度上的具体实例。

核心命题：**一个 sub-agent = 一次被治理收窄、结构有界、上下文隔离的嵌套 `CoreIntent(Turn)` run。** 多 Agent 不在稳定内核上新增编排机制，而是把内核已有的 turn 能力**递归地、受治理地**嵌套一层。

## 2. 设计原则

1. **认知独立，授权受限。** 子 agent 在「上下文、threadId、StageGraph、Model-Route、失败域」上完全独立；但在「权限上限、spawn 结构、副作用边界」上必须从父继承并收窄。独立的是*怎么想*，受约束的是*能动多大*。
2. **复用而非新建。** 子 agent 执行复用 `graph.turn.default`、`StableKernelCore`、`AdaptiveRuntimeExecutionLoop` 的完整反应式 loop，不为多 Agent 引入第二套编排路径或第二套验证器。
3. **结构有界优先于成本有界。** 防 fork 炸弹靠**离散结构闸门**（spawn 深度 / 扇出 / 树总节点数），在 spawn 那一刻可静态判定、超限 fail-closed。token / time 预算不作为 spawn admission 的结构闸；子 run 进入执行后仍受自身 `KernelBudget`、Runtime timeout、provider retry 和工具回边预算约束。
4. **权限单向收窄。** 子 `GovernanceEnvelope` 必须是父 envelope 的子集（allow-list、module、副作用上限、human gate 只能更严不能更宽）。这是主架构「副作用层层收窄」不变量在多 Agent 维度上的必然推论。
5. **Orchestrator–Worker，子间不通信。** 第一版只支持「父分解任务 → spawn 子 → 子对父负责 → 结果回流父」。子 agent 之间不互相通信、不辩论、不共享状态。peer/debate 范式明确排除。
6. **每个子 run 可独立复盘。** 子 run 有自己完整的 trace/replay；父 trace 中的 spawn step 引用子 run 的 runId，整棵 agent 树可从根重建。

## 3. 适用范围

| 项 | 本版处理 |
| --- | --- |
| 父 turn 通过 `spawn_agent` 串行派生子 agent | **本版主线**：模型在治理允许时可请求 `spawn_agent`，系统将其物化为子 `CoreIntent(Turn)` → 同步执行到终态 → 结果回流。该能力已实现，不等同于 live 观察中已经发生模型自主触发。 |
| 父 turn 通过 `spawn_agent` 并行 fanout（多子并发） | **本版预留、不默认开启**：契约与守护按并发设计，但第一版执行器先串行；并发作为第二步在本文档下显式追加。 |
| 子 agent 再 spawn 孙 agent | **受深度上限约束**：默认 `maxSpawnDepth = 1`（父→子，子不可再 spawn）；超限 fail-closed。 |
| 子 agent 之间通信 / 辩论 / 共享上下文 | **明确不支持**：peer/debate 反模式，与可复盘、fail-closed 哲学冲突。 |
| 宿主/人工管理的 agent job / roster | **不在本文范围**：由 `ControlPlane.Workflows` / `ControlPlane.Agents` 承担（见 tracker 条目 34），与模型自主 sub-agent 是两条独立路径。 |

> 与条目 31.8 / 34.4 的关系：agent job / roster 仍属于人工或宿主管理 runtime surface，不进入 provider-directed tool surface。模型可请求的 sub-agent 能力由本文单独定义：`spawn_agent` 只在 governance 显式授予时开放，未授予时继续 fail-closed；执行不复用旧 AppHost agent job 工具。当前 live 观察结论为 `SPAWN_OBSERVED=0/27`，只能说明机制已就绪但当前非诱导任务下模型未主动使用该能力。

## 4. 涉及项目与代码归属

| 项目 | 状态 | 归属内容 |
| --- | --- | --- |
| `src/Contracts/TianShu.Contracts.Modules` | 现有 | 新增 `ModuleKind.SubAgentOrchestration`（或等价命名的 Agent orchestration module kind）与 `BuiltInModuleDescriptors.SubAgent` 模块身份声明。 |
| `src/Contracts/TianShu.Contracts.Agents` | 现有 | 承载 sub-agent 公共契约：`SubAgentSpawnRequest`、`SubAgentRunResult`、`SubAgentLineage`、`SubAgentSpawnQuota`、`SubAgentFailure`。 |
| `src/Contracts/TianShu.Contracts.Kernel` | 现有 | 子 `CoreIntent(Turn)` 的派生构造规则（envelope 收窄、subject 派生、lineage metadata）落在 Kernel 契约层；不新增 IntentKind。 |
| `src/Core/TianShu.SubAgent` | 现有 | `ISubAgentModule` 默认实现 `SubAgentOrchestrationModule`：接收 spawn 请求 → 派生收窄子 intent → 调用 `IKernelRuntimeExecutionLoop.RunReactiveAsync` → 投影 `SubAgentRunResult`。 |
| `src/Core/TianShu.RuntimeComposition` | 现有 | `AdaptiveRuntimeExecutionLoop` 中 `tool-exec` 物化 spawn 请求为 `ModuleCapabilityStep`；`SubAgentSpawnLedger` 维护树级 lineage 与结构闸门计数。 |
| `src/Execution/TianShu.Execution.Runtime` | 现有 | 新增或扩展 module bridge（如 `ExecutionRuntimeSubAgentModuleBridge` 或通用 `ExecutionRuntimeModuleBridge`）路由 `ModuleCapabilityStep(sub_agent.spawn)` 到 `ISubAgentModule`，复用既有 module bridge governance 校验。 |
| `tests/TianShu.SubAgent.Tests` | 现有 | envelope 收窄、结构闸门 fail-closed、结果回流、lineage 复盘的验收测试。 |

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

`graph.turn.default` 的 `tool-exec` allow-list 增加模型可见的 `spawn_agent` 请求入口（仅在 governance 显式授予多 Agent 能力时）。该入口不是普通 tool bridge 执行，而是在 RuntimeComposition 中被改写为 `ModuleCapabilityStep(module.sub_agent / sub_agent.spawn)`；因此同一次治理还必须允许 `module.sub_agent`，且副作用等级声明为 `HostMutation`（spawn 子 run 是一次受治理的主机侧编排动作）。第一版只开放同步 `spawn`：`wait` / `send_input` / `close_agent` 不进入 allow-list，也不作为第一版可调用能力。

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
| `AllowedToolIds` | 子 ⊆ 父；第一版无条件移除 `spawn_agent`（见下方说明）。 |
| `AllowedModuleIds` | 子 ⊆ 父。 |
| `MaxSideEffectLevel` | 子 ≤ 父（枚举值不高于父）。 |
| `RequiresHumanGate` | 子为 `父.RequiresHumanGate || spawnRequest.requiresHumanGate`（只能更严）。 |
| `PolicyIds` | 子 ⊆ 父。 |
| `ApprovalIds` | 默认继承父已有审批引用全集；若 spawn request 显式声明 `RequestedGovernance.ApprovalIds`，则只允许缩窄为 `父 ∩ requested`，子不能凭空获得新审批（审批继承语义见 §6.3）。 |

**关于 `spawn_agent` 的移除**：第一版 `maxSpawnDepth = 1`（见 §7），子 agent 的 depth 已达上限、本就不允许再 spawn，因此子 envelope **无条件移除** `spawn_agent`——这是一条没有例外分支的硬规则，不要在第一版实现"判断子能否继承 spawn_agent"的条件逻辑（那是死分支）。只有当后续把 `maxSpawnDepth` 放宽到 ≥ 2 时（见 §10 约束 2），才引入"depth 仍有余量则保留 `spawn_agent`"的条件逻辑，并在本文档显式追加该规则。

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

agent 树的安全来自约束它的**形状**，不是能耗。第一版 admission 必须执行前三个离散、可静态判定、超限 fail-closed 的结构闸门；并发版本额外启用 `maxConcurrentAgents`：

| 闸门 | 含义 | 计数性质 | 默认值 | fail-closed code |
| --- | --- | --- | --- | --- |
| `maxSpawnDepth` | 从根 turn 算起的最大嵌套层数 | 血缘链上的位置（非计数器） | `1`（父→子，子不可再 spawn） | `subagent.spawn_depth_exceeded` |
| `maxFanoutPerAgent` | 单个 agent 一生可 spawn 的直接子数上限 | **单调累计**（按 agent，只增不减） | `8` | `subagent.fanout_exceeded` |
| `maxTreeNodes` | 整棵 agent 树历史上创建过的 agent 总数上限（含根） | **单调累计**（按树，只增不减） | `32` | `subagent.tree_node_budget_exhausted` |
| `maxConcurrentAgents` | 整棵树同时活跃的子 agent 数上限（仅在 `> 0` 时启用；第一版串行设为 `0`/不启用） | **可回收**（按树，随终止递减；仅启用时记账） | `0`（第一版不启用）／`4`（并发版本建议默认） | `subagent.concurrency_exceeded` |

**关键区分——单调累计 vs 可回收，两者语义不同、不可共用一个计数器：**

- `maxFanoutPerAgent` / `maxTreeNodes` 是**单调累计**：它们记录"历史上创建过多少"，**只增不减**。一个子 run 终止后，"它曾被创建过"这个事实不能回收——否则"终止一个、再 spawn 一个"就能无限循环绕过总量上限。这两个闸门防的是**累计繁殖失控**。
- `maxConcurrentAgents` 是**可回收**：它记录"此刻同时运行的子 agent 数量"，随子 agent 终止递减。它防的是**瞬时并发过载**，与累计无关。**第一版串行执行（spawn 后同步跑到终态再回流）完全不启用该闸门——admission 路径既不检查它、也不递增 `ActiveAgents`**，整套并发记账留到并发第二步再启用（见下方伪代码的启用守卫与 §10 约束 1）。

这些量由 RuntimeComposition 的 `SubAgentSpawnLedger` 跨整棵树维护，并随 `SubAgentLineage` 透传到每个子 run。每次 spawn 物化前检查：

```text
spawn 物化前（在 ModuleCapabilityStep 生成前）：
  if lineage.Depth + 1 > maxSpawnDepth                       → fail-closed (spawn_depth_exceeded)
  if ledger.CumulativeFanoutOf(lineage.CurrentRunId) + 1 > maxFanoutPerAgent
                                                            → fail-closed (fanout_exceeded)
  if ledger.CumulativeTreeNodes + 1 > maxTreeNodes          → fail-closed (tree_node_budget_exhausted)

  // 并发闸门仅在显式启用时检查；MaxConcurrentAgents = 0 表示「未启用」（第一版串行即如此）。
  // 这样可避免：未启用并发时 ActiveAgents(0)+1 > 0 误拒第一个子 agent。
  if maxConcurrentAgents > 0 and ledger.ActiveAgents + 1 > maxConcurrentAgents
                                                            → fail-closed (concurrency_exceeded)

  else → 允许 spawn：
           CumulativeTreeNodes += 1（永不回收）
           CumulativeFanoutOf(caller) += 1（永不回收）
           if maxConcurrentAgents > 0: ActiveAgents += 1（终止时回收）   // 未启用并发时不碰 ActiveAgents
```

启用规则：`ActiveAgents` 只有在 `MaxConcurrentAgents > 0`（即显式启用并发记账）时才递增；递增了就必须由 `OnChildTerminated` 回收。第一版串行 `MaxConcurrentAgents = 0`，admission 不递增 `ActiveAgents`，因此**不存在「串行版 `ActiveAgents` 只增不减导致假性并发超限」的问题**，也不依赖第一版调用 `OnChildTerminated`。负数不是"关闭并发"的等价写法，而是非法配额，必须由契约构造或配置校验 fail-closed。

子 run 终止时只回收 `ActiveAgents`（仅当并发启用时），**绝不回收** `CumulativeTreeNodes` 或 `CumulativeFanoutOf`。

`SubAgentLineage` 是不可变的传递链：父把自己的 `lineage`（含 depth、root runId、current runId、parent runId、ledger 引用）注入子 spawn 请求；ledger admission 通过后生成 child lineage。child run id 必须由 `ledgerRef + rootRunId + parent currentRunId + siblingIndex` 稳定派生，而不是随机生成；这样同一个父 run、同一个 spawn 序列在 replay 时可重新得到相同的子 run id。若父 run id 本身由宿主随机生成，则该随机性属于根 run 的既定事实；sub-agent 层只保证在同一根 run 事实下确定派生。子若再 spawn（depth 允许时），用自己的 lineage 继续派生下一层。**深度上限是砍断指数爆炸的主闸**；累计扇出和累计总节点数是宽度与绝对兜底；并发活跃数是并发版本的瞬时过载保护。

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
    int MaxConcurrentAgents);   // 整棵树同时活跃子 agent 数上限（可回收）；0 表示不启用并发记账，第一版串行设为 0；负数非法

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
- 子 intent 的 `GovernanceEnvelope` 必须经 `SubAgentGovernanceNarrowing.Narrow(parentEnvelope, request.RequestedGovernance)` 产出，该函数保证结果 ⊆ 父。`RequestedGovernance` 越界时先取交集而非直接报错；如果交集后已无法满足子任务声明的最小工具/模块/human gate/副作用边界，则 fail-closed。子 envelope 第一版仍必须移除 `spawn_agent`，不能因为父调用了 `module.sub_agent` 而让子获得再次 spawn 能力。
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
    /// 当 quota.MaxConcurrentAgents == 0（第一版串行）时，admission 不检查也不递增 ActiveAgents；
    /// quota.MaxConcurrentAgents < 0 是非法配额，必须在契约构造或配置校验处 fail-closed，
    /// 因此不会出现「ActiveAgents 只增不减导致假性并发超限」或「误拒第一个子 agent」。
    /// </summary>
    SubAgentSpawnDecision TryAdmitSpawn(SubAgentLineage parentLineage, SubAgentSpawnQuota quota);

    /// <summary>
    /// 子 run 终止后仅回收「可回收」计数（ActiveAgents -= 1），且仅当并发记账启用时才需要调用。
    /// 绝不回收单调计数：CumulativeTreeNodes 与 CumulativeFanout 一经递增永不回退，
    /// 否则「终止一个、再 spawn 一个」可绕过累计上限。第一版串行未启用并发记账，可不调用本方法。
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

## 9. 与其他设计的接口

| 层 / 文档 | 交互 |
| --- | --- |
| `tianshu-kernel-core-loop-design.md` | 子 run 完整复用 `StableKernelCore` + `StageGraphInterpreter`；Kernel 不感知"父子"，只看到又一个被治理的 `TurnIntent`。收窄不变量复用既有 validator。 |
| `tianshu-builtin-stage-graph-design.md` | 子 run 跑 `graph.turn.default`，与顶层 turn 同一张图。`spawn_agent` 作为模型可见请求入口进入 `tool-exec` allow-list 上界，但只有 governance 同时授予 `spawn_agent` 与 `module.sub_agent` 时才实际开放；未授予时仍 fail-closed。 |
| `tianshu-execution-runtime-design.md` | spawn 经 `ModuleCapabilityStep` + `ExecutionRuntimeSubAgentModuleBridge`（或通用 module bridge 的 sub-agent 分支）；遵守"RuntimeStep 分型边界固定"——不新增 step 类型。 |
| `tianshu-module-plane-design.md` | `ISubAgentModule` 是一个标准 Module，经 `ModuleDescriptor` / `ModuleKind.SubAgentOrchestration`（或等价命名）进入 discovery / 治理 / 健康检查。 |
| `tianshu-old-new-loop-parity-design.md` | 与旧/新 loop parity 文档分工：agent job 仍是宿主管理 runtime surface；模型可请求的 sub-agent 能力以本文的 `spawn_agent` / `module.sub_agent` 受治理路径为准。 |

## 10. 落地与迭代约束

1. **先串行，后并发。** 第一版 `SpawnAsync` 同步执行单个子 run 至终态再回流，证明隔离 + 收窄 + 回流闭环正确；并行 fanout（`wait` / 多子并发 / 活跃节点配额）作为第二步在本文档下显式追加，不混入第一版结论。
2. **深度上限不可关闭。** `maxSpawnDepth` 必须为正且有限；任何使其无界的配置由 validator/ledger fail-closed。第一版默认 `1`（子不可再 spawn），确认闭环后再按真实需求放宽到 `2`。
3. **token/time 不作 spawn admission 结构闸。** 严禁用 token/time 预算作为 `spawn_agent` 是否允许物化的结构判定依据；结构安全只能来自 `maxSpawnDepth / maxFanoutPerAgent / maxTreeNodes`。子 run 启动后仍必须遵守自身预算、Runtime cancellation、provider timeout 和工具回边预算。
4. **并发记账与串行解耦。** 第一版串行设 `MaxConcurrentAgents = 0`（不启用）：admission 既不检查 `maxConcurrentAgents`、也不递增 `ActiveAgents`，不依赖 `OnChildTerminated`。`ActiveAgents` 的递增/回收只在 `MaxConcurrentAgents > 0` 时成对启用，留给并发第二步；严禁在串行版半接并发记账（只增不减或检查未初始化的活跃数）。
5. **子能力默认更严。** 子 envelope 收窄是单向的；`SubAgentGovernanceNarrowing` 只能取交集。越界请求可以被夹紧，但任何"夹紧后仍越界"或"夹紧后缺失 sub-agent module 最小执行能力"的派生必须 fail-closed。
6. **不引入交互式子通信。** 第一版不提供 sub-agent 之间的消息/共享状态接口，也不提供父对子 run 的中途 `send_input` / `wait` / `close_agent` 通道；父只能通过一次同步 `spawn` 提供 `taskBrief` 与证据引用，并在子 run 终态后接收 `SubAgentRunResult`。交互式父子通信若未来需要，必须作为并发版本专项追加。

## 11. 验收基线

- 父 turn 模型输出 `spawn_agent` 请求 → 物化为 `ModuleCapabilityStep(sub_agent.spawn)` → 子 run 完整跑通 `graph.turn.default` → 子 `resultText` 作为 `toolResult` 回流父下一轮 `model-reason`。
- 当前阶段收口结论：确定性机制门禁已证明 `spawn_agent -> ModuleCapabilityStep(module.sub_agent/sub_agent.spawn) -> child run -> toolResults[] 回流`；三协议 live 观察矩阵已证明 turn 完成 `27/27`、provider tool surface 暴露 `spawn_agent` `27/27`；模型自主触发 `spawn_agent` 为 `0/27`。因此当前只能宣称机制完整、工具面就绪和非诱导 live 观察未触发，不得宣称产品级模型自主 Sub-Agent 已通过。
- 最终验收必须分两层记录：确定性机制门禁证明 `spawn_agent -> ModuleCapabilityStep(module.sub_agent/sub_agent.spawn) -> ExecutionRuntimeSubAgentModuleBridge -> SubAgentOrchestrationModule -> child run -> toolResults[] 回流`；live 自主触发是预冻结观察矩阵，用于记录真实模型在固定任务、固定模型协议 cell 与固定每格轮数下是否自主请求 `spawn_agent`。
- live 观察矩阵的提示词本体只允许描述问题域与交付要求，不得包含 agent、子任务、并行、委托、派生、拆分、协作、执行轨道、`spawn_agent` 等方法诱导词。若提示词含方法诱导，本轮 live 观察证据无效。
- live 观察矩阵必须在执行前冻结任务、模型协议 cell 和每格轮数，默认以 `3 tasks x 3 model cells x 3 runs` 形成 27 次观察；专门行为研究可显式调整 N，但结果必须记录实际 N。执行中不得因观察到或未观察到 `spawn_agent` 而早停、追加/删除样本或改写判读规则。
- live 观察矩阵的结论分三类：任一计划 run 观察到自主 `spawn_agent` 且回流成功时，产品级自主 Sub-Agent 证据成立；完整矩阵未观察到时，结论为“机制可用，当前任务/模型/tool surface 下未观察到自主触发”，这是有效工程观察但不能冒充产品级自主 Sub-Agent 通过；矩阵未完整执行、证据不足、提示词含方法诱导或无法确认 provider request 中 tool surface 时，本轮 live 观察无效，需要按同一冻结协议重跑。
- 子 `GovernanceEnvelope` 经断言确认 ⊆ 父：工具/模块/副作用上限/human gate 任一越界派生时 fail-closed。
- 第一版三个 admission 结构闸门各有 fail-closed 负例：`maxSpawnDepth` 超限（孙 agent 被拒）、`maxFanoutPerAgent` 超限、`maxTreeNodes` 超限，分别命中对应 failure code，且**未发起**越界子 run。
- `maxTreeNodes` / `maxFanoutPerAgent` 的单调性有专项断言：子 run 终止后再 spawn，累计计数**不回退**——"终止一个再 spawn 一个"不得绕过累计上限（`OnChildTerminated` 只回收 `ActiveAgents`，不回收单调计数）。
- 串行版（`MaxConcurrentAgents = 0`）有专项断言：连续 spawn 多个子 agent 不会因 `ActiveAgents` 累积而被 `concurrency_exceeded` 误拒，且第一个子 agent 不被误拒——证明并发记账在串行版完全不参与 admission。
- 审批继承（§6.3）有断言：父 envelope 带 `ApprovalIds` 时，子继承的 approval ⊆ 父；子不能凭空获得新 approval，也不能用继承 approval 覆盖父本不允许的副作用等级。
- approval 显式缩窄有断言：未声明 requested approval 时继承父 approval 全集；声明 requested approval 时只取 `父 ∩ requested`，不能新增。
- child run id 稳定派生有断言：同一个 parent lineage 与相同 sibling index 在新 ledger 中得到同一个 child run id；同一父的不同 sibling index 得到不同 child run id。
- 子 run fail / blocked 时，父收到结构化 `SubAgentRunResult{ Status, Failure }`，父状态机不被子失败污染（父可继续决策）。
- 整棵 agent 树可从根 runId + `SubAgentLineage` + 各子 `ReplaySummaryRef` 完整复盘 spawn 关系。
- `ISubAgentModule` 不直接构造 `StableKernelCore` / `StageGraph` / `RuntimeStep`，只消费 `IKernelRuntimeExecutionLoop`（源码边界守护）。
- token/time 预算不参与 `spawn_agent` admission 的结构判定；同时验证子 run 启动后仍受自身预算、timeout、cancellation 与工具回边预算约束。
