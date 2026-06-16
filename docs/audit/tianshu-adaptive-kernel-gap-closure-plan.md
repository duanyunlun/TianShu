# TianShu 自适应内核补全计划清单

## 1. 文档定位

本文是第二次自适应内核可行性验收后的补缺计划，供进入实现前审核。它不宣称当前系统已经达到 `feasible-controlled`，也不替代 `docs/tianshu-architecture-spec.md`、`docs/architecture/tianshu-kernel-core-loop-design.md` 或 `docs/tianshu-implementation-tracker.md`。

当前有效验收结论为 `feasible-with-limits`。该结论应理解为“接线缺口”，不是“架构不可行”，也不完全是“外部环境条件不足”：

- `harness-only`：AI 生成 StageGraph / patch / recovery / checkpoint / context policy 候选，并通过 typed mapper、KernelValidator、StageGraphInterpreter dry-run 的路径已经有可行性信号。
- `adaptive-limited`：token usage 不是完全没有产生，而是验收 runner 读取 stdout JSONL，当前 token 通知在 AppServer / transcript artifact 通道；同时现有 `BuildTokenUsagePayload` 是按文本长度估算的 token，不是 provider 返回的真实 usage。因此 C full matrix / D 的成本口径仍不能通过。
- `runtime-limited`：`StableKernelCore` 生成 `ExecutionPlan` 后尚未驱动 `IExecutionRuntime.ExecuteAsync`，产品主路径不能证明 adaptive StageGraph live execution。

本计划的目标是把上述限制收敛为可实施、可验收、可回滚的工作清单。审核通过后，实施前应把确认后的阶段任务同步到 `docs/tianshu-implementation-tracker.md` 的“正在做”。

## 2. 总目标

补齐两条闭环，并把两类统计源分开：

1. **产品执行闭环**：`CoreIntent -> StableKernelCore -> ExecutionPlan -> IExecutionRuntime.ExecuteAsync -> RuntimeStep bridge -> ExecutionRunResult -> KernelTrace / HostProjection`。
2. **可观测指标接线闭环**：`AppServer notification / transcript artifact / provider usage -> CLI stdout 或 runner artifact reader -> token usage / model-call / latency / cost attribution -> RunMetrics -> C full matrix -> D strategy gate`。

两类统计源不能混用：

- **候选生成统计**：C 阶段 LLM 生成 StageGraph / patch / recovery / context policy 时产生的模型调用统计。它属于 runner / CLI 调用模型生成候选的成本，归因到 `taskId / candidateKind / attemptIndex / reviseRound`。
- **Runtime 执行统计**：P3 之后固定或候选 StageGraph 被解释为 `ExecutionPlan` 并执行 RuntimeStep 时产生的 provider/tool/module 调用统计。它属于 Execution Runtime / provider bridge 的成本，归因到 `runId / graphId / stageId / stepId / executionId`。

C full matrix 的成本门槛使用候选生成统计；P3-P5 的 live execution 成本使用 Runtime 执行统计；D strategy evolution 必须能同时说明候选生成成本与 runtime 执行成本如何计入 evaluation。

补齐后才允许重新评估是否能从 `feasible-with-limits` 升级到 `feasible-controlled`。升级前不得把 adaptive StageGraph 接入 CLI/AppHost 默认产品主路径。

## 3. 不做范围

- 不直接让 AI 修改 Stable Kernel Core。
- 不绕过 `KernelValidator`、`TianShuExecutionRuntime.ValidateStep`、human gate、permission envelope 或 side effect ceiling。
- 不用已安装 `C:\Users\Example\.tianshu\bin\tianshu.exe` 作为验收基底。
- 不把 C smoke 的 10/10 合法性当成 C full matrix 或 D 通过。
- 不以旧 AppHost turn loop 的 steer / interrupt / resume / subagent 能力替代 adaptive StageGraph runtime loop 证明。
- 不在 token/cost 不可归因时进行 strategy promotion。

## 4. 涉及项目

| 项目 | 角色 | 本计划中的职责 |
| --- | --- | --- |
| `src/Core/TianShu.Kernel.Abstractions` | Kernel 抽象边界 | 定义 Kernel run、trace、execution handoff 所需的抽象返回与追踪字段。 |
| `src/Core/TianShu.Kernel` | Stable Kernel Core | 继续负责 intent / graph / plan 验证与解释，不直接依赖 runtime 实现。 |
| `src/Core/TianShu.RuntimeComposition` | 组合层 | 新增或扩展 Kernel -> Runtime live execution 编排入口。 |
| `src/Execution/TianShu.Execution.Runtime` | Runtime 执行层 | 执行 `ExecutionPlan` / `RuntimeStep`，输出 step result、diagnostics、runtime trace、metrics event。 |
| `src/Hosting/TianShu.AppHost` | AppHost 宿主 | 只作为产品宿主和 transport/composition root，不内嵌 Kernel 编排逻辑。 |
| `src/Hosting/TianShu.AppHost.Tools.Runtime` | 当前 turn loop 事实来源 | 作为旧 turn loop、token notification 和 A2 能力迁移参考，不能继续成为终态编排中心。 |
| `src/Presentations/TianShu.Cli` | 当前唯一优先消费层 | 通过 Host Gateway / runtime client 消费统一入口；本轮只考虑 CLI，不扩展 VSIX/ConfigGUI 产品路径；需要决定 token 通知进入 stdout JSONL 还是 artifact reader。 |
| `tools/Run-TianShuAdaptiveKernelFeasibility.ps1` | 验收 runner | 消费统一 metrics event 或 transcript artifact，执行 C full matrix / D gate。 |
| `tests/TianShu.Kernel.Tests` | Kernel 验证 | 覆盖 StableKernelCore、StageGraphInterpreter、trace handoff、fail-closed。 |
| `tests/TianShu.Execution.Runtime.Tests` | Runtime 验证 | 覆盖 ExecutionPlan / RuntimeStep / bridge / metrics event。 |
| `tests/TianShu.Cli.Tests` | CLI 边界验证 | 覆盖 CLI 不越界、不直接构造 core/runtime 对象。 |
| `docs/audit/evidence/adaptive-kernel-*` | 验收证据 | 保存补缺后的重跑证据。 |

## 5. 阶段清单

### P0：冻结补缺基线

目标：把当前 `feasible-with-limits` 作为补缺起点，避免实现过程中混入旧证据或旧产品路径假设。

清单：

- [ ] 确认当前分支、HEAD、工作区状态。
- [ ] 确认第二次验收最终报告仍为 `feasible-with-limits`。
- [ ] 确认不使用用户级已安装 CLI。
- [ ] 确认本轮实现只面向 CLI 消费路径。

验收：

- [ ] 后续证据目录与第二次验收目录分开，例如 `docs/audit/evidence/adaptive-kernel-gap-closure/`。

实施启动动作：

- [ ] 本计划审核通过后，再将确认后的实施清单同步为 tracker “正在做”任务。

### P1：定义 Kernel -> Runtime live execution 组合入口

目标：新增组合层入口，把 `StableKernelCore` 返回的 `ExecutionPlan` 交给 `IExecutionRuntime.ExecuteAsync`，但不让 Kernel 直接依赖 runtime 实现。

建议归属：

- 新增或扩展：`src/Core/TianShu.RuntimeComposition`
- 测试：`tests/TianShu.Execution.Runtime.Tests` 或新增 runtime composition 聚焦测试

建议接口骨架：

```csharp
namespace TianShu.RuntimeComposition;

public interface IKernelRuntimeExecutionLoop
{
    Task<KernelRuntimeExecutionResult> RunAsync(
        CoreIntent intent,
        KernelRuntimeExecutionOptions options,
        CancellationToken cancellationToken);
}

public sealed record KernelRuntimeExecutionOptions(
    KernelRunOptions KernelOptions,
    ExecutionRuntimeContext RuntimeContext,
    bool ExecuteRuntimePlan);

public sealed record KernelRuntimeExecutionResult(
    KernelRunResult KernelResult,
    ExecutionRunResult? RuntimeResult,
    KernelRuntimeExecutionDisposition Disposition,
    KernelTraceId? KernelTraceId,
    string? RuntimeTraceRef,
    string? DiagnosticsRef);

public enum KernelRuntimeExecutionDisposition
{
    Unspecified = 0,
    ApprovalOnly = 1,
    KernelRejected = 2,
    RuntimeCompleted = 3,
    RuntimeBlocked = 4,
    RuntimeFailed = 5,
}
```

清单：

- [ ] 定义组合层入口，不把它放进 `TianShu.Kernel`。
- [ ] `ExecuteRuntimePlan=false` 时保持当前 dry-run/approval-only 行为。
- [ ] `ExecuteRuntimePlan=true` 时要求 `KernelRunResult.ExecutionPlan` 不为空。
- [ ] 调用 `IExecutionRuntime.ExecuteAsync(plan, context, cancellationToken)`。
- [ ] 将 `ExecutionRunResult` 与 `KernelRunResult` 关联到同一 run / graph / intent。
- [ ] `KernelRuntimeExecutionResult` 必须包含 `Disposition`，区分 approval-only、kernel rejected、runtime completed、runtime blocked 和 runtime failed。
- [ ] runtime blocked 时不得伪造 Kernel completed。
- [ ] Kernel validation rejected 时不得调用 runtime。
- [ ] RuntimeStep governance rejected 时必须返回 blocked，并保留可审计 reason。

验收：

- [ ] 测试证明 Kernel rejected 时 runtime fake 未被调用。
- [ ] 测试证明 fixed StageGraph 能生成 `ExecutionPlan` 并调用 runtime fake。
- [ ] 测试证明 runtime blocked 会进入 fail-closed 结果。
- [ ] 测试证明 `Disposition` 与 `RuntimeResult` 是否为空一致，避免 P4 replay 无法区分 dry-run、kernel rejected 和 runtime blocked。
- [ ] 测试证明 result 中能关联 `runId`、`intentId`、`graphId`、`planId`、`executionId`。

### P2：补候选生成与 Runtime 执行的统计接线

目标：先修正 CLI / runner 的候选生成 token 通道接线，再补 Runtime/provider bridge 的真实执行统计。当前 `token_usage_event_missing` 的直接原因不是 AppHost 完全没发 token，而是 runner 只读 stdout JSONL；`thread/tokenUsage/updated` 在 AppServer 通知 / transcript artifact 侧，`ChatJsonlOutputWriter` 当前只输出 `{ type, text, partial }`。即使接通该通道，当前 `BuildTokenUsagePayload` 也是文本长度估算，不能作为 D 的真实成本依据。

P2 拆成两条子线：

- **P2A 候选生成统计最小接线**：runner / CLI 调用模型生成 StageGraph / patch 时的 token、latency、model-call。C full matrix 先依赖这条线。第一步允许 token 为 `estimated=true`，但必须端到端保留该标记，不得在报告里冒充 provider 真实 usage 或真实成本。
- **P2B Runtime 执行统计**：`TianShuExecutionRuntime` 在 provider bridge / tool bridge / module bridge 调用时产出的 metrics event。P3-P5 live execution 和 D 依赖这条线。P2B 才要求 provider bridge 侧真实 usage 归因。

建议归属：

- 契约或抽象：`src/Contracts/TianShu.Contracts.Execution`
- Runtime 实现：`src/Execution/TianShu.Execution.Runtime`
- AppHost 当前 token 投影迁移参考：`src/Hosting/TianShu.AppHost.Tools.Runtime/KernelTurnStageNotificationRuntime.cs`
- CLI 输出 / artifact 读取：`src/Presentations/TianShu.Cli`
- 验收 runner：`tools/Run-TianShuAdaptiveKernelFeasibility.ps1`

建议契约骨架：

```csharp
namespace TianShu.Contracts.Execution;

public sealed record RuntimeMetricsEvent(
    string EventId,
    string RunId,
    string ExecutionId,
    string PlanId,
    string GraphId,
    string? StageId,
    string? StepId,
    string ModelId,
    int AttemptIndex,
    int? ReviseRound,
    TokenUsageSnapshot TokenUsage,
    RuntimeCostSnapshot Cost,
    int ModelCallCount,
    TimeSpan Latency,
    IReadOnlyList<string> MissingReasons);

public sealed record CandidateGenerationMetricsEvent(
    string EventId,
    string TaskId,
    string CandidateKind,
    int AttemptIndex,
    int? ReviseRound,
    string ModelId,
    TokenUsageSnapshot TokenUsage,
    RuntimeCostSnapshot Cost,
    int ModelCallCount,
    TimeSpan Latency,
    IReadOnlyList<string> MissingReasons);

public sealed record TokenUsageSnapshot(
    bool Available,
    string? MissingReason,
    bool Estimated,
    long? InputTokens,
    long? CachedInputTokens,
    long? OutputTokens,
    long? ReasoningOutputTokens,
    long? TotalTokens,
    string Source);

public sealed record RuntimeCostSnapshot(
    bool Available,
    string? MissingReason,
    decimal? EstimatedCost,
    string? Currency,
    string? PriceModelVersion);
```

清单：

- [ ] 先完成 P2A 最小接线：让候选生成 token 从 CLI/AppServer artifact 或 stdout event 被 runner 稳定读取，保留 `estimated` / `source` 字段；不要先扩展成完整 telemetry 平台。
- [ ] 明确 runner 的 token 读取优先级：标准 stdout metrics event、`transcript-records.jsonl` / artifact event、兼容旧 `thread/tokenUsage/updated`。
- [ ] 让 `chat --script --protocol jsonl` 能输出或可定位 token usage artifact；不得只输出 stdout/stderr 文本帧后让 runner 失去通知事件。
- [ ] runner 不得只扫描 stdout JSONL；若 CLI 已写 `transcript-records.jsonl`，runner 必须能从 run artifact 中读取。
- [ ] 统一 token usage 字段命名。
- [ ] token 缺失时输出机器可读 `missingReason`，不得静默为 0。
- [ ] 区分 `estimated=true` 的文本长度估算 token 与 `estimated=false` 的 provider usage。
- [ ] C legality / C full matrix 可以使用 `estimated=true` 的候选生成 token 做趋势诊断和缺口解除；但不得将其计为真实成本，不得用于 D promotion。
- [ ] 候选生成的 `modelCallCount` 由 runner 记录每次候选生成调用，归因到 `taskId / candidateKind / attemptIndex / reviseRound`。
- [ ] Runtime 执行的 `modelCallCount` 必须由 `TianShuExecutionRuntime` 在每次 provider bridge 调用时发 metrics event，runner 只聚合，不硬猜。
- [ ] latency 至少有 run 级、step 级两个粒度。
- [ ] cost 只在非估算 token usage 和 price model 都可用时 available。
- [ ] 候选生成 metrics 必须可归因到 `taskId / candidateKind / attemptIndex / reviseRound?`。
- [ ] Runtime 执行 metrics 必须可归因到 `runId / graphId / stageId? / stepId? / executionId`。
- [ ] Runner 同时支持 `RuntimeMetricsEvent`、artifact 读取和兼容读取现有 `thread/tokenUsage/updated`，但通过标准事件后应优先使用标准事件。

验收：

- [ ] CLI JSONL stdout 或 runner artifact reader 能稳定拿到 `thread/tokenUsage/updated` 或标准 metrics event。
- [ ] C runner 能输出候选生成 metrics，并区分 first attempt / revise attempt。
- [ ] P2A 允许输出 `estimated=true`；验收报告必须显式写明该 token 只是候选生成诊断值，不能作为 provider usage 或 D 成本证据。
- [ ] Runtime fake provider bridge 能输出 Runtime 执行 metrics，并标记 `estimated=false`。
- [ ] 当前文本长度估算 payload 必须标记 `estimated=true` 或等价 source，不能被 D 当作 provider usage。
- [ ] 无 token provider 输出 `token_usage_event_missing` 或等价标准 missing reason，且能区分“通道未接通”和“provider 未返回 usage”。
- [ ] price model 缺失只阻断 D，不阻断 C legality。
- [ ] C smoke 输出不再出现不可解释的 token 缺失。

### P3：补 fixed StageGraph live execution 最小产品路径

目标：先跑固定 StageGraph，不接 AI 自适应；证明 Kernel -> Runtime live execution 可以在 CLI 消费链路外部或受控命令中完成。本阶段使用 Runtime 执行统计，不使用 C 阶段候选生成统计。

建议路径：

- 优先新增受控验收入口，不直接替换 CLI 默认 chat path。
- CLI 只作为 consumer，不构造 `CoreIntent`、`StageGraph`、`RuntimeStep`。

清单：

- [ ] 定义一个固定 turn `CoreIntent` fixture。
- [ ] 通过组合层入口生成并执行 `ExecutionPlan`。
- [ ] RuntimeStep 至少覆盖 `ModuleCapabilityStep` 和一个 provider/tool 相关 step 的受控 fake。
- [ ] 输出 `KernelRunResult`、`ExecutionRunResult`、metrics event、trace refs。
- [ ] Host projection 只消费 result / refs，不读取 runtime 内部对象。
- [ ] 该路径默认关闭 AI graph/patch 生成。

验收：

- [ ] 不依赖用户级安装 CLI。
- [ ] live 输出能证明 `IExecutionRuntime.ExecuteAsync` 被实际调用。
- [ ] trace 能关联 Kernel plan 和 runtime step result。
- [ ] CLI 测试证明 CLI 不直接 new `StableKernelCore`、不直接 new `TianShuExecutionRuntime`、不直接构造 `RuntimeStep`。

### P4：补 trace replay 全链路关系

目标：让一次 fixed graph live run 可以复盘完整链路，而不是只有离散 trace ref。

清单：

- [ ] 定义 replay 需要的最小事件集合。
- [ ] Kernel trace 包含 intent accepted、graph selected、graph validated、execution plan created、checkpoint proposal/evaluation refs。
- [ ] Runtime trace 包含 plan started、step started、step completed/blocked、module/provider/tool result。
- [ ] Metrics event 引用 run/plan/step。
- [ ] replay summary 能重建 graph -> stage -> runtime step -> result -> metrics。
- [ ] blocked step 的 failure code 可进入 replay summary。

验收：

- [ ] 测试输入一次 run 的 trace/events，输出结构化 replay summary。
- [ ] replay summary 缺少任何关键关联时 fail-closed。
- [ ] S5 的 `trace replay = partial-trace-only` 可以升级为 `live-pass-fixed-graph`。

### P5：补 checkpoint / recovery live 状态机

目标：从 trace-only 升级到 fixed graph live evidence，先不做 AI 生成 recovery 自动晋升。

清单：

- [ ] 明确 checkpoint 创建点：graph validated、plan approved、step completed、step blocked。
- [ ] 明确 recovery proposal 只作为候选，必须经 Stable Kernel Core 验证。
- [ ] Runtime blocked 时触发受控 recovery path，而不是直接继续执行。
- [ ] human gate 缺失时 recovery promotion fail-closed。
- [ ] rollback 能回到稳定 fixed graph 或上一 checkpoint。

验收：

- [ ] 测试固定失败 step 生成 checkpoint/recovery trace。
- [ ] 测试无 checkpoint 时 recovery fail-closed。
- [ ] 测试 recovery 越权时 validator 拒绝。

### P6：重跑 C full matrix

目标：在 P2A 候选生成统计可用后，重新启动 C full matrix，验证 AI graph/patch 候选稳定性。C full matrix 本身仍是“LLM 生成候选 -> mapper -> validator -> interpreter dry-run”的隔离实验，不执行 RuntimeStep，不产生 Runtime 执行统计。

前置条件：

- P2A 通过：候选生成 token/model-call/latency 可归因。
- P1-P4 不是 C legality full matrix 的硬前置；只有当本轮 C 验收追加“候选 graph live execution”时，才需要 P1-P4。
- token usage 标准事件、CLI stdout 转发或 runner artifact reader 至少一个通道可用；若 token 为估算值，只能用于 C legality / C full matrix 的候选生成诊断，不能用于 D 成本判断。
- C acceptance profile 已预注册。

清单：

- [ ] 每类 C task 至少 5 次。
- [ ] 每次最多 3 轮 revise。
- [ ] 只把结构化 rejection reason 返回模型。
- [ ] 统计首轮合法率、最终合法率、平均 revise、震荡率、越权率。
- [ ] 统计候选生成 latency、token、cost、model-call 增量。
- [ ] 若追加 live execution 子组，必须单独统计 Runtime 执行 latency、token、cost、model-call，不能混入候选生成统计。
- [ ] 高风险越权必须全部被 validator 拒绝。
- [ ] 合法 graph 不能全部退化为单个 `core_loop` stage。

验收：

- [ ] 默认 acceptance profile 最终合法率达到预注册阈值。
- [ ] 平均 revise 不超过预注册阈值。
- [ ] 候选生成 token/model-call 可归因；允许 token 为 `estimated=true`，但必须标注；cost 只有在 provider usage 与 price model 都可用时才可用。
- [ ] C full matrix 失败时不得启动 D。

### P7：重启 D strategy evolution

目标：在 C full matrix 通过后，验证策略候选能否受控 trial、promotion、rollback。

前置条件：

- P6 通过，且 token usage 不是文本长度估算值。
- P2B 通过，Runtime 执行 provider bridge 的 metrics event 可用。
- price model 已提供并绑定 modelId、币种、生效日期和单价。
- effect-size、promotion gate、rollback gate、human gate 已预注册。

清单：

- [ ] 定义 success/pass-rate 的统计规则。
- [ ] 定义 latency/token/cost/model-call 的统计规则。
- [ ] 定义质量评分来源、量表和最小有意义差异。
- [ ] strategy candidate 必须关联 risk profile、rollback plan、evaluation plan。
- [ ] 缺 evidence、缺 human gate、高风险、收益不足时 promotion fail-closed。
- [ ] promoted/trial strategy 可基于失败 evidence rollback。

验收：

- [ ] D 不在样本不足时输出 automatic promotion。
- [ ] D promotion 证据可回放。
- [ ] D rollback 证据可回放。
- [ ] 通过后才允许重新评估 `feasible-controlled`。

### P8：A2 steer / interrupt / resume / subagent 专项

目标：不要把 A2 混进 C/D 主线里偷渡通过；在 fixed graph live execution 后单独证明或降级。A2 必须按依赖拆分，不允许把所有项都放在 P3 后并行。

清单：

- [ ] steer：P3 后可验证，证明 late input 能映射为受控 HostInteractionStep 或对应 CoreIntent。
- [ ] interrupt：P5 后验证，证明中断能暂停 runtime plan，并记录 checkpoint。
- [ ] resume：P5 后验证，证明 resume 能从 checkpoint 或 pending interactive state 继续。
- [ ] subagent：证明 spawn / wait / result aggregation 能进入 runtime loop，或明确作为独立 module capability 降级。
- [ ] 对照第一次验收 `baseline-task-06` 5/5 `a2_delta_subagent_unavailable`，不能遗漏。

验收：

- [ ] 四项分别输出 `live-pass`、`trace-only`、`blocked-missing-runtime` 或 `blocked-unsafe`。
- [ ] 任一项未通过时，不得宣称完整 agent loop 可行。

### P9：旧 turn loop 与新 adaptive loop 并存/迁移边界

目标：明确 `TianShu.AppHost.Tools.Runtime` 当前 turn loop 与新增 `IKernelRuntimeExecutionLoop` 的关系，避免形成两条长期并行且互相绕开的产品主路径。

清单：

- [ ] 标记旧 turn loop 负责的现有能力：provider streaming、tool continuation、steer、interrupt、resume、subagent、terminal projection。
- [ ] 标记新 adaptive loop 的目标能力：StageGraph -> ExecutionPlan -> RuntimeStep live execution、trace replay、metrics attribution。
- [ ] 明确短期并存策略：旧 loop 保持当前产品默认路径，新 loop 只作为受控验收入口。
- [ ] 明确迁移策略：每迁移一类能力，必须有 parity 测试和回退策略。
- [ ] 明确不迁移策略：若某能力暂不迁移，必须标注为 product-path delta，不能在 `feasible-controlled` 中偷算通过。
- [ ] 明确 CLI 默认路径架构守护：CLI 默认执行路径必须经过 Host Gateway -> Control Plane，再进入 Kernel / Runtime；CLI 不得直接调用 Kernel 或 Execution Runtime 形成旁路主路径。
- [ ] 若短期只能落地一条守护，优先守住 Host Gateway / Control Plane 边界；Kernel / Runtime 直连只能作为受控验收入口或内部组合层，不得作为 CLI 默认路径。
- [ ] subagent 必须单独说明为什么旧 loop 能跑或不能跑、新 loop 能跑或不能跑，不能只记录“已有 spawn_agent 工具”。

验收：

- [ ] 文档列出旧 loop 和新 loop 的能力矩阵。
- [ ] 测试或架构守护能证明 CLI 默认路径必须经过 Host Gateway / Control Plane；若存在直接 Kernel / Runtime 调用，只能是明确标注的受控验收入口。
- [ ] 若新 loop 只作为验收入口，最终报告必须明确“尚未替换产品默认 turn loop”。
- [ ] 若要替换产品默认 turn loop，必须先完成 P1-P5、P8 对应能力和本阶段迁移 parity。

## 6. 推荐执行顺序

```text
P0 baseline freeze
  -> P1 Kernel -> Runtime execution loop
  -> P2 metrics wiring
  -> P3 fixed StageGraph live execution
  -> P4 trace replay
  -> P5 checkpoint/recovery live
  -> P8 A2 interrupt/resume/subagent dependent checks
  -> P9 old/new loop coexistence and migration boundary

P2A candidate-generation metrics
  -> P6 C full matrix
  -> P7 D strategy evolution
```

P1 到 P5 是产品执行闭环基础。P6 的 C legality full matrix 不依赖 P3 的 RuntimeStep live execution，但依赖 P2A 候选生成统计；若 P6 要追加候选 graph live execution 子组，才依赖 P1-P4。P7 依赖 P6、P2B、price model 和 rollback/human gate。P8 中 steer 可在 P3 后验证，interrupt/resume 必须在 P5 后验证。P9 必须在任何产品默认路径替换前完成。

## 7. 阶段性结论升级规则

| 状态 | 条件 |
| --- | --- |
| `feasible-with-limits` | 当前状态，harness 可行但 live execution / token 通道接线 / 真实 usage 成本 / D / 旧新 loop 迁移边界未闭合。 |
| `fixed-graph-live-pass` | P1-P5 通过，固定 StageGraph 能真实执行并可回放。 |
| `adaptive-candidate-pass` | P2A 与 P6 通过，AI graph/patch 候选在 full matrix 中达到阈值；若 token 为 `estimated=true`，只能说明候选生成统计可诊断，不能说明真实成本可控。 |
| `strategy-evolution-pass` | P7 通过，strategy trial/promotion/rollback 有统计证据。 |
| `feasible-controlled` | P1-P9 的产品路径、双统计源、C full matrix、D gate、A2 风险和旧新 loop 迁移边界均通过或有正式降级策略。 |

## 8. 实施前需要审核的问题

- [ ] 是否同意把 Kernel -> Runtime live execution 放在 `TianShu.RuntimeComposition`，而不是让 `TianShu.Kernel` 直接依赖 `TianShu.Execution.Runtime`？
- [ ] 是否同意 C legality full matrix 可在 P2A 通过后重跑；若追加候选 graph live execution 子组，才等待 fixed StageGraph live execution？
- [ ] 是否同意先修 CLI/runner token 通道接线，再区分 estimated token 与 provider usage？
- [ ] 是否同意把 C full matrix 的候选生成统计与 P3 Runtime 执行统计拆成两条 metrics source？
- [ ] 是否同意 P2A 第一阶段接受 `estimated=true` 的候选生成 token 作为 C 诊断值，但禁止用于 D 成本和 promotion？
- [ ] 是否同意 runtime metrics 采用标准事件优先，runner 兼容 transcript artifact 与旧 token event 作为过渡读取？
- [ ] 是否同意 CLI 本轮只作为消费入口，不扩展 VSIX / ConfigGUI 产品路径？
- [ ] 是否同意 CLI 默认路径必须守住 Host Gateway -> Control Plane，Kernel / Runtime 直连只能作为受控验收入口？
- [ ] 是否同意 A2 作为专项，其中 steer 可在 P3 后验证，interrupt/resume 必须在 P5 后验证？
- [ ] 是否同意新增 P9，先明确旧 turn loop 与新 adaptive loop 的短期并存和迁移边界？

## 9. 产品执行闭环最小通过口径

若只做产品执行闭环第一阶段补缺，最小可接受结果不是 `feasible-controlled`，而是：

- `StableKernelCore` 的 `ExecutionPlan` 被组合层实际交给 `IExecutionRuntime.ExecuteAsync`。
- Runtime 执行至少一个 fixed StageGraph plan。
- Runtime 输出标准 metrics event、CLI 可转发 token event，或 runner 能读取 transcript artifact；若是估算 token，必须明确标记。
- Trace replay 能重建 fixed graph 的 graph/stage/step/result/metrics 关系。
- CLI 不越界构造 core/runtime 内部对象。
- 新旧 turn loop 的并存边界已写明，不把新 loop 验收入口误报为产品默认路径替换完成。

达到这个口径后，才有资格把 fixed graph live execution 计入产品路径证据。C legality full matrix 的重跑资格由 P2A 候选生成统计决定；若 C 要追加候选 graph live execution 子组，才需要本节口径。
