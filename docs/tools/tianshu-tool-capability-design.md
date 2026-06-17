# TianShu 受治理能力面设计

## 1. 文档定位

本文件定义 TianShu v0.7 能力面扩展的正式设计基线。能力面包括模型可请求的工具、模块能力、结构化上下文能力和 Memory 能力。任何能力只要可能读取外部状态、改变工作区、调用远端服务、写入长期状态或影响后续 Kernel 决策，都必须经过 StageGraph、GovernanceEnvelope、descriptor、Execution Runtime bridge 和诊断投影。

能力面属于 Module Plane 与 Execution Runtime 的交界，不属于 provider 私有协议。Provider 只能看到已批准的 tool surface 和 provider input，不能自行开放 write、apply_patch、shell、MCP、Memory 或上下文裁切能力。

## 2. 涉及项目

| 项目 | 当前/目标责任 |
| --- | --- |
| `src/Contracts/TianShu.Contracts.Tools` | 工具 descriptor、schema、permission、side effect、audit、tool invocation envelope、tool result projection。 |
| `src/Contracts/TianShu.Contracts.Modules` | 模块 descriptor、capability projection、trust、health、discovery、loading 与治理 admission 通用契约。 |
| `src/Contracts/TianShu.Contracts.Kernel` | `GovernanceEnvelope`、`RuntimeStep`、`ToolInvocationStep`、`ModuleCapabilityStep`、`ContextPolicy`、`ApprovedContextPolicy`。 |
| `src/Contracts/TianShu.Contracts.Memory` | Memory retrieve / form / supersede / mutation 合同、Memory context candidate projection。 |
| `src/Contracts/TianShu.Contracts.Provider` | Provider-neutral input、provider invocation request、provider tool surface projection。 |
| `src/Execution/TianShu.Execution.Runtime` | 执行 RuntimeStep，调用 provider bridge、tool bridge、module bridge、context bridge，产出 metrics / diagnostics / evidence。 |
| `src/Core/TianShu.RuntimeComposition` | 为 CLI 等 Host Gateway 组合受控 provider/tool/module binding；不得在 Experience Plane 直接开放能力。 |
| `src/Tools/TianShu.Tools.FileSystem` | 只读文件能力，例如 read_file、list_dir、grep、glob。 |
| `src/Tools/TianShu.Tools.FileSystemMutating` | 受治理工作区写入能力，例如 write、apply_patch。 |
| `src/Tools/TianShu.Tools.Shell` | 受治理 shell 能力。 |
| `src/Tools/TianShu.Tools.McpResources` | MCP resource / tool 能力适配。 |
| `src/Tools/TianShu.Tools.Memory` | Memory tool surface，必须通过 Memory Module 或治理工具桥执行。 |
| `src/Hosting/TianShu.AppHost.Tools`、`src/Hosting/TianShu.AppHost.Tools.Runtime` | 旧宿主工具实现迁移输入，不是新能力面的最终授权源。 |

未来若新增能力项目，必须先补齐本文件中的项目归属、descriptor、治理边界、失败关闭规则和验收标准。

## 3. 统一调用链路

模型可见能力不能直接等于运行时可执行能力。正式调用链路是：

```text
Provider tool request / Kernel operation request
  -> Control Plane normalized operation
  -> Kernel StageGraph decision
  -> GovernanceEnvelope + descriptor admission
  -> RuntimeStep
  -> Execution Runtime bridge
  -> Tool Module / Module capability / Provider-neutral context bridge
  -> Result projection + diagnostics + evidence
```

任何一步缺失都必须 fail closed。特别是 provider 返回了工具名，也不表示工具可以执行；Execution Runtime 必须再次用真实 descriptor 校验 allow-list、副作用上限、human gate、schema 和 module admission。

## 4. 统一合同骨架

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

public sealed record ToolDescriptor(
    string ToolId,
    ToolKind Kind,
    JsonSchemaRef InputSchema,
    JsonSchemaRef OutputSchema,
    PermissionDeclaration Permissions,
    SideEffectProfile SideEffects,
    AuditProfile Audit,
    ToolImplementationBinding Binding);

public sealed record ToolInvocationEnvelope(
    CallId CallId,
    string ToolId,
    string Operation,
    StructuredValue Input,
    PermissionEnvelope Permission,
    SideEffectProfile SideEffect,
    MetadataBag Metadata);

public sealed record ToolInvocationContext(
    string RuntimeStepId,
    string SourceIntentId,
    string SourceGraphId,
    string SourceStageId,
    string SourceKernelOperationId,
    string? WorkingDirectory,
    MetadataBag Metadata);
```

归属项目：`src/Execution/TianShu.Execution.Runtime`。

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

bridge 必须执行以下校验：

- `ToolInvocationStep.InputEnvelope.ToolId` 与 `ToolDescriptor.ToolId` 必须一致。
- `ToolDescriptor.IsAllowedBy(GovernanceEnvelope)` 必须通过。
- 输入必须通过 descriptor schema 或等价 typed contract 校验。
- RuntimeStep 的 source ids 必须通过 `ToolInvocationContext` 传给工具 handler，并进入结果、审计和诊断投影。
- human gate 要求为真时必须存在批准引用；拒绝或缺失时返回 `approval-required` 或 `blocked`，不能调用 handler。
- handler 异常、超时、取消、治理拒绝和 schema 错误必须投影为不同 failure code。

## 5. 默认开放原则

默认 CLI 新 Kernel->Runtime loop 只开放低风险、可审计的只读文件能力。其他能力必须显式进入 StageGraph 和 GovernanceEnvelope：

| 能力 | 默认 provider tool surface | 最低副作用等级 | Human gate | 默认失败方式 |
| --- | --- | --- | --- | --- |
| `read_file` / `list_dir` / `grep` / `glob` | 可开放 | `ReadOnly` | 否 | 越权、越界或缺文件时结构化失败。 |
| `write` | 不默认开放 | `WorkspaceWrite` | 是 | 未批准、绝对路径、越界、冲突时 fail closed。 |
| `apply_patch` | 不默认开放 | `WorkspaceWrite` | 是 | patch 解析失败、冲突、越界或未批准时 fail closed。 |
| `shell` | 不默认开放 | `HostMutation` | 是 | 未批准、cwd 越界、超时、危险命令或 env 风险时 fail closed。 |
| MCP resource | 不默认开放 | `ReadOnly` | 按 manifest | server 缺失、schema 不可信或未授权时 degraded / fail closed。 |
| MCP tool | 不默认开放 | 由 manifest 声明，不能低估 | 是，除非显式只读且被策略允许 | server/tool 未授权、schema 不匹配或远端失败时 fail closed。 |
| Memory retrieve | 不默认开放 | `ReadOnly` | 否或按策略 | 未授权或缺 evidence ref 时 fail closed / dropped reason。 |
| Memory form / supersede | 不默认开放 | `ExternalMutation` | 是 | 未批准、缺 supersede link 或写入审计失败时 fail closed。 |
| 结构化上下文裁切 | 不作为 provider tool | 无直接副作用 | 不适用 | 缺 policy、source mismatch、预算缺失时 fail closed。 |

未出现在 provider tool surface 的能力被模型请求时，必须返回结构化 `unknown_or_unopened_tool` 类 failure code，不得尝试按名称动态发现实现。

## 6. Workspace mutation 合同

`write` 与 `apply_patch` 共享 workspace mutation 合同。工具 handler 可以继续保持不同输入形态，但进入 Execution Runtime 后必须收敛成同一组计划、预览、审批、应用和补偿投影。

当前代码基底：

- `src/Tools/TianShu.Tools.FileSystemMutating` 已提供 `write` 与 `apply_patch` descriptor。
- `write` 当前支持 `path`、`content`、`append`，并通过 `ITianShuFileMutationServices.IsWritePathAllowed` 做写入许可检查。
- `apply_patch` 当前支持结构化 freeform patch，已验证相对路径、cwd 越界、writable root、patch grammar、expected lines 和基本 add / delete / update / move 操作。
- P27.2 之后的正式合同必须补齐 change plan、diff preview、approval refs、content hash 冲突检测、artifact / audit refs 与补偿记录；这些属于 P27.3/P27.4 的实现和测试基线。

归属项目：

- 合同：`src/Contracts/TianShu.Contracts.Tools`
- Runtime step：`src/Contracts/TianShu.Contracts.Execution`
- 执行桥：`src/Execution/TianShu.Execution.Runtime`
- 实现模块：`src/Tools/TianShu.Tools.FileSystemMutating`
- 组合入口：`src/Core/TianShu.RuntimeComposition`

目标合同骨架：

```csharp
public sealed record WorkspaceMutationPlan(
    string PlanId,
    string ToolId,
    WorkspaceMutationKind Kind,
    IReadOnlyList<WorkspaceMutationTarget> Targets,
    string DiffPreviewRef,
    string ChangePlanArtifactRef,
    IReadOnlyList<string> ApprovalRefs,
    MetadataBag Metadata);

public sealed record WorkspaceMutationTarget(
    string WorkspaceRelativePath,
    string ResolvedPathRef,
    WorkspaceMutationOperation Operation,
    string? MoveToWorkspaceRelativePath,
    string? ExpectedBeforeHash,
    string? PlannedAfterHash,
    bool RequiresExistingFile,
    bool AllowsCreate);

public sealed record WorkspaceMutationResultProjection(
    string PlanId,
    string ToolId,
    WorkspaceMutationStatus Status,
    IReadOnlyList<WorkspaceMutationAppliedTarget> AppliedTargets,
    string AuditRef,
    string? CompensationRef,
    string? FailureCode);
```

正式执行顺序：

1. 解析工具输入，生成候选 target 列表。
2. 通过 workspace resolver 将每个 target 解析为 workspace-relative path 与安全 path ref。
3. 拒绝绝对路径、路径逃逸、未授权 writable root、空路径和不受支持的操作。
4. 读取目标快照，记录 exists、before hash、size、encoding hint 和只读/锁定状态。
5. 生成 change plan artifact 与 diff preview artifact。
6. 若 `RequiresHumanGate=true`，在预览后、应用前等待审批；审批结果必须写入 approval refs。
7. 应用前重新读取目标 hash；hash 不一致必须返回 conflict，不得覆盖。
8. 执行所有变更；多文件 patch 必须先完成全量验证，再进入应用阶段。
9. 写入 result projection、audit ref、trace ref、diagnostics ref。
10. 应用阶段失败时必须尝试补偿或记录不可补偿原因；不得把部分成功伪造成整体成功。

失败码必须结构化，至少覆盖：

- `workspace_mutation_path_empty`
- `workspace_mutation_absolute_path`
- `workspace_mutation_path_escape`
- `workspace_mutation_write_not_allowed`
- `workspace_mutation_approval_required`
- `workspace_mutation_approval_rejected`
- `workspace_mutation_conflict`
- `workspace_mutation_patch_parse_failed`
- `workspace_mutation_patch_target_missing`
- `workspace_mutation_partial_apply_failed`
- `workspace_mutation_compensation_failed`

## 7. write 能力

`write` 表达对单个工作区文件的受控写入。它不是任意路径写入，也不是 provider 可自行决定的持久化操作。

归属项目：

- 合同：`src/Contracts/TianShu.Contracts.Tools`
- 实现模块：`src/Tools/TianShu.Tools.FileSystemMutating`
- 运行时桥：`src/Execution/TianShu.Execution.Runtime`
- 组合入口：`src/Core/TianShu.RuntimeComposition`

设计骨架：

```csharp
public sealed record WorkspaceWritePlan(
    string WorkspaceRelativePath,
    string? ExpectedBeforeHash,
    string NewContentPreviewRef,
    string DiffPreviewRef,
    string ChangePlanArtifactRef,
    WriteConflictPolicy ConflictPolicy,
    bool AllowsCreate,
    IReadOnlyList<string> ApprovalRefs);

public sealed record WorkspaceWriteResult(
    string ArtifactRef,
    string? BeforeHash,
    string AfterHash,
    string AuditRef,
    bool Applied);
```

规则：

- 路径必须是 workspace-relative path；绝对路径即使落在工作区内也必须拒绝。
- 写入前必须通过 workspace resolver 解析并证明目标仍在允许工作区内。
- 写入前必须生成 change plan 与 diff / preview artifact。
- `append=true` 必须在 plan 中显示追加位置和预期 before hash，不得绕过冲突检测。
- `RequiresHumanGate=true` 时必须有批准引用。
- `ExpectedBeforeHash` 不匹配时必须返回冲突，不得覆盖。
- 新文件写入必须声明 `AllowsCreate=true`；覆盖既有文件必须声明 expected before hash。
- 成功或失败都必须产出 trace / diagnostics；成功还必须产出 before / after hash 与 audit ref。

## 8. apply_patch 能力

`apply_patch` 表达结构化补丁应用，不能退化为 raw write。它可以与 `write` 共用 mutating filesystem module，但必须有独立 descriptor、schema 和冲突检测。

设计骨架：

```csharp
public sealed record WorkspacePatchPlan(
    string PatchFormat,
    string PatchArtifactRef,
    IReadOnlyList<PatchTarget> Targets,
    string DiffPreviewRef,
    IReadOnlyList<string> ApprovalRefs);

public sealed record PatchTarget(
    string WorkspaceRelativePath,
    string ExpectedContentHash,
    PatchTargetOperation Operation);
```

规则：

- patch 必须先解析成 typed patch plan；解析失败不能进入文件写入。
- 每个 target 都必须通过 workspace resolver。
- 新增、删除、修改、移动必须分别记录 target operation。
- `Add File` 目标已存在时必须冲突；`Delete File` / `Update File` 目标缺失时必须冲突。
- `Move to` 必须同时校验 source 和 destination，destination 已存在时必须按冲突处理，除非合同显式允许覆盖并有 before hash。
- expected lines 匹配失败是冲突，不是 handler 内部异常。
- 冲突检测必须在应用前完成；部分应用失败时必须投影 compensation / rollback record。
- 默认 CLI tool surface 不开放 `apply_patch`；只有 P27.2-P27.4 合同、实现和测试完成后才能进入显式审批态。

## 9. shell 能力

`shell` 表达宿主命令执行，风险高于工作区写入。它可以改变宿主状态、读取环境变量、启动进程、访问网络或产生长时间运行的子进程，因此默认必须 human gate。任何 shell 能力都不能作为 provider 私有工具直接执行；provider 只能看到已批准、已收窄的 tool surface，真实执行必须进入 `ToolInvocationStep -> Execution Runtime shell bridge -> Shell Tool Module -> Host governed shell services`。

当前代码基底：

- `src/Tools/TianShu.Tools.Shell` 已提供 `shell`、`local_shell`、`shell_command`、`exec_command`、`write_stdin` descriptor 和 schema。
- `src/Contracts/TianShu.Contracts.Tools` 已提供 `ITianShuShellToolServices`、`TianShuShellToolRequest`、`TianShuShellToolResult` 作为 Tool Module 调用宿主 shell runtime 的最小受治理入口。
- `src/Hosting/TianShu.AppHost.Tools.Runtime` 已保留旧 AppHost shell executor、sandbox、managed network、unified exec 和输出格式化实现，可作为迁移输入。
- P27.5 的合同设计不意味着 CLI 默认开放 shell；P27.6 之前，shell 不得进入新 Kernel->Runtime loop 的默认 provider tool surface。

归属项目：

- 合同：`src/Contracts/TianShu.Contracts.Tools`
- 实现模块：`src/Tools/TianShu.Tools.Shell`
- 运行时桥：`src/Execution/TianShu.Execution.Runtime`
- 组合入口：`src/Core/TianShu.RuntimeComposition`
- 旧实现迁移输入：`src/Hosting/TianShu.AppHost.Tools`、`src/Hosting/TianShu.AppHost.Tools.Runtime`

设计骨架：

```csharp
public sealed record ShellExecutionRequest(
    string ToolId,
    ShellCommandShape CommandShape,
    ShellCommandPayload Command,
    string? RequestedWorkingDirectory,
    ShellSandboxRequest Sandbox,
    ShellEnvironmentRequest Environment,
    ShellTimeoutPolicy Timeout,
    ShellOutputPolicy Output,
    IReadOnlyList<string> ApprovalRefs,
    MetadataBag Metadata);

public sealed record ShellExecutionPlan(
    string PlanId,
    string ToolId,
    string CommandRef,
    string CommandDisplayRedacted,
    string WorkingDirectoryRef,
    ShellSandboxDecision SandboxDecision,
    TimeSpan EffectiveTimeout,
    IReadOnlyList<string> RedactedEnvironmentKeys,
    ShellOutputLimit OutputLimit,
    IReadOnlyList<string> ApprovalRefs,
    string TranscriptRef,
    string AuditRef);

public sealed record ShellExecutionResult(
    string PlanId,
    ShellExecutionStatus Status,
    int ExitCode,
    bool TimedOut,
    bool OutputTruncated,
    string StdoutRef,
    string StderrRef,
    string TranscriptRef,
    string DiagnosticsRef,
    string AuditRef,
    string? FailureCode);
```

### 9.1 工具形态与输入归一化

Shell 工具域允许多个 provider-facing tool id，但必须归一化到同一个 shell execution contract：

| Tool id | 输入形态 | 归一化结果 | 说明 |
| --- | --- | --- | --- |
| `shell` | `command` 为 string array 或 JSON 字符串化数组。 | `ShellCommandShape.Arguments`。 | 适合显式 argv；Windows 可前置 `powershell.exe -Command`。 |
| `local_shell` | `command` 为 string array 或 JSON 字符串化数组。 | `ShellCommandShape.Arguments`。 | 平台本地 shell 入口；仍受同一治理合同约束。 |
| `shell_command` | `command` 为 shell script string，允许 `login`。 | `ShellCommandShape.Script`。 | 由 runtime 选择默认 shell 包装，不允许 provider 自行改变 sandbox。 |
| `exec_command` | `command` / `cmd`、`cwd`、`login`、`max_output_tokens`。 | `ShellCommandShape.SessionStart`。 | 启动统一 exec session；必须返回 session id 与 transcript refs。 |
| `write_stdin` | `session_id` / `sessionId`、`text` / `chars`、`close`。 | `ShellCommandShape.SessionInput`。 | 只能写入当前 run 已批准且未关闭的 exec session。 |

归一化规则：

- 输入 schema 通过前不得解析为命令。
- `command` 不能为空；数组命令必须剔除空白参数，stringified JSON 数组必须解析失败关闭。
- `shell_command` 的 `login=true` 必须被当前 `ShellEnvironmentPolicy.AllowLoginShell` 或更严格策略允许；否则失败关闭。
- `exec_command` 和 `write_stdin` 必须绑定同一 thread / turn / execution lineage；跨 thread 或过期 session 必须拒绝。
- provider tool surface 只允许暴露当前治理批准的一个或一组 shell tool id；模型请求未开放 shell tool 必须返回 `unknown_or_unopened_tool`。

### 9.2 审批与治理

Shell 的最低副作用等级是 `HostMutation`。即使命令看似只读，也不能低于 `HostMutation`，除非未来引入可证明只读的受限 runner 并另立 tool id。

Runtime bridge 必须在调用 `ITianShuShellToolServices` 前完成：

- descriptor allow-list 校验：`ToolDescriptor.ToolId` 必须在 `GovernanceEnvelope.AllowedToolIds` 内。
- 副作用校验：`GovernanceEnvelope.MaxSideEffectLevel` 必须不低于 `HostMutation`。
- human gate 校验：shell descriptor 必须 `RequiresHumanGate=true`，且 runtime step 必须携带 approval refs。
- command preview 校验：审批前必须生成 redacted command preview、cwd ref、sandbox request、timeout、output cap 和 env diff。
- approval decision 校验：拒绝、缺失、过期或不匹配 command hash 的批准必须返回 `shell_approval_required` 或 `shell_approval_rejected`，不得调用 executor。
- sandbox escalation 校验：`sandbox_permissions=require_escalated` 或 `additional_permissions` 只能作为审批请求输入，不能自动提升权限。
- dangerous command gate：明显高风险命令模式必须在 executor 启动前 fail closed，例如递归强制删除、格式化磁盘、关机/重启、磁盘/启动项修改等；该 gate 不是完整 shell sandbox，只是高风险预检层。

### 9.3 cwd 与 sandbox

cwd 是 shell 安全边界的一部分，不是展示字段。Runtime 必须用 workspace/environment policy 解析 cwd：

- `workdir` / `cwd` 为空时使用 RuntimeStep 的 `WorkingDirectory`。
- 相对路径必须相对 `WorkingDirectory` 解析。
- 绝对 cwd 必须通过 workspace / sandbox policy；不在允许 root 内时返回 `shell_cwd_not_allowed`。
- cwd 不存在时默认失败关闭，除非未来合同显式允许创建工作目录且该创建作为单独 workspace mutation 受审计。
- sandbox policy 必须合并 session policy、tool request、approval amendment 和 host constraints；合并结果只能收紧，不能超过审批范围。
- network、filesystem read/write、process spawn、login shell、managed network proxy 都必须进入 plan 和 audit projection。

### 9.4 环境变量与脱敏

Shell 不得继承完整宿主环境。环境注入必须由 `ShellEnvironmentPolicy` 或等价 typed policy 生成：

- 默认只注入最小 allow-list，例如 PATH / HOME / TEMP 等运行必需项，具体列表由宿主策略控制。
- API key、token、password、secret、credential、cookie、authorization 等名称模式必须默认脱敏，且不得出现在 provider-facing output、diagnostics 或 transcript plaintext 中。
- approval preview 只展示环境变量 key、来源层和 redaction status，不展示 secret value。
- transcript 可记录 redaction placeholder，例如 `<redacted:OPENAI_API_KEY>`，不能记录明文。
- 命令输出、stderr、异常消息、managed network diagnostics 和 failure details 都必须经过同一 redactor。
- 若 redactor 缺失或无法证明输出已脱敏，高风险输出不得回流模型，只能返回 `shell_redaction_unavailable` 或 artifact ref。

### 9.5 timeout、输出截断与 transcript

每次 shell 执行必须有有效 timeout 和输出上限：

- 默认 timeout 由 execution policy 提供；工具输入可请求更短 timeout；更长 timeout 必须审批。
- timeout 必须取消或终止进程树；不能只停止等待而留下孤儿进程。
- stdout 与 stderr 必须分别采集，再生成聚合 preview；provider-facing 文本可以截断，但 transcript artifact 必须记录截断状态。
- 输出截断必须可观察：`OutputTruncated=true`、`originalByteCount` / `returnedByteCount` / `maxOutputBytes` 或 token 等价字段必须投影。
- 非零 exit code 是受控失败状态，不是 runtime crash；必须保留 exit code、stderr ref、transcript ref。
- timeout 是 `timeout` 状态；取消是 `cancelled` 状态；两者不能混用。
- unified exec session 的 `write_stdin` 必须记录 chunk id、session id、close flag、wall time、输出增量 ref 和最终 session state。

### 9.6 Transcript 与审计投影

Shell transcript 是验收和二次审查证据，不等同 provider-facing output。正式投影至少包含：

- `runtimeBoundary = "tool.shell_execution"`。
- `planId`、`toolId`、`commandHash`、`commandDisplayRedacted`。
- `workingDirectoryRef`，不得包含未脱敏绝对私有路径；必要时使用 `workspace://...` 或 path hash。
- `sandboxDecisionRef`、`approvalRefs`、`auditRef`、`traceRef`。
- `startedAt`、`endedAt`、`durationMs`、`timeoutMs`。
- `exitCode`、`timedOut`、`cancelled`、`outputTruncated`。
- `stdoutRef`、`stderrRef`、`transcriptRef`、`diagnosticsRef`。
- `redactionStatus` 与 `redactedEnvironmentKeys`。

provider-facing tool result 只能返回摘要和必要输出片段；完整 transcript 通过 artifact / evidence ref 提供。任何 raw command、raw env、raw cwd 或 raw output 若可能含 secret，都不得直接进入 provider input。

### 9.7 失败码

失败码必须结构化，至少覆盖：

- `shell_services_unavailable`
- `shell_schema_invalid`
- `shell_command_empty`
- `shell_tool_not_opened`
- `shell_approval_required`
- `shell_approval_rejected`
- `shell_cwd_not_allowed`
- `shell_sandbox_rejected`
- `shell_dangerous_command_rejected`
- `shell_escalation_not_approved`
- `shell_login_not_allowed`
- `shell_environment_rejected`
- `shell_redaction_unavailable`
- `shell_timeout`
- `shell_cancelled`
- `shell_nonzero_exit`
- `shell_output_truncated`
- `shell_session_not_found`
- `shell_session_not_owned`
- `shell_transcript_write_failed`

### 9.8 P27.6 / P27.7 实施约束

P27.6 实现时必须以本节为准，不能简单复用旧 AppHost executor 成功返回文本作为完成标准。最小落地应做到：

- 新 Execution Runtime shell bridge 接受 `ToolInvocationStep`，完成 descriptor / governance / approval / schema / side effect 校验。
- `TianShu.Tools.Shell` handler 只调用 `ITianShuShellToolServices`，不得直接启动进程。
- RuntimeComposition 只有在显式审批态才注册 shell tool surface；默认 CLI 新 loop 不开放 shell。
- AppHost 旧 executor 可作为 `ITianShuShellToolServices` 的执行后端，但必须补齐 plan、audit、redaction、timeout、output cap、transcript projection。
- P27.7 测试必须覆盖危险命令 gate、cwd 越界、env 脱敏、超时、非零退出码、输出截断、审批拒绝、未开放 shell 请求和 transcript ref。

## 10. MCP 能力

MCP 分为 resource 和 tool。resource 是只读上下文来源；tool 是远端可执行能力，可能有副作用。

归属项目：

- 合同：`src/Contracts/TianShu.Contracts.Tools` 与 `src/Contracts/TianShu.Contracts.Modules`。
- 配置与 manifest：`src/Core/TianShu.Configuration` 的 `TianShuMcpServerManifestConfiguration` 与 `modules/mcp-servers/*/server.toml`。
- 只读 resource 工具适配：`src/Tools/TianShu.Tools.McpResources`，现有 tool id 为 `list_mcp_resources`、`list_mcp_resource_templates`、`read_mcp_resource`。
- 运行时桥：`src/Execution/TianShu.Execution.Runtime` 的 `ExecutionRuntimeToolBridge` 与 `TianShuExecutionRuntime.RuntimeSteps`。
- 旧产品 MCP server / AppHost surface：`src/Hosting/TianShu.AppHost` 与 `src/Hosting/TianShu.AppHost.Tools.Runtime` 只能作为后端能力来源，不得绕过新 RuntimeStep / governance 合同。

### 10.1 当前代码基底

当前已有最小合同与 P27.9 新增 MCP tool 合同：

```csharp
public interface ITianShuMcpResourceToolServices
{
    Task<TianShuMcpListResourcesResult> ListResourcesAsync(string? server, string? cursor, CancellationToken cancellationToken);
    Task<TianShuMcpListResourceTemplatesResult> ListResourceTemplatesAsync(string? server, string? cursor, CancellationToken cancellationToken);
    Task<TianShuMcpReadResourceResult> ReadResourceAsync(string server, string uri, CancellationToken cancellationToken);
}

public sealed record TianShuMcpResourceEntry(string Server, JsonElement Resource);
public sealed record TianShuMcpResourceTemplateEntry(string Server, JsonElement Template);
public sealed record TianShuMcpReadResourceResult(string Server, string Uri, JsonElement Result);

public sealed record TianShuMcpToolDescriptor(
    string ServerId,
    string ToolName,
    string ToolId,
    string DisplayName,
    string Description,
    JsonElement InputSchema,
    JsonElement? OutputSchema,
    SideEffectLevel SideEffectLevel,
    bool RequiresHumanGate,
    IReadOnlyList<string> RequiredScopes,
    ToolImplementationKind ImplementationKind);

public interface ITianShuMcpToolServices
{
    Task<TianShuMcpToolResult> InvokeMcpToolAsync(TianShuMcpToolRequest request, CancellationToken cancellationToken);
}
```

不得把旧 AppHost MCP tool call 直接当成新内核 MCP tool evidence。P27.9 起，`TianShu.Tools.McpResources` 同时承载 read-only resource 工具和显式传入的 MCP tool binding：resource 通过 `ITianShuMcpResourceToolServices` 执行；远端 tool 先由 `TianShuMcpToolDescriptor` 投影为统一 `ToolDescriptor`，再通过 `ITianShuMcpToolServices` 执行。`KernelRuntimeTurnLoopComposition.CreateTools(includeMcp: true, ...)` 是当前新 loop 的显式接入口；默认 CLI 新 loop 仍不开放 MCP。

### 10.2 Server Manifest 合同

MCP server 必须先有可审计 manifest，不能按模型请求动态连接未知 server。现有 manifest 字段基线为：

```csharp
public sealed class McpServerManifestValue
{
    public string Id { get; set; }
    public bool Enabled { get; set; }
    public bool Required { get; set; }
    public string Transport { get; set; } // stdio / http / websocket
    public string? Command { get; set; }
    public IReadOnlyList<string> Args { get; set; }
    public IReadOnlyDictionary<string, string> Env { get; set; }
    public IReadOnlyList<string> EnvVars { get; set; }
    public string? Cwd { get; set; }
    public string? Url { get; set; }
    public string? BearerTokenEnvVar { get; set; }
    public int? StartupTimeoutMs { get; set; }
    public int? ToolTimeoutMs { get; set; }
    public IReadOnlyList<string> EnabledTools { get; set; }
    public IReadOnlyList<string> DisabledTools { get; set; }
}
```

正式治理还必须补齐以下逻辑字段，若暂时没有独立字段，P27.9 必须从 manifest + discovery + default policy 生成等价 projection：

- `serverId`：稳定、大小写规范化的 server 标识。
- `trustLevel`：`BuiltIn` / `UserConfigured` / `WorkspaceConfigured` / `ThirdParty` / `Unknown`。
- `capabilityList`：server 声明和运行时发现到的 resource / resource template / tool 名称。
- `toolPermissions`：每个远端 tool 的 required scopes、是否 human gate、side effect level、affected resources、network / filesystem / external mutation 标记。
- `schemaRefs`：resource list/read schema、tool input schema、tool output schema 的 hash 或 ref。
- `healthStatus`：`Healthy` / `Unavailable` / `AuthRequired` / `SchemaInvalid` / `Disabled` / `Unknown`。
- `diagnosticsRefs`：连接、鉴权、schema 校验和工具发现的诊断引用。

Manifest 解析规则：

- `enabled=false` 的 package 或 server 不得进入 provider tool surface。
- `required=true` 只表示产品配置期望该 server 存在；运行时不可达仍必须 fail closed 或 degraded，不得伪造成功。
- `env` 和 `env_vars` 只能作为 secret reference 或 allow-list key；不得把 secret value 投影给 provider。
- `cwd` 必须落在 manifest package directory 或显式 workspace policy 允许范围内；越界 fail closed。
- `enabled_tools` / `disabled_tools` 必须在 discovery 后再次校验；二者冲突时按更保守的 disabled 处理。
- manifest 与 runtime discovery 不一致时，以更保守结果为准，并写入 diagnostics。

### 10.3 Resource 只读访问

MCP resource 是上下文来源，不是 mutation 能力。`list_mcp_resources`、`list_mcp_resource_templates`、`read_mcp_resource` 必须保持 `ReadOnly`，默认不要求 human gate，但必须受以下边界约束：

- server 必须来自已启用 manifest 或用户显式配置；未知 server 返回 `mcp_server_not_configured`。
- `read_mcp_resource(server, uri)` 的 `uri` 必须来自本轮或最近可审计 `list_mcp_resources` / template resolution 结果；不能让模型自由构造任意 URI。
- resource 读取结果必须投影 `server`、`uri`、`mimeType`、`contentLength` 或等价 metadata、`evidenceRef`、`retrievedAt`、`schemaHash`。
- resource 内容进入模型上下文时，只能作为 `ContextSourceCandidate` / `ContextSourceKind.McpResource` 或 read-only `toolResults[]`；必须带 evidence ref 或 dropped reason。
- resource 读取失败可以 degraded，但 degraded 必须显式投影 `mcp_resource_degraded`、原因、server、uri 和 diagnostics ref；不能以空内容伪装成功。
- resource 内容若可能包含 secret、raw header、本地绝对路径或外部凭据，必须先脱敏；无法证明脱敏时不得进入 provider input。

P27.9 实际合同骨架：

```csharp
public sealed record McpResourceEvidence(
    string ServerId,
    string Uri,
    string EvidenceRef,
    string? MimeType,
    string SchemaHash,
    DateTimeOffset RetrievedAt,
    StructuredValue Payload);
```

### 10.4 MCP Tool 治理

MCP tool 是远端 capability tool。它可以是只读、外部网络、外部 mutation、workspace mutation 或 host mutation，不能只因为来自 MCP 就默认为 `ReadOnly`。

远端 tool 进入 provider tool surface 前必须完成：

- server manifest 已启用且 health 不是 `Unavailable` / `SchemaInvalid` / `AuthRequired`。
- server discovery 返回 tool name、description、input schema；缺 schema 或 schema 不合法返回 `mcp_tool_schema_invalid`。
- 本地将 MCP tool 投影为普通 `ToolDescriptor`，tool id 必须稳定，例如 `mcp.{serverId}.{toolName}` 或等价命名，不能和内置 tool 冲突。
- `ToolDescriptor.Permissions` 必须来自 server manifest、tool metadata 和本地 policy 合并结果。
- `ToolDescriptor.SideEffects` 取 server manifest 与 tool descriptor 的更高风险值；未知副作用按 `ExternalMutation` 或更保守等级处理。
- 除非 manifest 明确声明只读且本地 policy 允许，否则 MCP tool 默认 `RequiresHumanGate=true`。
- provider 看到的 MCP tool schema 只能是已验证、已裁剪的 JSON schema；不得把 server 原始 metadata、auth token、私有路径或诊断正文带入 schema。

P27.9 实际合同骨架：

```csharp
public sealed record TianShuMcpToolDescriptor(
    string ServerId,
    string ToolName,
    string ToolId,
    string DisplayName,
    string Description,
    JsonElement InputSchema,
    JsonElement? OutputSchema,
    SideEffectLevel SideEffectLevel,
    bool RequiresHumanGate,
    IReadOnlyList<string> RequiredScopes,
    ToolImplementationKind ImplementationKind);

public interface ITianShuMcpToolServices
{
    Task<TianShuMcpToolResult> InvokeMcpToolAsync(TianShuMcpToolRequest request, CancellationToken cancellationToken);
}
```

### 10.5 RuntimeStep 映射

MCP resource 与 MCP tool 的 runtime 入口必须分开：

- resource list/read 可以作为 `ToolInvocationStep` 进入 `ExecutionRuntimeToolBridge`，但只允许绑定到 `TianShu.Tools.McpResources`，输出为 read-only tool result 或 context evidence。
- MCP tool 必须作为普通 `ToolInvocationStep(capabilityToolId = "mcp.{serverId}.{toolName}")` 进入 `ExecutionRuntimeToolBridge`，并由 MCP tool adapter 调用 `ITianShuMcpToolServices`。
- `ExecutionRuntimeToolBridge` 必须在 output 顶层投影 `toolRuntimeBoundary`，并在每个 `toolResults[]` 条目投影 `runtimeBoundary`；MCP resource 为 `tool.mcp_resource`，MCP tool 为 `tool.mcp_tool`。
- 如果未来某些 server 被建模为独立 Module，也必须先投影为 `ModuleCapabilityDescriptor`，再通过 `ModuleCapabilityStep`，不得由 provider 直接调用 server。
- 未绑定 MCP tool/resource、server 未启用、tool 未授权、schema hash 不匹配或 governance 不满足时，必须在 executor 前 fail closed。当前 runtime 对未绑定 `mcp.*` 返回 `mcp_tool_not_opened`，对未绑定 resource 工具返回 `mcp_resource_not_opened`。

`ToolInvocationStep` 输出必须保留：

- `runtimeBoundary = "tool.mcp_resource"` 或 `runtimeBoundary = "tool.mcp_tool"`。
- `serverId`、`toolName` / `uri`、`callId`、`schemaHash`、`manifestRef`。
- `remoteTraceRef`、`diagnosticsRef`、`auditRef`。
- `status`、`failureCode`、`degradedReason`。
- 脱敏后的 `outputPreview`，以及完整结果 artifact / evidence ref。

### 10.6 连接失败、降级与失败码

Resource 与 tool 的失败策略不同：

- Resource list/read 可 degraded：server 不可达、auth 缺失、超时、schema 不完整时可返回 dropped reason / degraded evidence，但不得返回成功内容。
- Tool invocation 必须 fail closed：server 不可达、auth 缺失、schema 不合法、tool 未授权、human gate 缺失、结果不匹配 schema 都不能执行或不能晋升成功。

失败码至少覆盖：

- `mcp_server_not_configured`
- `mcp_server_disabled`
- `mcp_server_unavailable`
- `mcp_server_auth_required`
- `mcp_server_manifest_invalid`
- `mcp_server_health_unknown`
- `mcp_resource_not_listed`
- `mcp_resource_not_opened`
- `mcp_resource_read_failed`
- `mcp_resource_degraded`
- `mcp_tool_not_opened`
- `mcp_tool_not_authorized`
- `mcp_tool_schema_invalid`
- `mcp_tool_input_invalid`
- `mcp_tool_output_invalid`
- `mcp_tool_side_effect_unknown`
- `mcp_tool_human_gate_required`
- `mcp_tool_remote_failure`
- `mcp_tool_timeout`

### 10.7 P27.9 / P27.10 实施边界

P27.9 已实现最小可验证闭环：

- 通过显式 `TianShuMcpToolDescriptor` 生成 MCP tool binding projection；manifest/discovery 到 descriptor 的完整自动生成仍留给 P27.10 后续补测与 P27 后续收敛。
- resource list/read 进入统一 `ToolInvocationStep -> ExecutionRuntimeToolBridge -> McpResourceToolProvider -> ITianShuMcpResourceToolServices`。
- MCP tool schema 投影为 `ToolDescriptor`，并进入 provider tool surface only when GovernanceEnvelope allow-list 同意。
- MCP tool invocation 进入统一 ToolUse / RuntimeStep / governance / metrics / diagnostics。
- 默认 CLI 新 loop 不开放 MCP；只有显式开启并具备 allow-list / human gate 时才开放。

P27.10 测试必须覆盖：

- 只读 resource 成功与 degraded。
- 未配置 server、disabled server、不可达 server。
- 远端 tool human gate 缺失。
- schema 缺失 / 不合法 / output 不匹配。
- tool side effect 不能被本地适配器降级。
- provider-facing output 不泄漏 bearer token、env、raw header 或本地绝对路径。

## 11. 结构化上下文能力

结构化上下文不是 provider tool。它是 Kernel 批准的 `ContextPolicy` 与 Execution Runtime context bridge 共同完成的 provider input 准备能力。

归属项目：

- 合同：`src/Contracts/TianShu.Contracts.Kernel`
- 执行：`src/Execution/TianShu.Execution.Runtime`
- Provider 输入：`src/Contracts/TianShu.Contracts.Provider`

规则：

- Kernel 在 StageGraph / Stage 边界生成并批准 `ApprovedContextPolicy`。
- Execution Runtime 只执行已批准 policy，并产出 `ProviderInputItem[]` 与 `ContextPolicyApplicationReport`。
- Provider Module 不得重新裁切、摘要、去重或丢弃上下文。
- token threshold、分层降级、supersede 取舍、可逆压缩和 checkpoint 必须作为 policy / report / trace 进入，不得静默发生。
- provider usage 缺失时可以使用 estimated token 作为 diagnostics，但不能晋升为真实 usage 或 cost。
- 缺 evidence ref、source mismatch、预算缺失或 policy 不合法时必须 fail closed 或记录 dropped reason。

## 12. Memory 能力

Memory 能力既可以表现为 `ModuleCapabilityStep`，也可以经由受控 Memory tool surface 暴露给模型。两条路径都必须经过同一治理边界。

归属项目：

- 合同：`src/Contracts/TianShu.Contracts.Memory`
- 默认实现：`src/Core/TianShu.IdentityMemory`
- 工具适配：`src/Tools/TianShu.Tools.Memory`
- Runtime 调用：`src/Execution/TianShu.Execution.Runtime`

规则：

- `retrieve` 是只读能力，只能返回 typed projection 与 `MemoryContextCandidateProjection`。
- `form` 表达形成候选记忆；正式写入必须受 policy、human gate 和 audit 约束。
- `supersede` 表达事实取代，必须保留 supersede link、旧记录引用和审计记录。
- `forget`、`delete`、`supersede` 不能静默覆盖长期事实。
- Memory 检索结果只能作为 `ContextSourceCandidate(ContextSourceKind.MemoryRecord)` 进入 ContextPolicy，不得由 Memory Module 自行决定最终 inclusion/drop。
- Memory Module 不保存完整模型思考链或内部推理轨迹，只保存用户确认事实、工具证据、artifact 引用、反馈、引用记录和可审计摘要。

## 13. 失败关闭与诊断投影

所有能力必须遵守统一失败关闭原则：

- 未注册 descriptor：fail closed。
- descriptor 与 RuntimeStep / invocation envelope 不一致：fail closed。
- tool id 未在 allow-list：fail closed。
- module id 未在 allowed module set：fail closed。
- 副作用等级超过 envelope 上限：fail closed。
- human gate 缺失、拒绝或过期：不执行，返回 `approval-required` / `blocked`。
- schema 缺失或输入不合法：fail closed。
- runtime bridge 找不到 binding：fail closed。
- handler 成功但输出不匹配 schema：标记失败，不晋升为成功。

诊断投影必须包含：

- `runId`、`executionId`、`planId`、`graphId`、`stageId`、`stepId`。
- `toolId` 或 `moduleId`。
- governance decision、approval refs、side effect level、failure code。
- trace ref、diagnostics ref、artifact ref 或 transcript ref。
- secret / API key / raw header / 私有绝对路径脱敏结果。

## 14. 验收标准

- 所有模型可见能力都有 descriptor、schema、permission、side effect、audit 和 implementation binding。
- Provider tool surface 只包含 StageGraph 与 GovernanceEnvelope 同时允许的能力。
- hallucinated 或未开放工具必须结构化失败，不得动态绕过 allow-list。
- `write`、`apply_patch`、`shell`、MCP tool、Memory mutation 均必须覆盖 human gate。
- `write` / `apply_patch` 必须先生成 change plan 与 diff preview，再审批，再应用；应用前必须重新做 hash 冲突检测。
- `apply_patch` 的 add / delete / update / move target 必须全量验证后再执行，部分失败必须有 compensation / rollback 投影。
- MCP resource、Memory retrieve、workspace facts、tool evidence 进入上下文时必须带 evidence ref 或 dropped reason。
- ContextPolicy 只由 Kernel 批准，Execution Runtime 只执行，Provider 不重新裁切。
- Runtime 结果、metrics、diagnostics 与 replay summary 能从同一批事件重建能力调用关系。

## 15. v0.7 capability release gate

`tools/Test-TianShuV07CapabilityReleaseGate.ps1` 是 v0.7 受治理能力面的发布门禁入口，必须在 CI release workflow 中执行。该门禁不依赖真实 provider key，也不做外部网络调用；它使用当前源码构建出的 deterministic Runtime / bridge / tool handler 路径证明能力面可复现。

门禁必须覆盖：

- 默认 CLI 新 loop 的 provider tool surface 只包含只读文件系统工具，不默认开放 `write`、`apply_patch`、shell、MCP tool 或 Memory mutation。
- `write` / `apply_patch` 只有显式开启时才进入工具表，且必须要求 human gate；缺少 approval 时失败关闭。
- shell 必须显式开启、要求 human gate、限制 cwd、拒绝危险命令、脱敏环境变量、投影 transcript/audit/trace。
- MCP resource 保持只读；MCP remote tool 必须显式开启、按 descriptor 声明 side effect 和 human gate，缺少 approval 或本地降级 side effect 时失败关闭。
- Memory retrieve/form/supersede 必须通过 `ModuleCapabilityStep -> ExecutionRuntimeMemoryModuleBridge -> IMemoryModule`，mutation payload 不合法或模块未绑定时失败关闭。
- ContextPolicy 必须覆盖 token threshold、可逆压缩、supersede 优先级、受保护 segment、provider usage 缺失估算降级。
- Kernel / StageGraph 安全矩阵必须证明治理边界不可由 graph、runtime step 或模型工具请求绕过。
- 关键路径 live smoke 必须至少覆盖 deterministic reactive loop：模型工具请求可以物化为 RuntimeStep 并回流工具结果，未授权工具请求必须失败关闭且不得进入 tool-exec stage。

真实 provider 网络调用、用户凭据、跨协议模型行为和最终产品案例仍属于最终验收，不作为 v0.7 release gate 的默认 CI 前置条件。
