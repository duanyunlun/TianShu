# TianShu 结构化上下文管理与 Context Policy 设计

## 1. 文档定位

结构化上下文管理位于 Kernel StageGraph 和 Execution Runtime 输入准备之间。Kernel 负责基于 provider usage、估算 token、来源优先级、supersede 链和压缩策略生成上下文管理计划，并批准 `ContextPolicy`；Execution Runtime 只执行 `ApprovedContextPolicy`，并将候选上下文物化为 provider-neutral `ProviderInputItem` 与可审计报告。Provider Module 只消费已经准备好的 provider invocation input，不拥有上下文裁切、摘要、降权、supersede 取舍或引用化决策权。

本文件以 `docs/tianshu-architecture-spec.md` 为总架构基线，作为 Context Policy 当前代码落地和后续验收基线。

## 2. 当前项目代码

| 项目 | 当前职责 |
| --- | --- |
| `src/Contracts/TianShu.Contracts.Kernel` | 定义 `ContextPolicy`、`ContextSourceRule`、`ApprovedContextPolicy`、`ContextSourceCandidate`、`ContextPolicyApplicationReport`、`MaterializedContextSegment`、`DroppedContextSegment`；后续补齐 `StructuredContextManagementPlan`、usage signal、trigger、degradation、supersede、compression checkpoint 和 audit record 合同。 |
| `src/Execution/TianShu.Execution.Runtime` | 定义 `ExecutionRuntimeContextPolicyBridge`，只接受 Kernel 已批准的 `ApprovedContextPolicy` 并输出 provider-neutral input；后续补齐 context slice、compaction candidate、supersede decision、checkpoint 与 trace 执行链。 |
| `src/Contracts/TianShu.Contracts.Provider` | 定义 `ProviderInputItem`、`ProviderInvocationRequest` 和 `ProviderInvocationContext`；Provider 只消费这些结果，不重新裁切上下文。 |
| `src/Contracts/TianShu.Contracts.Diagnostics` | 承载 context policy application report、context management audit record、trigger reason、degradation reason 和 trace 投影。 |
| `src/Contracts/TianShu.Contracts.Projections` | 定义 `ThreadTokenUsageProjection`、`ThreadContextSlicingDiagnosticsProjection` 和消费层可见的 kept / dropped / downgrade summary。 |
| `src/Contracts/TianShu.Contracts.Memory` | Memory retrieve 只能投影 `ContextSourceCandidate(ContextSourceKind.MemoryRecord)`；form / supersede 只提供候选和审计证据，不拥有最终上下文取舍权。 |
| `src/Hosting/TianShu.AppHost.Tools.Runtime.ContextSlicing` | 现有 AppHost 上下文裁切实现，作为迁移输入；新功能不得继续把 AppHost helper 当成最终 ContextPolicy 所有者。 |

## 3. 正式契约骨架

归属项目：`src/Contracts/TianShu.Contracts.Kernel`

```csharp
public sealed record ContextPolicy
{
    public string PolicyId { get; }
    public int MaxInputTokens { get; }
    public IReadOnlyList<string> PriorityRefs { get; }
    public IReadOnlyList<string> AllowedSourceKinds { get; }
    public IReadOnlyList<ContextSourceRule> SourceRules { get; }
    public bool PreserveLatestUserCorrection { get; }
    public bool RequireEvidenceRefs { get; }
    public ContextProjectionMode LowConfidenceMode { get; }
    public bool FailClosed { get; }
    public MetadataBag Metadata { get; }
}

public sealed record ContextSourceRule
{
    public ContextSourceKind SourceKind { get; }
    public int Priority { get; }
    public ContextProjectionMode ProjectionMode { get; }
    public decimal MinConfidence { get; }
    public bool RequireEvidenceRef { get; }
    public int MaxTokens { get; }
}

public sealed record ApprovedContextPolicy
{
    public ContextPolicy Policy { get; }
    public CoreIntentId SourceIntentId { get; }
    public StageGraphId SourceGraphId { get; }
    public StageId SourceStageId { get; }
    public KernelOperationId SourceKernelOperationId { get; }
    public DateTimeOffset ApprovedAt { get; }
    public IReadOnlyList<string> ValidationRefs { get; }
}

public sealed record ContextSourceCandidate
{
    public string SegmentId { get; }
    public ContextSourceKind SourceKind { get; }
    public string Content { get; }
    public int EstimatedTokens { get; }
    public decimal Confidence { get; }
    public string? EvidenceRef { get; }
    public string? ArtifactRef { get; }
    public bool IsLatestUserCorrection { get; }
    public MetadataBag Metadata { get; }
}

public sealed record ContextPolicyApplicationReport
{
    public string PolicyId { get; }
    public int MaxInputTokens { get; }
    public int EstimatedTotalTokens { get; }
    public int EstimatedIncludedTokens { get; }
    public IReadOnlyList<MaterializedContextSegment> IncludedSegments { get; }
    public IReadOnlyList<DroppedContextSegment> DroppedSegments { get; }
}
```

归属项目：`src/Execution/TianShu.Execution.Runtime`

```csharp
public interface IExecutionRuntimeContextPolicyBridge
{
    Task<ExecutionRuntimeContextPolicyResult> PrepareProviderInputAsync(
        ModelInvocationStep step,
        ExecutionRuntimeContext context,
        ApprovedContextPolicy approvedPolicy,
        IReadOnlyList<ContextSourceCandidate> candidates,
        CancellationToken cancellationToken);
}
```

## 4. 来源与投影规则

正式来源类别：

- `CurrentUserInput`
- `LatestUserCorrection`
- `ToolEvidence`
- `ArtifactReference`
- `MemoryRecord`
- `ConversationHistory`
- `WorkspaceFact`
- `SystemInstruction`

正式投影模式：

- `Full`：全文进入 provider-neutral input。
- `Summary`：摘要进入 provider-neutral input，并保留源引用。
- `ReferenceOnly`：只进入引用占位，不暴露完整内容。
- `Excluded`：不进入 provider-neutral input，必须在 report 中记录原因。

`CurrentUserInput` 和 `LatestUserCorrection` 在默认排序中优先级最高。`ToolEvidence` 与 `ArtifactReference` 必须携带 `EvidenceRef` 或 `ArtifactRef`，否则必须 fail closed 或记录 `MissingEvidenceRef` dropped reason。低置信历史默认降级为 `ReferenceOnly`，不得静默当成高可信事实。

## 5. Execution Runtime 规则

- Execution Runtime 只能消费 `ApprovedContextPolicy`，不得消费裸 `ContextPolicy` 执行 provider 输入准备。
- `ApprovedContextPolicy` 的 `SourceIntentId`、`SourceGraphId`、`SourceStageId`、`SourceKernelOperationId` 必须与 `ModelInvocationStep` 一致，否则返回 `context_policy_source_mismatch`。
- `ContextPolicy.FailClosed` 为 `true` 时，`MaxInputTokens` 必须为正数，否则返回 `context_policy_missing_budget`。
- 超预算片段必须进入 `DroppedContextSegment`，原因必须是 `BudgetExceeded`。
- 缺少证据引用的工具证据或 artifact 引用必须进入 `DroppedContextSegment`，原因必须是 `MissingEvidenceRef`。
- 成功输出必须是 `ProviderInputItem`，不得是 provider wire payload 或具体厂商消息格式。

## 6. Provider 边界

Provider Module 不重新裁切上下文。Provider 的正式输入是：

```text
ExecutionRuntimeContextPolicyBridge
  -> ProviderInputItem[]
  -> ProviderInvocationRequest
  -> ExecutionRuntimeProviderBridge
  -> IProviderModule.InvokeAsync
```

Provider implementation 可以做协议映射，例如 OpenAI Responses、Anthropic Messages 或 Google Generative 格式转换；但这种映射只能消费 `ProviderInvocationRequest.Inputs`，不能重新决定哪些上下文应进入输入，也不能丢弃 Kernel 已批准的当前用户输入或最新纠正。

## 7. 结构化上下文触发与失败关闭

结构化上下文管理不是 provider tool，也不是 Memory Module 私有能力。它只能由 Kernel 批准的 `ApprovedContextPolicy` 触发，再由 Execution Runtime context bridge 物化。

P27 的上下文能力必须按以下规则落地：

- token threshold、分层降级、supersede 取舍、可逆压缩和 checkpoint 都必须表达在 policy / report / trace 中，不得静默发生。
- provider usage 缺失时可以使用 estimated token 作为 diagnostics 与降级参考，但不能晋升为真实 provider usage 或 cost。
- Memory、MCP resource、workspace fact、tool evidence 只能作为 `ContextSourceCandidate` 进入策略执行；它们不能自行决定最终 inclusion / drop。
- 缺少 `ApprovedContextPolicy`、source ids 不一致、预算非法、证据引用缺失或 policy schema 不合法时必须 fail closed，或者在 policy 明确允许降级时记录 dropped reason。
- 压缩与 supersede 决策必须保留原始 evidence / artifact ref；不可逆摘要不能替代需要完整保真的用户最新纠正、审批输入或工具证据。

## 8. P27.11 正式管理模型

P27.11 的目标不是只做一次简单裁切，而是定义后续 P27.12/P27.13 可以照着实现和验收的结构化上下文管理闭环。正式闭环分为六步：

1. `UsageInput`：采集上一轮或当前可用的 provider usage、估算 token、模型上下文窗口和成本可用性。
2. `PressureTrigger`：根据 usage、估算输入、模型窗口、预算余量和 fail-closed 策略判断是否需要上下文管理动作。
3. `LayeredDegradation`：按来源层级做 keep、summary、reference-only、drop 或 compression-candidate。
4. `SupersedeDecision`：对记忆、事实、用户纠正和工具证据做取代链取舍，旧事实不得静默覆盖或消失。
5. `CompressionCheckpoint`：对可压缩片段形成可逆压缩候选、checkpoint 和 artifact/evidence ref。
6. `AuditReport`：输出 `ContextPolicyApplicationReport`、diagnostics、trace、Host projection 和 audit ref。

正式数据流：

```text
RuntimeMetricsEvent / ProviderUsage / estimated tokens
  -> ContextUsageSignal
  -> StructuredContextManagementPlan
  -> ApprovedContextPolicy
  -> ExecutionRuntimeContextPolicyBridge
  -> ProviderInputItem[] + ContextPolicyApplicationReport
  -> Runtime metrics / diagnostics / thread projection / audit
```

`StructuredContextManagementPlan` 属于 Kernel 决策，不属于 Execution Runtime。Execution Runtime 只执行已批准计划对应的 `ApprovedContextPolicy`，并在 report 中证明实际执行结果。

## 9. Provider Usage 输入规则

Provider usage 是上下文管理的输入信号，但不是上下文策略本身。正式规则：

- 真实 provider usage 来源为 `ProviderUsage` 或 `RuntimeMetricsEvent.TokenUsage(Estimated=false)`。
- 估算 usage 来源为输入文本长度、token estimator 或 provider usage 缺失时的降级估算，必须标记 `Estimated=true`。
- provider usage 缺失时必须记录 `provider_usage_missing`；估算 usage 不得晋升为真实 provider usage，也不得作为真实 cost。
- cost 只有在真实 usage、price model version、currency 和 cost 同时存在时才可用。
- usage 信号可以触发下一轮或本轮后续 stage 的上下文管理；不能回写改变已发送给 provider 的历史请求。
- usage 信号必须携带 `runId / executionId / planId / graphId / stageId / stepId / providerId / modelId / source`，用于 trace 和 replay。

归属项目：`src/Contracts/TianShu.Contracts.Kernel`

```csharp
public sealed record ContextUsageSignal(
    string SignalId,
    string Source,
    bool Estimated,
    int? InputTokens,
    int? OutputTokens,
    int? ReasoningTokens,
    int? TotalTokens,
    int? ModelContextWindow,
    string? MissingReason,
    MetadataBag Metadata);

public sealed record ContextPressureTrigger(
    string TriggerId,
    ContextPressureTriggerKind Kind,
    decimal ThresholdRatio,
    int? ThresholdTokens,
    int ObservedTokens,
    int? ModelContextWindow,
    bool FailClosed);

public enum ContextPressureTriggerKind
{
    Unspecified = 0,
    EstimatedInputBudgetExceeded = 1,
    ProviderUsageHighWatermark = 2,
    ModelContextWindowNearLimit = 3,
    MissingUsageFallback = 4,
    ManualPolicyRequest = 5,
}
```

## 10. Token 触发规则

默认触发条件必须保守且可诊断：

| 触发 | 条件 | 动作 |
| --- | --- | --- |
| `EstimatedInputBudgetExceeded` | 候选输入估算 token 超过 `ContextPolicy.MaxInputTokens`。 | 执行分层降级和预算裁切。 |
| `ProviderUsageHighWatermark` | 上一轮真实 `InputTokens / ModelContextWindow` 达到高水位。 | 下一轮默认降低历史和低置信来源投影。 |
| `ModelContextWindowNearLimit` | 当前模型窗口已知且剩余窗口不足。 | fail-closed 或强制 reference-only / drop 低优先级片段。 |
| `MissingUsageFallback` | provider usage 缺失。 | 使用估算 token 作为 diagnostics 和降级参考，记录 missing reason。 |
| `ManualPolicyRequest` | Kernel / Host operation 明确请求上下文刷新。 | 生成显式 audit record，不静默执行。 |

触发只能影响 `StructuredContextManagementPlan` 与 `ApprovedContextPolicy`，不得由 Provider Module 在协议映射时自行触发。

## 11. 分层降级规则

上下文来源必须先分层，再按层内 priority / confidence / evidence 排序：

| 层级 | 来源 | 默认策略 | 保护规则 |
| --- | --- | --- | --- |
| L0 | `CurrentUserInput`、`LatestUserCorrection`、当前审批输入 | `Full` | 不可压缩、不可丢弃；超预算时 fail closed。 |
| L1 | 当前 stage 的 `ToolEvidence`、`ArtifactReference`、MCP resource 读取结果 | `Full` 或 `Summary` | 必须有 evidence/artifact ref；缺失则 dropped 或 fail closed。 |
| L2 | `WorkspaceFact`、高置信 `MemoryRecord` | `Summary` 或 `ReferenceOnly` | 不得绕过 ContextPolicy 直接进入 provider request。 |
| L3 | `ConversationHistory`、低优先级历史 | `Summary`、`ReferenceOnly` 或 `Dropped` | 优先被压缩和裁切。 |
| L4 | 低置信、重复、superseded、过期来源 | `ReferenceOnly` 或 `Dropped` | 必须记录 dropped / downgraded reason。 |

归属项目：`src/Contracts/TianShu.Contracts.Kernel`

```csharp
public sealed record ContextDegradationLayerRule(
    string LayerId,
    IReadOnlyList<ContextSourceKind> SourceKinds,
    ContextProjectionMode DefaultProjectionMode,
    ContextProjectionMode PressureProjectionMode,
    bool ProtectedFromDrop,
    bool ProtectedFromCompression,
    int Priority);

public sealed record ContextDegradationDecision(
    string SegmentId,
    string LayerId,
    ContextProjectionMode OriginalMode,
    ContextProjectionMode EffectiveMode,
    ContextDropReason? DropReason,
    string? EvidenceRef,
    string? ArtifactRef);
```

## 12. Supersede 取舍规则

Supersede 只处理事实、记忆和纠错链，不处理模型内部推理。正式规则：

- `LatestUserCorrection` 永远优先于旧 conversation history 和旧 memory fact。
- Memory `SupersedeMemory` 必须保留 old record ref、new record ref、reason、approval ref 和 audit ref。
- 被 supersede 的旧事实默认进入 `ReferenceOnly` 或 `Dropped(ContextDropReason.PolicyExcluded)`，不得与新事实同时作为高可信全文输入。
- 如果新旧事实都有工具证据，必须保留证据链，并在 report 中标记 supersede decision。
- 如果 supersede 链不完整，不能静默覆盖；只能保留最新用户纠正和可验证 evidence，并记录 `context_supersede_chain_incomplete` diagnostics。

归属项目：`src/Contracts/TianShu.Contracts.Kernel`

```csharp
public sealed record ContextSupersedeDecision(
    string DecisionId,
    string SupersededSegmentId,
    string ReplacementSegmentId,
    ContextSupersedeDisposition Disposition,
    string Reason,
    string? EvidenceRef,
    string? AuditRef);

public enum ContextSupersedeDisposition
{
    Unspecified = 0,
    PreferReplacement = 1,
    KeepBothWithConflictMarker = 2,
    DropSuperseded = 3,
    ReferenceOnlySuperseded = 4,
}
```

## 13. 可逆压缩规则

压缩不是简单摘要。P27 的压缩必须先以 candidate / checkpoint 形式存在，确保可审计和可回溯：

- `CurrentUserInput`、`LatestUserCorrection`、审批输入、需要完整保真的工具证据不可压缩。
- 可压缩对象主要是 L2/L3 的历史、重复证据、长 artifact 摘要和低频 memory records。
- 可逆压缩必须保留原始 evidence/artifact ref、原始 segment refs、压缩算法/模型/策略 id 和 checkpoint ref。
- 压缩产物可以作为 `ContextSourceCandidate` 重新进入策略，但必须标记 `ProjectionMode.Summary` 或 `ReferenceOnly`。
- 不可逆摘要只能作为 diagnostics 或低置信参考，不能替代 protected segment。

归属项目：`src/Contracts/TianShu.Contracts.Kernel`

```csharp
public sealed record ContextCompressionCandidate(
    string CandidateId,
    IReadOnlyList<string> SourceSegmentIds,
    int OriginalEstimatedTokens,
    int TargetEstimatedTokens,
    bool Reversible,
    string? ArtifactRef,
    string? EvidenceRef,
    string Reason);

public sealed record ContextCompressionCheckpoint(
    string CheckpointId,
    string CandidateId,
    IReadOnlyList<string> SourceSegmentRefs,
    string CompressedArtifactRef,
    bool Reversible,
    string PolicyId,
    string AuditRef);
```

## 14. 审计记录与投影

结构化上下文管理必须可复盘。每次执行至少要产生：

- `ContextPolicyApplicationReport`：Runtime 执行事实。
- `ContextManagementAuditRecord`：Kernel 决策事实，包括 trigger、usage、degradation、supersede、compression。
- diagnostics refs：面向开发和故障排查。
- trace refs：面向 replay。
- Host projection：面向 CLI / 远程连续性 / 其他宿主的摘要视图。

归属项目：`src/Contracts/TianShu.Contracts.Kernel` 与 `src/Contracts/TianShu.Contracts.Projections`

```csharp
public sealed record StructuredContextManagementPlan(
    string PlanId,
    ContextUsageSignal UsageSignal,
    IReadOnlyList<ContextPressureTrigger> Triggers,
    IReadOnlyList<ContextDegradationLayerRule> LayerRules,
    IReadOnlyList<ContextSupersedeDecision> SupersedeDecisions,
    IReadOnlyList<ContextCompressionCandidate> CompressionCandidates,
    ApprovedContextPolicy ApprovedPolicy,
    MetadataBag Metadata);

public sealed record ContextManagementAuditRecord(
    string AuditId,
    string PlanId,
    string PolicyId,
    string SourceIntentId,
    string SourceGraphId,
    string SourceStageId,
    string SourceKernelOperationId,
    IReadOnlyList<string> TriggerIds,
    IReadOnlyList<string> IncludedSegmentIds,
    IReadOnlyList<string> DroppedSegmentIds,
    IReadOnlyList<string> CompressionCheckpointRefs,
    IReadOnlyList<string> DiagnosticsRefs,
    DateTimeOffset CreatedAt);
```

Host projection 不暴露原始长文本或 secret。`ThreadContextSlicingDiagnosticsProjection` 只展示 policy id、预算、估算 token、kept/dropped segment 摘要、projection mode、dropped reason、evidence ref、artifact ref 和 source layer。

## 15. P27.12 实施顺序

P27.12 必须按以下顺序实现，避免先做完美 schema 或先接 Memory mutation：

1. 扩展 `TianShu.Contracts.Kernel`：补 `ContextUsageSignal`、trigger、degradation、supersede、compression candidate、audit record。
2. 扩展 `ExecutionRuntimeContextPolicyBridge`：在现有排序/预算逻辑上增加 source layer、downgrade reason、supersede decision 和 compression candidate 输出。
3. 接入 provider usage：从 `RuntimeMetricsEvent.TokenUsage` 或 `ProviderUsage` 输入 context management；缺失时生成 estimated signal。
4. 接入 checkpoint / trace：为压缩候选和 supersede decision 写 audit / diagnostics refs。
5. 更新 `ThreadContextSlicingDiagnosticsProjection`：投影 source layer、trigger reason、supersede disposition 和 compression checkpoint refs。
6. 只在上述链路稳定后，再让 Memory retrieve/form/supersede 进入 P27.14。

## 16. 迁移规则

- `src/Hosting/TianShu.AppHost.Tools.Runtime.ContextSlicing` 作为现有行为迁移输入保留。
- 新增上下文策略、预算、降权、dropped reason、provider input 准备逻辑必须优先进入 `TianShu.Contracts.Kernel` 与 `TianShu.Execution.Runtime`。
- AppHost helper 不得成为新功能的正式 ContextPolicy 所有者。
- 迁移期如需桥接旧 context slicing report，必须只输出 diagnostics / trace，不得作为 Provider Module 私有裁切策略。

## 17. 验收标准

- `ContextPolicy`、`ApprovedContextPolicy`、`ContextSourceCandidate`、`ContextPolicyApplicationReport` 位于 `TianShu.Contracts.Kernel` 并有 contract tests。
- Execution Runtime 通过 `ExecutionRuntimeContextPolicyBridge` 只执行已批准 context policy。
- 当前用户输入和最新纠正优先进入 provider-neutral input。
- 工具证据和 artifact 引用缺少可追踪 ref 时必须记录 dropped reason。
- 低置信历史默认 `ReferenceOnly`，不得作为高可信全文进入 provider input。
- 超预算必须产出 `BudgetExceeded` dropped reason。
- provider usage 缺失必须记录 missing reason；估算 usage 只能作为 diagnostics 和降级参考。
- token 触发、分层降级、supersede 取舍、压缩候选、checkpoint 和 audit record 必须可在 report / diagnostics / trace 中复盘。
- protected segment 不得被压缩或丢弃；若预算不足必须 fail closed。
- superseded 旧事实不得与 replacement 同时作为高可信全文输入。
- 可逆压缩必须保留 source segment refs、artifact ref、evidence ref、checkpoint ref 和 audit ref。
- Provider Module 不拥有上下文裁切策略，只消费 `ProviderInvocationRequest.Inputs`。
- Memory、MCP resource、workspace fact、tool evidence 均不得绕过 `ApprovedContextPolicy` 直接塞入 provider request。
