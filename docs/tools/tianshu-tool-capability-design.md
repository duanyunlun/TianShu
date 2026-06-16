# TianShu Tool / Capability 设计

## 1. 文档定位

工具能力属于 Module Plane。所有 AI 可调用能力必须通过统一 `ITianShuTool` 描述、授权、审计和执行。

## 2. 当前项目

| 项目 | 当前用途 |
| --- | --- |
| `src/Contracts/TianShu.Contracts.Tools` | 工具契约。 |
| `src/Contracts/TianShu.Contracts.Modules` | Module Plane 通用契约。 |
| `src/Tools/TianShu.Tools.Artifacts` | 工件工具。 |
| `src/Tools/TianShu.Tools.Code` | 代码工具。 |
| `src/Tools/TianShu.Tools.Collaboration` | 协作工具。 |
| `src/Tools/TianShu.Tools.Fanout` | fanout 工具。 |
| `src/Tools/TianShu.Tools.FileSystem` | 文件系统只读能力。 |
| `src/Tools/TianShu.Tools.FileSystemMutating` | 文件系统变更能力。 |
| `src/Tools/TianShu.Tools.Interaction` | 用户交互能力。 |
| `src/Tools/TianShu.Tools.McpResources` | MCP resource 能力。 |
| `src/Tools/TianShu.Tools.Memory` | 记忆工具。 |
| `src/Tools/TianShu.Tools.Search` | 搜索工具。 |
| `src/Tools/TianShu.Tools.Shell` | shell 工具。 |
| `src/Hosting/TianShu.AppHost.Tools`、`TianShu.AppHost.Tools.Runtime` | 现有宿主工具运行时。 |

## 3. 接口骨架归属

归属项目：`src/Contracts/TianShu.Contracts.Tools`。

```csharp
public interface ITianShuTool
{
    ToolDescriptor Descriptor { get; }

    ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationEnvelope invocation,
        ToolInvocationContext context,
        CancellationToken cancellationToken);
}

public sealed class TianShuToolHandlerAdapter : ITianShuTool
{
    public TianShuToolHandlerAdapter(
        ITianShuToolHandler handler,
        Func<ToolInvocationContext, TianShuToolInvocationContext>? contextFactory = null);
}

public sealed record ToolDescriptor(
    string ToolId,
    ToolKind Kind,
    JsonSchemaRef InputSchema,
    JsonSchemaRef OutputSchema,
    PermissionDeclaration Permissions,
    SideEffectProfile SideEffects,
    AuditProfile Audit);
```

Kernel-specific result types 归属 `TianShu.Contracts.Kernel`。

`ToolDescriptor` 不允许以空治理信息进入运行时目录。若工具未显式传入 `PermissionDeclaration`、`SideEffectProfile` 或 `AuditProfile`，契约层会按 `ToolId`、`ToolApprovalRequirement` 和 `ToolConcurrencyClass` 生成保守默认值：

- Shell / exec / stdin 类工具默认 `SideEffectLevel.HostMutation`，必须 human gate，审计事件必填。
- Required approval 或 Exclusive 工具默认 `SideEffectLevel.WorkspaceWrite`，必须 human gate，审计事件必填。
- SharedReadOnly 工具默认 `SideEffectLevel.ReadOnly`，不需要 human gate，但仍必须审计。
- 任何 descriptor 的 side effect 不得为 `Unspecified`。

`ToolDescriptor.ToModuleDescriptor` 必须将工具能力投影为 Module Plane 通用 `ModuleDescriptor`，并保留 permission、side effect、audit、schema ref、trust level 与 implementation binding。Module Plane 只读取该投影，不重新解释工具私有实现。

Execution Runtime 通过 `IExecutionRuntimeToolBridge` 消费 `ToolInvocationStep` 与 `ITianShuTool`：

```csharp
public interface IExecutionRuntimeToolBridge
{
    Task<RuntimeStepResult> ExecuteAsync(
        ToolInvocationStep step,
        ExecutionRuntimeContext context,
        ITianShuTool tool,
        CancellationToken cancellationToken);
}
```

bridge 必须拒绝 `ToolInvocationStep.InputEnvelope.ToolId` 与 `ToolDescriptor.ToolId` 不一致的调用。

## 4. 调用链路

```text
AI ToolUse
  -> KernelTool 或 CapabilityTool
  -> Stable Kernel Core validate
  -> RuntimeStep
  -> Execution Runtime
  -> Tool Module
```

## 5. 验收标准

- 工具不能绕过 Execution Runtime。
- 高副作用工具必须声明 permission、side effect、human gate。
- 工具结果必须可脱敏、可诊断、可回放。
- 内置工具 provider 的 descriptor 必须有 input schema、permission、side effect、audit 和 implementation binding。
- 工具能力必须能投影为 `ModuleDescriptor`，供 Module Plane discovery、trust level、capability set 和 health / smoke check 使用。
