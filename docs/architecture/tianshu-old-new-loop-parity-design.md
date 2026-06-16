# TianShu 旧/新 Turn Loop Parity 迁移设计

## 1. 文档定位

本文是条目 23 的实施基线，用于把旧产品 turn loop 与新 `IKernelRuntimeExecutionLoop` 固定 StageGraph 路径逐项对齐。23.4.1 后 CLI `send` 默认已进入新 Kernel→Runtime loop；36 起旧 AppHost turn loop 只作为历史迁移输入存在，不再是旧显式入口或 fallback。

本文只记录当前代码事实、迁移矩阵和后续 parity 验收要求，不替代 `docs/tianshu-architecture-spec.md`、`docs/architecture/tianshu-builtin-stage-graph-design.md` 或 `docs/architecture/tianshu-execution-runtime-design.md`。

## 2. 涉及项目代码

| 项目 | 当前角色 | 迁移定位 |
| --- | --- | --- |
| `src/Hosting/TianShu.AppHost.Tools.Runtime` | 旧产品 turn loop 的主要实现所在地，负责 turn start、后台调度、provider streaming、tool continuation、steer、interrupt、terminal projection 等。 | 仅作为历史迁移输入；不得继续作为当前产品 turn 路径。 |
| `src/Execution/TianShu.Execution.Runtime` | 既包含新 `IExecutionRuntime` / `TianShuExecutionRuntime`，也包含旧 Stage Executor / TurnExecutionRunner / checkpoint 投影支持。 | 新路径负责执行 `RuntimeStep`；旧 Stage Executor 支持只作为历史迁移输入。 |
| `src/Core/TianShu.RuntimeComposition` | 新 Kernel -> Runtime 组合入口，包含 `IKernelRuntimeExecutionLoop` / `AdaptiveRuntimeExecutionLoop` / replay projector / CLI opt-in bridge / 配置驱动 provider module。 | 作为新 turn loop 的执行主线。 |
| `src/Core/TianShu.Kernel` | 新 `StableKernelCore`、`StageGraphInterpreter`、固定图选择与验证；同时仍保留旧 Stage Registry 相关组合。 | 新路径的 Kernel 主体；旧 Stage Registry 仅作为历史迁移输入。 |
| `src/Contracts/TianShu.Contracts.Kernel` | 新 `CoreIntent`、`StageGraph`、`StageNode`、`StageResult`、Kernel trace 等契约。 | 新固定 StageGraph 的 source-of-truth。 |
| `src/Contracts/TianShu.Contracts.Execution` | 新 `ExecutionPlan` / `RuntimeStep` / step result 契约。 | 新 Runtime 执行边界。 |
| `src/Contracts/TianShu.Contracts.Orchestration` | 旧 `StageDefinition`、`StageContextPackage`、`StageExecutionRequest`、`StageCheckpoint` 等生命周期契约。 | 作为旧路径迁移输入；不得重新成为新默认 turn 编排契约。 |

## 3. 当前旧路径事实

旧产品 turn 路径曾由 `KernelTurnExecutionAppHostRuntime` 暴露宿主入口，并由 `KernelTurnExecutionRuntimeComposition` 组装执行对象图。36 起它不再是 CLI `send` 的可触发产品路径；以下链路仅作为历史迁移输入说明：

```text
KernelTurnExecutionAppHostRuntime
  -> KernelTurnLaunchRuntime
    -> KernelTurnBackgroundSchedulerRuntime
      -> KernelTurnRunnerRuntime
        -> TurnExecutionRunner<TurnRequestContext>
          -> StageExecutorRunner<TurnRequestContext>
            -> KernelTurnModelStageRuntime
              -> KernelTurnOperationChainRuntime
                -> ResolveInput
                -> ResolveDependencies
                -> ExecuteAssistant
                -> StreamAssistantOutput
```

`KernelTurnProviderAssistantRuntime` 在 `ExecuteAssistant` 内部运行 provider-backed assistant tool loop：

```text
compose provider request
  -> stream via HTTP/WebSocket transport with retry
  -> parse response output items
  -> extract function/custom/tool_search calls
  -> execute tool calls
  -> build follow-up provider input
  -> repeat until assistant completion
```

因此旧路径不是新 `StageGraph` 的等价实现，而是一个产品级 turn loop：它用旧 Stage Executor 分派模型包裹一个模型 turn implementation，真正的多轮工具循环发生在 `KernelTurnProviderAssistantRuntime.StreamResponsesToolLoopAsync` 内部。

## 4. 旧 Stage lifecycle 契约定位

旧 lifecycle 契约当前承担的是执行入口、上下文投影和 checkpoint 投影，不是新固定 StageGraph 的终态 IR。

| 契约 / 组件 | 当前代码位置 | 当前承担的能力 | 新路径定位 |
| --- | --- | --- | --- |
| `StageDefinition` / `BuiltInStageDefinitions` | `src/Contracts/TianShu.Contracts.Orchestration` | 定义旧 lifecycle stage、route kind、allowed previous/next、executor binding。 | 迁移输入；新 turn 默认图以 `StageGraph` / `StageNode` 为准。 |
| `StageRegistryRuntimeComposition` | `src/Core/TianShu.Kernel` | 从内置或配置 stage 定义生成 registry snapshot 和 transition diagnostics。 | 历史迁移输入；不得作为新 turn 默认编排入口。 |
| `SessionOrchestrator` | `src/Core/TianShu.Kernel` | 按旧 `StageDefinition` 和 observed state 选择一次 lifecycle stage。 | 迁移输入；新 turn 编排由 `StableKernelCore` + `StageGraphInterpreter` + `AdaptiveRuntimeExecutionLoop` 承担。 |
| `StageContextPackage` | `src/Contracts/TianShu.Contracts.Orchestration` | 表达旧 stage 执行的上下文投影包，包含 segment、artifact refs、source checkpoints、budget。 | 迁移为 `stage.prepare-context` 的输入/输出证据或 context module projection。 |
| `StageExecutionRequest` | `src/Contracts/TianShu.Contracts.Orchestration` | 把旧 stage、decision、context package、model route 绑定成一次执行请求。 | 迁移为 `CoreIntent` + approved `ExecutionPlan` / `RuntimeStep` 来源追踪。 |
| `StageCheckpoint` | `src/Contracts/TianShu.Contracts.Orchestration` | 记录旧 stage 完成、失败、中断后的可审计 checkpoint。 | 迁移为 Kernel checkpoint / Runtime state commit / Host projection 组合证据。 |
| `TurnExecutionDispatchContext` | `src/Execution/TianShu.Execution.Runtime` | 将旧 Stage Executor dispatch plan 投影到 AppHost turn context。 | 历史迁移输入；新路径应使用 `KernelRuntimeExecutionResult` / replay summary / trace ref。 |

## 5. 旧路径能力盘点

| 能力 | 旧路径当前实现 | 关键代码 | 新路径目标 | 当前状态 |
| --- | --- | --- | --- | --- |
| turn start / background scheduling | 校验 thread、archive、输入长度，构造 `TurnRequestContext`，登记 active turn，调度后台任务。 | `KernelTurnLaunchRuntime`、`KernelTurnBackgroundSchedulerRuntime` | Host Gateway / Control Plane 生成受治理 `CoreIntent(Turn)`，新 loop 执行并返回 typed projection。 | 36 起 CLI `send` 只进入 `KernelRuntimeTurnLoopBridge`；旧路径仅作为历史迁移输入。 |
| 旧 Stage Registry / lifecycle stage 选择 | 从 `StageDefinition` registry 和 observed state 选择旧 lifecycle stage，再绑定 Stage Executor。 | `StageRegistryRuntimeComposition`、`SessionOrchestrator`、`StageExecutorDispatcher` | `StableKernelCore` 选择 `graph.turn.default`，`StageGraphInterpreter` 解释 typed `RuntimeStep`。 | 新 Kernel path 已成为 CLI `send` 唯一路径；旧 Stage Registry 仅作为历史迁移输入。 |
| context slicing / context package | 旧路径在 provider request 前裁剪 thread history、current input、overlay segments，并生成 slicing diagnostics。 | `KernelTurnProviderAssistantRuntime`、`ContextSlicing/*` | `stage.prepare-context` 产出可追踪 context evidence，Context module 提供裁剪实现。 | 32 已冻结新路径口径：`ExecutionRuntimeContextPolicyBridge` 只消费 Kernel 批准的 `ApprovedContextPolicy`，输出 provider-neutral input 与 `ContextPolicyApplicationReport`；Provider Module 不得重新裁切上下文。旧 AppHost slicing diagnostics 不做逐字段同构，正式降级为 typed context slicing summary。 |
| provider request composition | 按 provider wire API、model、native tools、reasoning/text options 组合 Responses request。 | `KernelResponsesRequestCompositionRuntime` | `ModelInvocationStep` 经 `ExecutionRuntimeProviderBridge` 调用 provider module。 | 新 registered bridge 已跑通；28 已用当前源码、隔离 `TIANSHU_HOME`、无显式 `--config` 跑通 `responses` wire-api（`openai/gpt-5.5`，当前配置 endpoint）与 `openai_chat_completions` wire-api（`openai-compatible/openai-compatible-default`）；30A 已补 RuntimeComposition 级 request composition 测试，覆盖 Responses 的 model/input/instructions/tools/reasoning/text/service_tier/trace header，Chat Completions 的 tool replay request 组合，以及同一显式 openai-compatible Chat Completions 配置下的旧/新用户可见终态 parity。 |
| provider streaming / retry / transport | HTTP/WebSocket stream、retry、idle timeout、W3C trace、stream item processing。 | `KernelResponsesHttpStreamTransportRuntime`、`KernelResponsesWebSocketStreamTransportRuntime`、`KernelResponsesStreamProcessingRuntime` | Provider module / provider bridge 输出统一 provider events、usage、trace。 | P23-C 已接通配置驱动 HTTP/SSE provider module registered path；30A 已补 HTTP/SSE `traceparent`、turn metadata、assistant delta、Anthropic `content_block_delta.delta.text`、reasoning filter、usage metrics、retry/attempt timeout 与 Chat tool-call 分片合并测试。当前细粒度 idle timeout 仍以 provider bridge 单次 attempt timeout 覆盖，未迁移旧 AppHost 的逐事件 idle timer；WebSocket/Realtime 不进入 30 阶段关闭条件。 |
| tool continuation | 从 provider output items 提取 function/custom/tool_search call，执行工具并构造下一轮 provider input。 | `KernelResponsesToolContinuationRuntime`、`KernelResponsesFollowUpInputRuntime` | `model-reason -> tool-exec -> model-reason` 回边，按 `toolRequests[]` 物化 `ToolInvocationStep` 并回流 `tool_call + tool_result` canonical provider input。 | P23-E 已接通 provider directive -> 只读 filesystem 工具 -> tool result 回流下一轮 provider input；30A 起 RuntimeComposition 必须成对注入 `ToolCallProviderInputItem` 与 `ToolResultProviderInputItem`，Responses 投影为 `function_call` + `function_call_output`，Chat Completions 投影为 assistant `tool_calls[]` + `tool` message；workspace write 仅验收 `write`，只能在 CLI 显式审批态下进入真实 tool bridge；高风险工具 parity 待补。 |
| built-in tool execution | 通过 `KernelToolRegistry` 和各 AppHost runtime support 执行 filesystem、shell、apply_patch、MCP、agent job、artifact 等工具。 | `KernelToolRegistry`、`KernelToolExecutionAppHostRuntime`、各 `Kernel*RuntimeSupport` | Module Plane 工具实现 + `IExecutionRuntimeToolBridge`；sub-agent 使用 `ModuleCapabilityStep` + module bridge。 | 只读 filesystem 工具已迁移到新 loop 默认注册表；`write` 作为 workspace write 工具只允许在 human gate / explicit approval 边界下注册，且输入路径必须是 workspace-relative；`apply_patch` 虽已存在于 `TianShu.Tools.FileSystemMutating` 模块，但未进入 CLI 新 loop allow-list，不属于 P23-E 已验收能力；shell、MCP、artifact 等继续按独立工具治理迁移。agent job 不进入当前 provider-directed tool surface，只保留 ControlPlane / RuntimeSurface typed 命令与投影。模型自主 sub-agent 由 38 专项作为受治理 `spawn_agent` 能力接入。 |
| steer / follow-up input | 启动前短延迟合并 steer；工具回合后可追加 steer user input；late steer 最多触发 operation chain 重新进入。 | `KernelTurnSteerInputRuntime`、`KernelTurnOperationChainRuntime` | Host operation / HostInteractionStep 进入 Kernel，可审计地影响后续 model input。 | 29 已补 RuntimeComposition pending steer queue 与 CLI `follow-up --kernel-runtime-loop --mode steer` 产品证据；steer 文本进入下一轮 provider input 并投影 `applied_to_model_input`。 |
| interrupt | 记录 pending interrupt，取消后台 turn CTS，flush pending interrupt response，最终投影 interrupted 状态。 | `KernelTurnInterruptRuntime`、`KernelTurnTerminalStateRuntime` | `graph.interrupt.default` 物化 `HostInteractionStep(interrupt.cancel_tail_stream)`，再由 Host/Runtime bridge 执行产品取消。 | 29 已补 in-process CTS cancellation、file-backed active-run index / cancel signal 与 CLI 产品命令证据；interrupt 仍保持 `HostMutation`。 |
| resume / restored draft | 旧 CLI/AppHost 路径依赖 thread state、active turn、restored composer draft、pending steer 队列等产品状态。 | CLI consumer tests、thread state/projection 相关运行时 | `graph.resume.default` 物化 `HostInteractionStep(resume.from_checkpoint)`，checkpoint 重入由后续 Runtime/Host bridge 实现。 | 29 已补 HostControl checkpoint store、缺失/thread mismatch fail-closed、pending steer queue 重入与 CLI `--checkpoint-ref` 产品命令证据。 |
| terminal projection | 持久化 completed/failed/interrupted turn、rollout、thread status、turn/completed notification；可附加 stage checkpoint。 | `KernelTurnTerminalStateRuntime`、`KernelTerminalTurnProjectionCommitRuntime` | `StateCommitStep` / `DiagnosticStep` / Host Gateway projection 从新 replay/trace/state 生成用户可见结果。 | 32 已关闭当前 CLI/Host typed projection 口径：CLI 继续输出 `KernelRuntimeProductTerminalProjection`，HostGateway 只消费 `ThreadProjection` 的 runtime status / usage / diagnostics / evidence 附属投影，不引用 RuntimeComposition 内部类型。旧 AppHost terminal 内部字段不要求逐字段同构。 |
| diagnostics / turn log / rollout | 写 turn log、provider request diagnostics、context slicing diagnostics、rollout records。 | `TurnLogDiagnosticEventSink`、`KernelProviderRequestDiagnosticsRuntime`、`KernelContextSlicingDiagnosticsRuntime` | Execution Runtime metrics + diagnostics + trace refs；Host Gateway 消费 projection。 | 32 已收敛：新 loop 最小 turn log / rollout evidence 必须真实落盘或结构化 unavailable；ContextPolicy diagnostics 以 kept/dropped/source/budget summary 投影；旧 AppHost raw diagnostics 不作为正式产品契约。 |
| token usage notification | 用有效用户文本和 assistant 文本发布估算 usage。 | `KernelTurnStageNotificationRuntime.PublishTokenUsageUpdatedAsync` | Provider usage / estimated usage 经 Runtime metrics 投影到 Host。 | 32 已收敛：Runtime metrics / CLI diagnostics projection / `ThreadProjection.TokenUsage` 共同承载 usage；真实 provider usage 与 estimated usage 必须带 source/estimated，cost 缺 price model 时保持 unavailable。 |
| review / plan mode UI events | review entered、plan item start/complete、agent message start/stream/complete。 | `KernelTurnModelStageRuntime`、`KernelTurnStageNotificationRuntime` | Host Gateway / ControlPlane typed stream event 与 projection。 | 34 终局：review / plan UI 属于宿主可见 typed projection / runtime surface，不属于 provider-directed tool，也不是删除旧 AppHost 兼容路径的阻塞项。CLI 当前只承诺保留 `PlanUpdated`、workflow plan projection、review start / diff artifact 等 typed surface；旧路径 agent-message 私有事件不逐字段迁移。 |
| subagent / agent job tools | 旧路径可通过 provider native/built-in tools 触发部分 agent job、spawn/fanout 能力。 | `KernelAgentJobsAppHostRuntime`、`KernelSpawnAgentsOnCsvAppHostRuntime`、相关 tool support | agent job 仍归 ControlPlane / RuntimeSurface typed 命令与 projection；模型自主 sub-agent 归 `docs/architecture/tianshu-subagent-design.md`，经 `spawn_agent` -> `ModuleCapabilityStep(module.sub_agent / sub_agent.spawn)` 执行。 | 34 终局仍适用于 agent job：人工/宿主管理 runtime surface 不进入默认 provider tool allow-list。38 起模型自主 sub-agent 不复用旧 agent job 工具，而是受 governance 显式授予、结构闸门 admission 和 module bridge 约束；默认未授予时仍 fail-closed。 |

## 6. 旧/新 loop parity 矩阵

| Parity 项 | 新路径目标入口 | 迁移决策 | 必须验收的最小证据 |
| --- | --- | --- | --- |
| P23-A turn start 到 Kernel intent | Host Gateway / Control Plane -> `CoreIntentKind.Turn` -> `AdaptiveRuntimeExecutionLoop.RunReactiveAsync` | 23.3 已建立显式 opt-in，23.4 前不切默认 | CLI `send --kernel-runtime-loop` 通过 `src/Core/TianShu.RuntimeComposition/KernelRuntimeTurnLoopBridge.cs` 进入新 loop；Presentation 不构造 `CoreIntent` / `RuntimeStep`；默认旧路径仍作为回退。 |
| P23-B context prepare | `stage.prepare-context` + context module projection | 已迁移当前合同口径；旧诊断逐字段同构正式降级 | 同一 thread/input 下，新路径 provider input 必须包含用户输入、必要 history/tool evidence/artifact refs 的 Runtime 已物化结果；`ExecutionRuntimeContextPolicyBridge` 输出 `ContextPolicyApplicationReport`，Host projection 使用 `ThreadContextSlicingDiagnosticsProjection` 的 kept/dropped/source/budget 摘要。Provider module 不得重新裁切。 |
| P23-C provider streaming | `ModelInvocationStep` -> provider module -> provider event projection | 已迁移 registered path；28 已补最小多 provider live matrix；30A 补齐 SSE transport parity 单元证据 | `send` 默认新 Kernel→Runtime loop 通过 `KernelRuntimeTurnLoopComposition` 注册 `provider.default`，由 `ConfiguredResponsesProviderModule` 使用 resolved config 组装 provider southbound request 并解析 SSE text delta / completion / tool directive / usage；无凭据必须 fail-closed，不偷算 external live。Provider secret 读取通过可注入 env reader，默认 HTTP client 复用，runtime 结束时释放 provider/tool binding。30A live 口径固定为 `responses -> gpt-5.5`、`anthropic_messages -> claude-opus-4.8`、`openai_chat_completions -> deepseek-v4-flash`；Google provider live 暂因当前模型稳定性不足不作为阻塞，但 transport contract 仍保留单元覆盖。 |
| P23-D provider tool request | provider events -> `toolRequests[]` -> `stage.tool-exec` | 已部分迁移，需产品 parity | 断言工具请求来自 provider directive，不来自静态 allow-list 占位。 |
| P23-E tool continuation | `ToolInvocationStep` -> `toolResults[]` -> next `ModelInvocationStep` input | 已迁移 read-only；workspace write 仅验收 `write`；工具集合 parity 待补 | `model-reason` 的 provider directive 必须生成 `toolRequests[]`，`tool-exec` 必须命中真实 filesystem 工具注册表；下一轮 provider input 必须同时保留 canonical `tool_call` 与 `tool_result`，再按 wire-api 投影为 Responses `function_call_output` 或 Chat assistant/tool message 对。默认 CLI 新 loop 仍只读，`write` 必须依赖显式审批态与 governance/descriptor 双重校验，且只能接收 workspace-relative path；`apply_patch` 不在 CLI 新 loop P23-E allow-list 中，高风险工具暂不开放。 |
| P23-F steer | Host operation / HostInteractionStep -> next model input | 23.9 迁移基础输入合并；29 已补 RuntimeComposition pending steer queue 与 CLI 产品命令证据 | 新路径必须把 accepted steer/follow-up input 注入后续 `model-reason` provider input，并投影 applied/queued/unavailable 与 reason；不允许静默回旧 loop。 |
| P23-G interrupt | `graph.interrupt.default` -> `HostInteractionStep(interrupt.cancel_tail_stream)` -> product cancel bridge | 23.7 迁移 typed bridge；25.4.3f 迁移 in-process active-run cancellation registry；29 已补 workingDirectory file-backed active-run index/cancel signal 与 CLI 产品命令证据 | interrupt 必须经过 Kernel `InterruptIntent` 与 Runtime `HostInteractionStep`，终态投影 `interrupted`；命中正在执行的新 loop active run 时必须取消并投影 `activeRunCancellation.available=true` 与 `active-run://...` reference，未命中时必须投影 `active_run_not_found`。 |
| P23-H resume | `graph.resume.default` -> checkpoint lookup -> product resume bridge | 23.8 迁移 typed bridge；29 已补 RuntimeComposition HostControl checkpoint store、pending steer queue 重入与 CLI 产品命令证据 | resume 必须经过 Kernel `ResumeIntent` 与 Runtime `HostInteractionStep`；缺 checkpoint ref 必须 fail-closed，有 checkpoint ref 时投影 checkpointRef、resumeToken、replay/diagnostics 证据。 |
| P23-I terminal projection | `stage.finalize` -> `StateCommitStep` / `DiagnosticStep` -> Host projection | 23.6 迁移产品终态 projection contract；25.4.3 补齐新 loop 最小 turn log / rollout 证据引用 | 新路径必须生成 `KernelRuntimeProductTerminalProjection`，包含 stable session/thread/turn ids、assistant text、turn status、thread projection、turn log projection、rollout record projection、runtime trace refs、diagnostics refs、replay completeness 和 downgrade reasons；有 working directory 时 turn log / rollout reference 必须真实落盘。 |
| P23-J diagnostics / metrics | Runtime metrics + trace refs + replay summary | 32 已迁移当前 CLI/Host typed projection 口径；旧 raw diagnostics 正式降级 | replay 可重建 stage path、step summaries、model/tool metrics、failure codes；stage path 与 step summaries 明确分离；CLI/Host 可消费投影必须包含 metrics event ids、provider usage 汇总、cost availability、diagnostics refs、runtime trace refs、turn log ref、rollout ref 和 context slicing summary。 |
| P23-K review / plan UI | Host Gateway / ControlPlane typed projection | 已定案为宿主可见 projection / runtime surface，不作为 provider-directed tool | CLI/Sidecar 可以消费 `PlanUpdated`、workflow plan projection、review start 与 diff artifact typed surface；不迁移旧 AppHost 私有 agent-message start/stream/complete 字段。P23-K 不阻塞旧 AppHost 兼容路径删除。 |
| P23-L subagent / agent jobs | agent job 继续作为 ControlPlane / RuntimeSurface typed command；模型自主 sub-agent 进入 38 专项 | 已定案为 agent job 与模型自主 sub-agent 分流：前者仍是宿主管理 surface，后者是受治理 provider-directed `spawn_agent` 能力；agent job 不进入默认 provider tool allow-list | `AgentList` / `AgentRoster` / `AgentThreadRegister` / `AgentJobCreate` / `AgentJobDispatch` / `AgentJobItemReport` / `AgentJobRead` 继续作为人工/宿主管理 runtime surface；`spawn_agent` 仅在 governance 同时授予 `spawn_agent` 与 `module.sub_agent` 时进入 `tool-exec`，并物化为 `ModuleCapabilityStep(module.sub_agent / sub_agent.spawn)`。 |

## 7. 23.3 推荐实施顺序

1. 先迁移 P23-A / P23-I：让 CLI 产品入口可通过受控 typed bridge 触发新 loop，并能拿到可消费终态投影。这一步已落地并在 23.4.1 切为 CLI `send` 默认路径；它证明 `CoreIntentKind.Turn -> IKernelRuntimeExecutionLoop.RunReactiveAsync` 通道接通，但不等同完整产品投影 parity。
2. 再迁移 P23-B / P23-C / P23-D / P23-E：对齐 context、provider streaming、tool request、tool continuation。P23-C registered HTTP/SSE provider module 与 P23-E read-only filesystem tool continuation 已落地；后续先补 workspace write 的 human gate / explicit approval，再补高风险工具。
3. 然后迁移 P23-J：先把 runtime metrics、provider usage、diagnostics refs、runtime trace refs、replay summary 投影到 CLI / Host Gateway 可消费面；当前新 loop 已在 working directory 下写入最小 turn log / rollout JSONL 证据引用，旧 AppHost 诊断细节和完整 thread projection 仍作为独立 product-path delta 收敛。
4. 再处理 P23-G / P23-H / P23-F：interrupt、resume、steer 属于产品交互控制面，必须有明确降级策略，不能阻塞前两步的基础 turn 路径验证。
5. P23-K / P23-L 的 34 终局结论继续约束 review / plan UI 与 agent jobs：它们只作为宿主可见 typed projection / runtime surface 保留，不进入 provider-directed tool allow-list，也不阻塞旧 AppHost 兼容路径删除。模型自主 sub-agent 已从该排除项中拆出，由 38 专项按 `spawn_agent` + `module.sub_agent` 的受治理能力单独落地。

## 8. 回退策略

- 36 起 CLI `send` 的唯一产品 turn 路径是新 Kernel→Runtime loop；旧 `KernelTurnExecutionAppHostRuntime` 不再作为显式入口或 Host Gateway typed decision fallback。
- 新增功能必须走 `CoreIntent -> StageGraph -> RuntimeStep` 主线；若某能力未迁移或不在当前默认能力集内，必须 fail-closed 并输出 `failureCode`，不得回退到旧 AppHost turn loop。
- 旧/新产品行为 parity 证据只作为删除旧入口前的历史迁移判断依据，不再作为新增验收的运行路径。后续验收必须直接验证新 loop 的 provider/tool/control/evidence 能力。
- 23.4.0 已完成默认路径切换前置守护：默认路径判定下沉到 RuntimeComposition / Host Gateway typed decision；CLI 不得直接决定新旧 loop 或构造 Kernel / Runtime 内部对象。36 后 typed decision 只允许 `kernel-runtime-loop` 或 `fail-closed`。
- `--kernel-runtime-loop` 只保留为显式新 loop 验证入口；`--apphost-control-plane` 已移除并返回迁移诊断，不得出现在正式使用说明或当前验收步骤中。
- opt-in/default 等价测试只比较治理与终态语义白名单字段，包括 `turnStatus`、`stagePath`、`replayCompleteness`、diagnostics projection 结构、token usage 真实/估算语义和 cost availability；`executionPath`、`fallbackReason`、`failureCode`、run/turn/timestamp/trace ids 等路由或运行实例证据必须单独断言，不得混入 deep-equal。

23.3 的 `send --kernel-runtime-loop` 入口已经验证 P23-A / P23-I 的基础通道、P23-C registered provider module、P23-E read-only tool continuation 和无凭据 fail-closed。以下能力不纳入本次通过结论，必须作为后续独立 parity 项处理：

- 外部 provider 凭据 live path 已有 28/30 最小多 provider live matrix 证据：`responses`、`anthropic_messages` 与 `openai_chat_completions` wire-api 均通过当前源码默认新 loop；旧 AppHost 显式入口与新 Kernel→Runtime 默认路径在同一外部 provider 配置下的用户可见终态等价已作为删除旧入口前的历史证据关闭。
- provider WebSocket、旧 transport retry/idle timeout/W3C trace 的完整 parity。
- `write` 的 workspace write 路径仅在显式审批态下迁移，且只接受 workspace-relative path；`apply_patch` 只是在 mutating filesystem 模块内具备实现，不进入当前 CLI 新 loop allow-list；shell、MCP、artifact、memory/search tool 等非只读或非默认工具按 31 工具 parity 矩阵处理，不得隐式进入 provider tool surface；agent job 由 34 定案为人工/宿主管理 runtime surface，不进入当前 provider tool surface。模型自主 sub-agent 仅按 38 的 `spawn_agent` / `module.sub_agent` 受治理路径进入。
- steer / follow-up input 与 active turn 合并：23.9 先迁移 provider input 注入和 product projection；29 已补 RuntimeComposition pending steer queue、resume 重入和 CLI 产品命令层 evidence。后续只在非 CLI 宿主收敛或完整 thread status parity 中继续扩展。
- interrupt / resume 的真实产品桥接：interrupt 已在 25.4.3f 迁移 in-process active-run cancellation registry，并在 29 补 workingDirectory file-backed active-run index/cancel signal 与 CLI 产品命令层 evidence；resume 已补 RuntimeComposition HostControl checkpoint store、pending steer queue 重入和 CLI `--checkpoint-ref` evidence。AppHost / app-server 非 CLI 宿主控制面不属于当前 CLI parity 完成结论。
- review / plan UI 的旧 AppHost 私有 agent-message 事件逐字段同构；34 已定案为 Host Gateway / ControlPlane typed projection，不阻塞 35/36。
- provider-directed agent job tool；34 已定案为当前默认 Agent 工作流排除项，只保留人工/宿主管理 runtime surface，不阻塞 35/36/37。provider-directed sub-agent 已拆到 38，以 `spawn_agent` 请求和 `module.sub_agent` module bridge 独立验收。

## 9. 31 Tool parity 终局矩阵

31 只处理“AI/provider-directed tool request 是否进入当前 CLI 新 Kernel→Runtime loop 的工具入口”。CLI slash command、`!` user shell、ConfigGUI 操作、Sidecar RPC 或后续 Host Gateway UI 能力不因本矩阵自动变成模型可调用工具。

| 工具/能力 | 旧路径来源 | 当前新 loop 决策 | Runtime 入口 | 治理与拒绝口径 |
| --- | --- | --- | --- | --- |
| `read_file` | built-in filesystem tool | 迁移，默认开放 | `ToolInvocationStep` -> `TianShu.Tools.FileSystem` | 只读，`SideEffectLevel.ReadOnly`，无需 human gate。 |
| `list_dir` | built-in filesystem tool | 迁移，默认开放 | `ToolInvocationStep` -> `TianShu.Tools.FileSystem` | 只读，`SideEffectLevel.ReadOnly`，无需 human gate。 |
| `grep` / `glob` | built-in filesystem search | 迁移，默认开放 | `ToolInvocationStep` -> `TianShu.Tools.FileSystem` | 只读，`SideEffectLevel.ReadOnly`，无需 human gate。 |
| `write` | mutating filesystem tool | 迁移，但只在显式审批态开放 | `ToolInvocationStep` -> `TianShu.Tools.FileSystemMutating` | 必须同时满足 allow-list、`WorkspaceWrite` side-effect 上限、`RequiresHumanGate=true`、approval ref 和 workspace-relative path；缺任一条件 fail-closed。 |
| `apply_patch` | mutating filesystem tool | 当前 CLI 新 loop 明确禁用，不进入 provider tool surface | 无默认入口；模块实现仅作为未来迁移基底 | 若模型幻觉请求，Kernel/Runtime 以 `runtime.reactive.tool_request_not_allowed` fail-closed；未来迁移前必须补 patch preview、审批、workspace 约束、冲突处理和回滚证据。 |
| `shell` / `local_shell` / `shell_command` | shell runtime support | 当前 CLI 新 loop 明确禁用，不进入 provider tool surface | 无 provider-directed 入口；用户 `!` shell 属于 CLI/Control Plane 用户命令，不是模型工具 | 若模型幻觉请求，fail-closed；未来迁移前必须补命令审批、cwd/workspace 限制、环境脱敏、超时、输出截断和 audit。 |
| MCP resource | MCP runtime support | 当前不作为 provider-directed tool 进入默认 loop | 未来应以 MCP Module / Host Gateway typed projection 接入 | 只读 resource 可独立设计为 ModuleCapability；本阶段模型请求不得绕过 governance。 |
| MCP tool | MCP runtime support | 当前禁用 | 无默认入口 | 远端副作用必须等 MCP governance、descriptor、Host projection 完整后再迁移。 |
| artifact publish / attach | artifacts runtime support | 不作为 provider-directed tool 迁移 | `ArtifactStep` / `IArtifactStateProjectionModule` | 由 Kernel 物化 ArtifactStep；模型直接请求 `artifacts` / artifact tool 时不进入默认 allow-list。 |
| memory search / mutation | memory tools / memory runtime | 不作为 provider-directed tool 迁移 | `ModuleCapabilityStep` / `IMemoryModule`，CLI memory 命令仍走 Control Plane | memory query/mutation 必须由 Kernel/Runtime 模块能力或 CLI typed command 触发；`memory_search` 不进入默认 provider tool surface。 |
| search / `tool_search` | dynamic/search tool | 当前禁用 | 无默认入口 | 外部网络或动态 connector 搜索需另行配置治理；本阶段不作为删除旧路径前置阻塞项。 |
| agent job | agent job tools | 工具入口当前禁用；34 定案为人工/宿主管理 runtime surface，不进入 provider-directed tool surface | 无默认 provider-directed 入口；CLI/Sidecar runtime surface 仍可走 ControlPlane typed command | 不得因旧路径存在 agent job support 而进入 provider tool allow-list。 |
| subagent / fanout | spawn/fanout tools | 受治理开放 `spawn_agent`；不开放 `wait` / `send_input` / `close_agent` | `spawn_agent` 请求由 RuntimeComposition 物化为 `ModuleCapabilityStep(module.sub_agent / sub_agent.spawn)`，再由 `ExecutionRuntimeSubAgentModuleBridge` 调 `ISubAgentModule` | 必须同时满足 `spawn_agent` tool allow-list、`module.sub_agent` module allow-list、`HostMutation` 副作用上限、human gate / approval 继承规则和 `SubAgentSpawnLedger` 结构闸门；默认未授予时 fail-closed。 |

工具结果投影必须遵守统一状态集合：`succeeded`、`failed`、`blocked`、`cancelled`、`approval-required`、`timeout`。`ExecutionRuntimeToolBridge` 对成功、工具失败、治理拒绝和取消均必须输出结构化 `toolResults[]`，其中包含 `callId`、`toolId`、`status`、`output`、`failure` 和 `auditRef`；Runtime step status 只表达执行层终态，不能替代 `toolResults[]` 的工具级状态。

## 10. 32 Thread / Terminal / Diagnostics Parity

32 的目标是关闭当前 CLI 产品路径与 HostGateway typed projection 的可消费投影缺口，不恢复旧 AppHost 内部 raw diagnostics 逐字段同构。

正式合同如下：

- `src/Contracts/TianShu.Contracts.Projections` 的 `ThreadProjection` 是 HostGateway 对 thread status / usage / diagnostics / evidence 的正式消费面。
- `ThreadRuntimeStatusProjection` 承载 lifecycle、turn status、background status、active run 和 notification code；HostGateway 不暴露 `KernelRuntimeProductTerminalProjection` 等 RuntimeComposition 内部类型。
- `ThreadTokenUsageProjection` 承载 last/total token usage、context window、`estimated`、`source` 与 missing reason；真实 provider usage 和估算 usage 必须可区分。
- `ThreadDiagnosticsProjection` 承载 runtime trace refs、diagnostics refs、metrics event ids、failure codes、missing reasons 与 `ThreadContextSlicingDiagnosticsProjection`。
- `ThreadContextSlicingDiagnosticsProjection` 只投影 kept/dropped/source/budget 摘要；旧 AppHost slicing raw event、raw provider request diagnostics 和未脱敏 raw json 不作为新产品契约。
- `ThreadEvidenceProjection` 承载 turn log、rollout、audit refs 与 downgrade reasons；有 working directory 的新 loop turn log / rollout 必须真实落盘，否则结构化 unavailable。

实现口径如下：

- CLI 新 loop 继续由 `KernelRuntimeTurnLoopBridge` 输出 `KernelRuntimeProductTerminalProjection`，并把最小 turn log / rollout evidence 引用写入 summary。
- Execution Runtime 的 thread snapshot materialization 必须把已有 `ThreadStatusChanged`、`TurnStarted`、`TurnCompleted`、`ThreadTokenUsageUpdated` 事件归并为 `ThreadProjection.RuntimeStatus` 与 `ThreadProjection.TokenUsage`。
- Context prepare 的正式执行边界是 `ExecutionRuntimeContextPolicyBridge`：只消费 Kernel 批准的 `ApprovedContextPolicy`，输出 provider-neutral input 与 `ContextPolicyApplicationReport`；Provider module 不得自行重新裁切。
- review / plan UI event 不纳入 32 完成结论；34 已定案为宿主 typed projection / runtime surface，不重新打开 thread status parity。
- subagent / agent jobs 不纳入 32 完成结论；34 已定案为人工/宿主管理 runtime surface，不得把 agent job 状态误投为当前 thread status parity 已完成条件。

32 的验收守护：

- Runtime / ContextPolicy 测试必须覆盖预算裁切、缺 evidence ref、source mismatch、fail-closed 和 provider-neutral input。
- HostGateway 测试必须覆盖 `ThreadProjection` 的 status / usage / diagnostics / evidence typed payload，并继续守护不引用 Kernel / Runtime 内部类型。
- CLI 产品测试必须继续覆盖 `kernelRuntimeTerminalProjection`、runtime diagnostics projection、turn log / rollout reference、default/opt-in semantic parity。

## 11. 测试要求

后续 23.3 代码迁移必须至少补充以下测试：

| 测试类型 | 目标 |
| --- | --- |
| boundary tests | CLI/Experience 不直接构造 `CoreIntent`、`StageGraph`、`RuntimeStep`。 |
| product path parity tests | 同一输入下，旧路径和新路径的用户可见 terminal projection 等价；23.6~23.9 已建立新路径投影契约，25.4.3 已补新 loop 最小 turn log / rollout 证据引用与 in-process active-run cancellation registry；25.4.3f 已用同一显式 openai-compatible provider 配置证明旧/新用户可见终态等价。32 已补 HostGateway typed `ThreadProjection` 的 status / usage / diagnostics / evidence payload；旧 AppHost raw diagnostics 和新 KernelRuntime terminal projection 的内部字段不要求逐字段同构。 |
| provider bridge tests | registered HTTP/SSE path 必须覆盖 stream / tool directive / usage / 无凭据 fail-closed；外部 provider 凭据可用时再补 live path。 |
| tool bridge tests | 已覆盖 read-only provider-directed continuation、workspace write 审批、未授权高风险工具 fail-closed，以及 `succeeded` / `failed` / `blocked` / `cancelled` / `approval-required` / `timeout` 结构化 `toolResults[]` 投影。 |
| interrupt/resume tests | interrupt 必须经过 `InterruptIntent -> HostInteractionStep` 并投影 interrupted；新 Kernel→Runtime loop 内匹配 active run 时必须真实取消并投影 `active-run://...`，未命中时投影 `active_run_not_found`。interrupt 属于 `HostMutation`；resume 无 checkpoint fail-closed，有 checkpoint 时经过 `ResumeIntent -> HostInteractionStep` 并投影 checkpoint/replay 证据，当前仍为 `ReadOnly`。 |
| replay tests | `StagePath` 是 stage-level path，`Steps` 是 RuntimeStep summaries；同一 stage 多 step 不得被误读为 stage 重复执行。 |
| thread projection tests | HostGateway 必须能消费 `ThreadProjection.RuntimeStatus`、`TokenUsage`、`Diagnostics.ContextSlicing` 与 `Evidence`，且不得引用 `KernelRuntimeProductTerminalProjection`、`RuntimeStep`、`StageGraph` 等内部类型。 |
| multi-host experience boundary tests | ConfigGUI 只能引用配置 projection / preview / apply 契约；Sidecar 只能在 composition boundary 持有 runtime 并包装 Control Plane / HostGateway；VSIX 只能通过 sidecar typed protocol，不引用核心 runtime 项目或内部对象。 |

## 12. 29 Host control parity 设计归属

29 的目标不是恢复旧 AppHost turn loop，而是把产品控制面迁入新 Kernel→Runtime 主线。Host control 的正式链路固定为：

```text
Host Gateway typed surface
  -> Control Plane normalized operation
  -> RuntimeComposition HostControl bridge/store
  -> CoreIntent(interrupt/resume/steer)
  -> StageGraph HostInteractionStep / ModelInvocationStep
  -> Execution Runtime projection + evidence
```

归属规则如下：

| 能力 | 正式归属 | 不允许的归属 |
| --- | --- | --- |
| 跨进程 active-run registry | `src/Core/TianShu.RuntimeComposition` 提供可注入 HostControl store；AppHost / app-server / CLI 都只能通过 typed operation 访问。 | CLI 静态字典、Presentation 直接持有 `CancellationTokenSource`、HostGateway 直接构造 Kernel/Runtime 内部对象。 |
| interrupt cancel | Host operation 归一化后进入 `InterruptIntent -> HostInteractionStep`，再由 HostControl store 定位 run 并取消；命中投影 `active-run://...`，未命中投影 `active_run_not_found`。 | 隐式回退旧 AppHost loop 或仅写一个 interrupted 文本终态。 |
| checkpoint store / resume | HostControl store 负责 checkpoint ref 到 thread/session/turn/replay/restored draft/pending steer 的可审计映射；`ResumeIntent -> HostInteractionStep` 只消费已解析 checkpoint projection。 | resume 命令绕开 checkpoint store、只凭用户传入字符串算通过。 |
| pending steer queue | HostControl store 按 thread/turn 保存 accepted / queued / applied / rejected steer；下一轮 `model-reason` 的 provider input 只读取该 typed projection。 | Provider module 自行读取 CLI 状态、Presentation 私有队列直接拼 provider input。 |
| background run lifecycle | RuntimeComposition 在 run start/register、completion/failure/cancel cleanup、thread status projection 与 evidence refs 处统一登记和清理。 | 只证明取消入口，遗漏后台 run 完成后的清理与状态投影。 |

实现分层要求：

- `src/Core/TianShu.HostGateway` 只暴露宿主 typed operation 和 projection，不引用 `CoreIntent`、`StageGraph`、`RuntimeStep`、`StableKernelCore` 或 `AdaptiveRuntimeExecutionLoop`。
- `src/Core/TianShu.ControlPlane` 只做操作归一化、治理 envelope 和 typed command 转发，不拥有 active run、checkpoint 或 pending steer 的真实存储。
- `src/Core/TianShu.RuntimeComposition` 拥有 HostControl store 抽象与默认实现，并负责把 Host control command 映射成 Kernel intent 与 Runtime evidence。
- `src/Execution/TianShu.Execution.Runtime` 只消费 RuntimeStep 并产出 metrics / diagnostics / projection，不直接读取 CLI 或宿主 UI 状态。
- `src/Presentations/TianShu.Cli` 只能调用 Host Gateway / Control Plane typed surface 或 `KernelRuntimeTurnLoopBridge` 产品桥，不直接访问 HostControl store、Kernel 或 Runtime 内部对象。

29 已关闭当前 CLI 产品控制面 Host control parity：RuntimeComposition HostControl store 提供 active-run registry、checkpoint store、pending steer queue 与生命周期清理；CLI `follow-up --kernel-runtime-loop` 通过产品桥执行 interrupt / steer / resume，并保存对应证据。33 补充关闭多宿主 Experience 边界守护：ConfigGUI 作为配置编辑宿主不进入 turn runtime；Sidecar 作为 VSIX 进程内 composition bridge 可持有 `IExecutionRuntime` 生命周期但不得触碰 Kernel / Runtime 内部对象；VSIX 只经 sidecar typed protocol。provider streaming parity、旧 AppHost 兼容路径删除分别由后续条目收敛，不得偷算进 29 或 33。
