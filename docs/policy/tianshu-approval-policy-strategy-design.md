# TianShu Approval / Policy 设计

## 1. 文档定位

Approval / Policy 属于 Control Plane、Kernel、Execution Runtime 与 Module Plane 的共同边界。Control Plane 创建并归一化 `GovernanceEnvelope`；Kernel 只能在 envelope 内生成 `StageGraph`、`KernelOperation` 与 `RuntimeStep`；Execution Runtime 及其 bridge 入口必须拒绝超过 envelope 的 step；Tool / Module descriptor 必须能在进入执行前与 envelope 对齐。

## 2. 当前项目

| 项目 | 当前用途 |
| --- | --- |
| `src/Contracts/TianShu.Contracts.Kernel` | `GovernanceEnvelope`、`GraphPolicySet`、`PermissionEnvelope`、`SideEffectProfile`、proposal risk profile。 |
| `src/Contracts/TianShu.Contracts.Governance` | `ApprovalId`、`AuditRecordId`、`PolicyDecision` 等治理事实契约。 |
| `src/Core/TianShu.ControlPlane` | `ControlPlaneGovernanceEnvelopeFactory`，负责创建和归一化 envelope，不执行 Kernel 编排。 |
| `src/Core/TianShu.Kernel` | `KernelValidator`，验证 StageGraph / KernelOperation / RuntimeStep 不超过 envelope。 |
| `src/Execution/TianShu.Execution.Runtime` | `TianShuExecutionRuntime` 与 runtime bridge 执行前 envelope 校验。 |
| `src/Contracts/TianShu.Contracts.Tools` | `ToolDescriptor.IsAllowedBy(GovernanceEnvelope)`。 |
| `src/Contracts/TianShu.Contracts.Modules` | `ModuleDescriptor.IsAllowedBy(GovernanceEnvelope)`。 |

## 3. 接口骨架归属

归属项目：`src/Contracts/TianShu.Contracts.Kernel`。

```csharp
public sealed record GovernanceEnvelope(
    string EnvelopeId,
    IReadOnlyList<string> PolicyIds,
    IReadOnlyList<string> AllowedToolIds,
    IReadOnlyList<string> AllowedModuleIds,
    SideEffectLevel MaxSideEffectLevel,
    bool RequiresHumanGate,
    IReadOnlyList<ApprovalId> ApprovalIds,
    IReadOnlyList<AuditRecordId> AuditRecordIds,
    IReadOnlyList<PolicyDecision> PolicyDecisions);
```

## 4. 规则

- Control Plane 是 envelope 创建入口。
- Control Plane 创建 envelope 时必须去重、裁剪空白 id，并保留 policy、tool、module、side effect、approval、audit 与 policy decision 引用。
- Kernel 验证 StageGraph 时必须检查 graph policy 不超过 envelope：required policy 必须存在于 envelope；allowed KernelTool / CapabilityTool 必须存在于 `AllowedToolIds`；allowed module 必须存在于 `AllowedModuleIds`；graph 副作用上限不得超过 envelope。
- Kernel 验证 RuntimeStep 时必须检查 step 副作用和 human gate 不超过 envelope。
- Execution Runtime 的批量入口、单 step 入口和 provider / tool / memory / artifact / diagnostics bridge 入口都必须复用 RuntimeStep envelope 校验。
- ToolInvocationStep 必须同时检查 `CapabilityToolId` 与 `InputEnvelope.ToolId` 是否在 `AllowedToolIds`。
- ModelInvocationStep 必须检查 `ProviderModuleId` 是否在 `AllowedModuleIds`；若 envelope 声明 `PolicyIds`，则 `ModelRoutePolicy.PolicyId` 必须包含其中。
- ModuleCapabilityStep 必须检查 `ModuleId` 是否在 `AllowedModuleIds`。
- 需要 human gate 的 RuntimeStep 只有在 envelope `RequiresHumanGate = true` 且存在 `ApprovalIds` 时才能执行。
- Tool / Module descriptor 必须通过 `IsAllowedBy(GovernanceEnvelope)` 或等价规则判断是否处于 allow-list、副作用上限与 human gate 边界内。
- 高风险策略晋升必须 human gate；strategy registry 不允许缺少 human-approved evidence 的 promoted 迁移。
- `propose_kernel_policy_change` 只能返回 `PolicyChangeProposal`，不得自动改写当前 envelope 或 graph policy。

## 5. 验收标准

- policy 变更可审计。
- approval 结果可回放。
- AI 不能通过 KernelTool 放宽 governance envelope。
- Runtime bridge 不能绕过 Execution Runtime envelope 校验。
- descriptor 对齐测试必须覆盖 tool/module allow-list、副作用上限和 human gate。
