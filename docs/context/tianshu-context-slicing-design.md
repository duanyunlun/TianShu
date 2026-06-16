# TianShu Context Policy 设计

## 1. 文档定位

Context Policy 位于 Kernel StageGraph 和 Execution Runtime 输入准备之间。Kernel 负责决定和批准 `ContextPolicy`，Execution Runtime 只执行 `ApprovedContextPolicy`，并将候选上下文物化为 provider-neutral `ProviderInputItem`。Provider Module 只消费已经准备好的 provider invocation input，不拥有上下文裁切、摘要、降权或引用化决策权。

本文件以 `docs/tianshu-architecture-spec.md` 为总架构基线，作为 Context Policy 当前代码落地和后续验收基线。

## 2. 当前项目代码

| 项目 | 当前职责 |
| --- | --- |
| `src/Contracts/TianShu.Contracts.Kernel` | 定义 `ContextPolicy`、`ContextSourceRule`、`ApprovedContextPolicy`、`ContextSourceCandidate`、`ContextPolicyApplicationReport`、`MaterializedContextSegment`、`DroppedContextSegment`。 |
| `src/Execution/TianShu.Execution.Runtime` | 定义 `ExecutionRuntimeContextPolicyBridge`，只接受 Kernel 已批准的 `ApprovedContextPolicy` 并输出 provider-neutral input。 |
| `src/Contracts/TianShu.Contracts.Provider` | 定义 `ProviderInputItem`、`ProviderInvocationRequest` 和 `ProviderInvocationContext`；Provider 只消费这些结果，不重新裁切上下文。 |
| `src/Contracts/TianShu.Contracts.Diagnostics` | 承载 context policy application report 的诊断事件和 trace 投影。 |
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

## 7. 迁移规则

- `src/Hosting/TianShu.AppHost.Tools.Runtime.ContextSlicing` 作为现有行为迁移输入保留。
- 新增上下文策略、预算、降权、dropped reason、provider input 准备逻辑必须优先进入 `TianShu.Contracts.Kernel` 与 `TianShu.Execution.Runtime`。
- AppHost helper 不得成为新功能的正式 ContextPolicy 所有者。
- 迁移期如需桥接旧 context slicing report，必须只输出 diagnostics / trace，不得作为 Provider Module 私有裁切策略。

## 8. 验收标准

- `ContextPolicy`、`ApprovedContextPolicy`、`ContextSourceCandidate`、`ContextPolicyApplicationReport` 位于 `TianShu.Contracts.Kernel` 并有 contract tests。
- Execution Runtime 通过 `ExecutionRuntimeContextPolicyBridge` 只执行已批准 context policy。
- 当前用户输入和最新纠正优先进入 provider-neutral input。
- 工具证据和 artifact 引用缺少可追踪 ref 时必须记录 dropped reason。
- 低置信历史默认 `ReferenceOnly`，不得作为高可信全文进入 provider input。
- 超预算必须产出 `BudgetExceeded` dropped reason。
- Provider Module 不拥有上下文裁切策略，只消费 `ProviderInvocationRequest.Inputs`。
