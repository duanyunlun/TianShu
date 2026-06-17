# TianShu Remote Continuity 设计

## 1. 文档定位

Remote Continuity 是 TianShu v0.8 的远程连续性能力基线。它让外部设备、Web 页面、移动端、云中继或自动化服务在受控范围内查看当前线程状态、订阅事件、提交用户意图和处理审批。

Remote Continuity 不是内核内置移动端，不是默认公网服务，也不是让远端设备直接执行本地工作负载。它的正式形态是：

```text
状态/控制接口
  + 可替换 Remote Module transport
  + Host Gateway / Control Plane 写入边界
  + Execution Runtime / projection 只读投影
```

本文以 `docs/tianshu-architecture-spec.md` 为总架构基线。任何远程入口、远程状态、远程命令、配对授权、事件流、断线恢复和安全投影实现都必须与本文对齐。

## 2. 涉及项目

| 归属 | 当前或目标项目 | 责任 |
| --- | --- | --- |
| Remote contracts | `src/Contracts/TianShu.Contracts.Remote` | 定义 thread snapshot、event cursor、event subscription、event envelope、remote command envelope、payload、scope、idempotency、audit、pairing、token、device、transport abstraction 与 module activation 合同。 |
| Host contracts | `src/Contracts/TianShu.Contracts.Host` | 承载远端命令进入 Host Gateway 时使用的 typed host operation，不暴露 Kernel / Runtime 内部对象。 |
| Projection contracts | `src/Contracts/TianShu.Contracts.Projections` | 承载 `ThreadProjection`、runtime status、pending approval、artifact、diagnostics、evidence 等只读投影。 |
| Governance contracts | `src/Contracts/TianShu.Contracts.Governance`、`src/Contracts/TianShu.Contracts.Kernel` | 定义远程命令可用 scope、副作用上限、human gate、approval decision、audit reference。 |
| Module contracts | `src/Contracts/TianShu.Contracts.Modules` | 将 Remote Module 作为可发现、可健康检查、可禁用的模块家族。 |
| Host Gateway | `src/Core/TianShu.HostGateway` | 远端写入命令的唯一产品入口；负责把 Remote Module 收到的命令转为 typed host operation。 |
| Control Plane | `src/Core/TianShu.ControlPlane.Abstractions`、`src/Core/TianShu.ControlPlane` | 对远端 operation 做归一化、治理、路由和拒绝，不编排 stage，不执行工具。 |
| RuntimeComposition | `src/Core/TianShu.RuntimeComposition` | 提供 active run、checkpoint、pending steer、thread projection、事件桥接等可注入 store / bridge。 |
| Remote Module 实现 | `src/Modules/TianShu.Remote.Local` 或等价独立包 | 当前提供本地 HTTP polling / SSE 形态的 in-process 示例；只负责连接、配对、token、事件传输和命令转发。 |
| CLI 消费入口 | `src/Presentations/TianShu.Cli` | 当前阶段只负责配置、启停、doctor、smoke 和用户提示；不直接拥有 remote state。 |
| 测试 | `tests/TianShu.Contracts.Remote.Tests`、`tests/TianShu.Remote.Local.Tests`、`tests/TianShu.HostGateway.Tests` | 覆盖 snapshot、event cursor、command admission、pairing/token、本地 remote module 示例、断线恢复、默认关闭和安全投影。 |

不存在移动端项目时，Remote Continuity 仍然必须可由本地 HTTP/SSE 或 WebSocket 测试客户端验证。移动端、Web 或云中继只是消费形态，不是 TianShu 核心架构层。

## 3. 分层归属

Remote Continuity 横跨 Experience、Host Gateway、Control Plane、Execution Runtime 和 Module Plane，但每层职责固定：

| 层级 | 职责 | 禁止事项 |
| --- | --- | --- |
| Experience Plane | 远端消费者和 CLI 管理命令。 | 不直接读写 runtime state，不构造 `CoreIntent`、`StageGraph`、`RuntimeStep`。 |
| Host Gateway Plane | 远端写入命令的唯一 typed control entry；输出可消费 projection。 | 不绕过 Control Plane，不直接调用 Remote Module 私有 transport。 |
| Control Plane | 归一化 remote command，应用 session/thread/governance/pairing scope，路由到查询、控制或核心执行。 | 不编排 stage，不执行工具，不改 workspace。 |
| Kernel / Core Loop Plane | 只在远端命令被归一化为 core intent 后参与编排。 | 不内置移动端状态机，不管理 transport 连接。 |
| Execution Runtime Plane | 从正式事件、结果、artifact、diagnostics 生成可投影状态。 | 不直接接受远端命令，不把 raw RuntimeStep / provider payload 暴露给远端。 |
| Module Plane | Remote Module 提供可替换 transport、pairing、token、event delivery 和 command ingress adapter。 | 不直接写 runtime state、workspace、memory、artifact 或 Kernel state。 |

正式数据流如下：

```text
Remote Consumer
  -> Remote Module transport
    -> Remote Continuity Bridge
      -> Host Gateway typed operation
        -> Control Plane normalized operation
          -> Kernel / Runtime / Projection
```

只读状态流如下：

```text
Execution Runtime / Projection Stores
  -> ThreadProjection / RemoteThreadSnapshot
    -> Remote Continuity Bridge
      -> Remote Module transport
        -> Remote Consumer
```

## 4. 核心原则

- 默认关闭：未显式配置和配对时，不监听远程 transport，不接受远端命令。
- 本地优先：v0.8 的基础实现以本地 loopback HTTP/SSE 或 WebSocket 为验收目标；公网、云中继和移动端 App 不作为默认能力。
- 状态只读投影：远端只能看到脱敏后的 thread snapshot、run state、stage、tool/sub-agent、pending approval、artifact、diagnostics 和 evidence 引用。
- 写入走 Host Gateway：任何 submit message、steer、interrupt、resume、approval decision、cancel pending operation 都必须进入 Host Gateway / Control Plane。
- 高风险动作继续 human gate：远端审批只提交 decision；它不能降低原 RuntimeStep / ToolUse / Module capability 的 human gate、side effect 或 allowed scope。
- 可断线恢复：事件流必须支持 cursor resume 和 snapshot refresh；重复命令必须幂等。
- 可审计：每个远端连接、token、设备、命令、审批、拒绝、断线恢复都必须有 diagnostics / audit ref。
- 最小暴露：远端 projection 不暴露 API key、secret、raw provider payload、完整本地绝对路径、未经授权的 workspace 文件内容或内部推理轨迹。

## 5. Remote contracts 骨架

以下骨架描述目标公共契约，归属 `src/Contracts/TianShu.Contracts.Remote`。P28.2 已先落地线程状态投影；P28.3 已落地事件流订阅合同；P28.4 已落地远程控制命令合同；P28.5 已落地远程命令 ingress 合同与 Host Gateway bridge；activation、pairing 和 token 由 P28.6 继续补齐。

```csharp
namespace TianShu.Contracts.Remote;

public interface IRemoteContinuityModule
{
    ValueTask<RemoteModuleActivationResult> ActivateAsync(
        RemoteModuleActivationContext context,
        IRemoteContinuityBridge bridge,
        CancellationToken cancellationToken);

    ValueTask DeactivateAsync(
        RemoteModuleDeactivationRequest request,
        CancellationToken cancellationToken);
}
```

Remote Module 只拿到 `IRemoteContinuityBridge`，不得拿到 HostGateway、ControlPlane、RuntimeComposition、ExecutionRuntime、Kernel 或 store 的具体实现。

```csharp
public interface IRemoteContinuityBridge
{
    ValueTask<RemoteThreadSnapshot> GetSnapshotAsync(
        RemoteThreadSnapshotQuery query,
        CancellationToken cancellationToken);

    IAsyncEnumerable<RemoteContinuityEvent> SubscribeAsync(
        RemoteEventSubscriptionRequest request,
        CancellationToken cancellationToken);
}
```

远程写入命令使用独立 ingress 合同：

```csharp
public interface IRemoteCommandIngress
{
    ValueTask<RemoteCommandResult> SubmitCommandAsync<TPayload>(
        RemoteCommandEnvelope<TPayload> command,
        CancellationToken cancellationToken)
        where TPayload : IRemoteCommandPayload;
}
```

P28.5 的正式实现归属 `src/Core/TianShu.HostGateway/RemoteCommandHostGatewayBridge.cs`。该 bridge 只能把 `RemoteCommandEnvelope<TPayload>` 转成 `HostOperationRequest` 并调用 `IHostGateway.InvokeAsync`，随后进入 Control Plane；Remote Module 实现不得自己解释 submit / approval / interrupt / resume 的业务语义，也不得拿到 RuntimeComposition、ExecutionRuntime、Kernel、workspace resolver 或 store 的具体实现。

## 6. 线程状态投影

`RemoteThreadSnapshot` 是远端可消费状态，不等于 Kernel state、RuntimeStep 或 StageGraph。它只能由正式 projection 组装。

```csharp
public sealed record RemoteThreadSnapshot(
    string SnapshotId,
    string ThreadId,
    RemoteRunState RunState,
    RemoteStageState? CurrentStage,
    IReadOnlyList<RemoteToolState> ToolStates,
    IReadOnlyList<RemoteSubAgentState> SubAgentStates,
    IReadOnlyList<RemotePendingApproval> PendingApprovals,
    IReadOnlyList<RemoteArtifactRef> Artifacts,
    RemoteDiagnosticsSummary Diagnostics,
    RemoteEvidenceSummary Evidence,
    RemoteSnapshotRedaction Redaction);
```

P28.2 的正式合同归属 `src/Contracts/TianShu.Contracts.Remote/RemoteThreadSnapshotModels.cs`，已冻结以下远端可见类型：

- `RemoteThreadSnapshot`
- `RemoteRunState`
- `RemoteStageState`
- `RemoteToolState`
- `RemoteSubAgentState`
- `RemotePendingApproval`
- `RemoteArtifactRef`
- `RemoteDiagnosticsSummary`
- `RemoteEvidenceSummary`
- `RemoteSnapshotRedaction`

这些类型只能表达远端可见摘要和安全引用；不得新增 raw provider payload、完整本地绝对路径、RuntimeStep、StageGraph 或 secret 字段。`RemotePendingApproval` 必须声明明确 side effect 且不得关闭 human gate。

## 7. 事件流

事件流是 snapshot 的增量补充。每个事件必须可排序、可恢复、可去重：

```csharp
public sealed record RemoteContinuityEvent(
    string EventId,
    string ThreadId,
    string Cursor,
    RemoteContinuityEventKind Kind,
    DateTimeOffset OccurredAt,
    object Payload,
    RemoteEventVisibility Visibility);
```

事件流 transport 可以是 SSE、WebSocket、stdio bridge、local named pipe 或云中继 adapter，但 Remote Module transport 不改变事件语义。P28.3 的正式合同归属 `src/Contracts/TianShu.Contracts.Remote/RemoteEventStreamContracts.cs`，已冻结以下类型：

- `RemoteEventCursor`
- `RemoteEventSubscriptionRequest`
- `RemoteEventReplayPlan`
- `RemoteContinuityEvent`
- `RemoteEventVisibility`
- `IRemoteContinuityEventSubscriber`

事件流合同必须满足：

- `RemoteEventCursor` 是断线重连和去重的唯一位置标识。
- `RemoteEventSubscriptionRequest.TransportKind` 必须显式声明 `ServerSentEvents`、`WebSocket`、`LocalHttpPolling`、`NamedPipe`、`StdioBridge` 或 `CloudRelayAdapter`，不能使用 `Unspecified`。
- `RemoteEventReplayMode.FromCursor` 必须携带 cursor；cursor 过期或未保留时必须要求 snapshot refresh。
- `RemoteContinuityEvent` 必须携带 event id、thread id、cursor、明确 event kind、发生时间、可选 payload 和 visibility。
- `RemoteEventVisibility` 只能表达 scope、redaction kind 和 policy ref，不得携带 secret、raw provider payload 或本地绝对路径。
- `IRemoteContinuityEventSubscriber` 只输出远程事件 envelope，不拥有 Host / Kernel / Runtime 状态，也不解释远端命令。

## 8. 远程控制命令

远端命令是受治理用户/系统操作，不是 RuntimeStep。基础命令集合如下：

| 命令 | 语义 | 目标入口 |
| --- | --- | --- |
| `SubmitMessage` | 向线程提交新的用户消息或 follow-up。 | Host Gateway -> Control Plane -> Core intent。 |
| `Steer` | 给当前或下一轮 run 提供 steer 输入。 | Host Gateway -> Control Plane -> RuntimeComposition HostControl bridge。 |
| `Interrupt` | 请求中断 active run。 | Host Gateway -> Control Plane -> active-run cancellation。 |
| `Resume` | 从 checkpoint 或 pending input 继续。 | Host Gateway -> Control Plane -> checkpoint / pending queue。 |
| `ApprovalDecision` | 对 pending approval 提交 approve / deny / defer。 | Host Gateway -> Control Plane -> governance decision。 |
| `CancelPendingOperation` | 取消未执行的 pending operation。 | Host Gateway -> Control Plane -> controlled pending store。 |

命令 envelope 至少包含：

```csharp
public sealed record RemoteCommandEnvelope<TPayload>(
    string CommandId,
    string ThreadId,
    string DeviceId,
    string SessionId,
    TPayload Payload,
    RemoteCommandScope Scope,
    RemoteCommandIdempotencyKey IdempotencyKey,
    RemoteAuditContext Audit);
```

P28.4 的正式合同归属 `src/Contracts/TianShu.Contracts.Remote/RemoteCommandContracts.cs`，已冻结以下类型：

- `RemoteCommandKind`
- `RemoteCommandIdempotencyKey`
- `RemoteCommandScope`
- `RemoteAuditContext`
- `IRemoteCommandPayload`
- `RemoteCommandEnvelope<TPayload>`
- `RemoteSubmitMessagePayload`
- `RemoteSteerPayload`
- `RemoteInterruptPayload`
- `RemoteResumePayload`
- `RemoteApprovalDecisionPayload`
- `RemoteCancelPendingOperationPayload`
- `RemoteCommandResult`
- `IRemoteCommandIngress`

远程命令合同必须满足：

- 命令不是 `RuntimeStep`，只是 Host Gateway / Control Plane 之前的受治理输入。
- `RemoteCommandScope` 必须声明允许命令集合和明确 side effect 上限，不能使用 `Unspecified`。
- `RemoteCommandEnvelope<TPayload>` 必须携带 command id、thread id、device id、session id、payload、scope、idempotency key 和 audit context。
- Envelope 的 payload kind 必须在 scope allow-list 内；否则在合同层拒绝构造。
- `RemoteApprovalDecisionPayload` 不能使用 `Unspecified` decision。
- 所有断线重试必须使用 `RemoteCommandIdempotencyKey`；P28.5 以后由 Host Gateway / Control Plane admission 返回 `RemoteCommandResult`。

P28.5 已把命令 payload、Host Gateway 映射和拒绝语义接入正式入口。当前 `remote.submit_message`、`remote.interrupt`、`remote.resume` 会被 Control Plane 归一化为 CoreIntent；`remote.approval_decision` 进入 Governance；`remote.steer` 与 `remote.cancel_pending_operation` 当前只完成 Control 分类，在受控 handler 未接入前必须 fail closed。该状态是入口边界完成，不代表 Remote Module 已具备完整远程操控产品能力。

## 9. Pairing、token 与 device scope

Remote Module 必须通过显式配对建立授权，不得把安装 TianShu 等同于远端已授权。

```csharp
public sealed record RemoteTransportDescriptor(
    RemoteModuleTransportKind Kind,
    string EndpointRef,
    RemoteTransportSecurityMode SecurityMode,
    string? BindAddress,
    bool AllowsPublicNetwork,
    bool RequiresPairing);

public sealed record RemotePairingGrant(
    string PairingId,
    DeviceId DeviceId,
    string DeviceDisplayName,
    RemoteDeviceTrustLevel TrustLevel,
    RemoteCommandScope Scope,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string RevocationRef,
    IReadOnlyList<RemoteModuleTransportKind> AllowedTransports);

public sealed record RemoteSessionTokenDescriptor(
    string TokenRef,
    string PairingId,
    DeviceId DeviceId,
    IReadOnlyList<RemoteTokenAudience> Audiences,
    RemoteCommandScope Scope,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string RevocationRef);

public interface IRemoteContinuityModule
{
    ValueTask<RemoteModuleActivationResult> ActivateAsync(
        RemoteModuleActivationContext context,
        IRemoteContinuityBridge bridge,
        CancellationToken cancellationToken);

    ValueTask DeactivateAsync(
        RemoteModuleDeactivationRequest request,
        CancellationToken cancellationToken);
}
```

P28.6 的正式合同归属 `src/Contracts/TianShu.Contracts.Remote/RemoteModuleContracts.cs`，已冻结以下类型：

- `RemoteModuleTransportKind`
- `RemoteTransportSecurityMode`
- `RemoteDeviceTrustLevel`
- `RemotePairingStatus`
- `RemoteTokenAudience`
- `RemoteSessionRevocationReason`
- `RemoteTransportDescriptor`
- `RemotePairingGrant`
- `RemoteSessionTokenDescriptor`
- `RemoteSessionRevocation`
- `RemoteModuleActivationContext`
- `RemoteModuleActivationResult`
- `RemoteModuleDeactivationRequest`
- `IRemoteContinuityBridge`
- `IRemoteContinuityModule`
- `RemoteThreadSnapshotQuery`

配对和 token 规则：

- token 必须短期有效，当前合同上限为 24 小时；合同只暴露 `TokenRef`，不得携带 raw token、secret 或 token value。
- device identity 必须与 pairing grant 绑定。
- scope 必须区分只读订阅、提交消息、审批、interrupt/resume、workspace-sensitive projection。
- token 不得写入日志、diagnostics、snapshot 或事件 payload。
- 默认不允许公网监听；即使启用远程 transport，也必须显式声明 bind address。
- 云中继只能作为 Remote Module transport adapter；它不拥有 TianShu 内核控制权。
- `RemoteModuleActivationContext` 必须校验 device id、pairing id、token、transport allow-list 和 token scope；token scope 不得超过 pairing scope。

P28.6 已把 transport abstraction、pairing、short-lived token、session revocation、device identity 和 scope 固定为正式合同。它仍不实现具体 Remote Module server，不开放监听，也不代表远端操控产品能力已完成。

## 10. 本地 Remote Module 示例

P28.7 的正式示例实现归属 `src/Modules/TianShu.Remote.Local`，测试归属 `tests/TianShu.Remote.Local.Tests`。该示例提供：

- `LocalRemoteContinuityModule.SupportedTransports`：声明 `LocalHttpPolling` 与 `ServerSentEvents` 两种 loopback 形态，默认 `AllowsPublicNetwork=false`，且不主动打开监听。
- `ActivateAsync` / `DeactivateAsync`：只接受 P28.6 定义的 `RemoteModuleActivationContext` 与 `IRemoteContinuityBridge`，不持有 HostGateway、ControlPlane、RuntimeComposition、ExecutionRuntime、Kernel 或 store 实现。
- `GetSnapshotAsync`：把只读状态查看转发给 `IRemoteContinuityBridge.GetSnapshotAsync`，并在返回远端消费者前经过 `RemoteProjectionSecurityProjector.ProjectSnapshot`。
- `RefreshSnapshotAsync`：在 cursor 过期、事件无法补发或客户端主动刷新时重新读取并投影线程快照。
- `SubscribeServerSentEventsAsync`：把 SSE 形态事件订阅转发给 `IRemoteContinuityBridge.SubscribeAsync`，并在输出事件前经过 `RemoteProjectionSecurityProjector.ProjectEvent`。
- `BuildReconnectReplayPlan`：根据 `lastCursor` 与 retention state 生成 `FromCursor` 或 `SnapshotThenEvents` replay 计划。
- `SubmitMessageAsync`、`SubmitApprovalDecisionAsync`、`InterruptAsync`、`ResumeAsync`：统一生成 `RemoteCommandEnvelope<TPayload>` 并转发给 `IRemoteContinuityBridge.SubmitCommandAsync`；转发前必须通过 `RemoteCommandScope.AllowsSideEffectFor` 校验命令 kind 和最低副作用等级。
- 命令幂等：同一 pairing、device、thread、command kind 与 idempotency key 重复提交时，不再次调用 bridge，而是返回 `DuplicateIgnored` 并保留原 accepted operation ref。
- 过期审批：提交 `ApprovalDecision` 前若可读取 snapshot，必须检查 pending approval 是否已经过期或不再 pending；过期返回 `Expired`，非 pending 返回 `Invalid`，不得转发。

该示例用于证明 Remote Module 的最小产品形态可落地，但它仍是 in-process 示例，不是生产 HTTP server、WebSocket server、移动端或云中继。真实 transport 实现必须复用同一合同，并继续满足默认关闭、显式 pairing、短期 token、scope 不扩权和所有写入进入 Host Gateway / Control Plane 的边界。

## 11. 安全投影

远端投影必须先经过 redaction / visibility policy。最低要求：

| 数据 | 远端默认 |
| --- | --- |
| API key、secret、token、header | 永不暴露。 |
| 本地绝对路径 | 默认脱敏为 workspace-relative 或 redacted ref。 |
| workspace 文件内容 | 默认不随 snapshot 暴露；必须通过单独受治理只读能力获取。 |
| raw provider request / response | 默认不暴露；只暴露 result summary、failure code、usage、diagnostics ref。 |
| RuntimeStep / StageGraph raw object | 不暴露；只暴露 stage/status/tool/sub-agent/projection summary。 |
| 内部推理轨迹 | 不保存、不远端暴露。 |
| pending approval | 暴露最小 action 摘要、side effect、risk、diff/artifact ref 和 decision options。 |

P28.8 的正式安全投影实现归属 `src/Contracts/TianShu.Contracts.Remote/RemoteProjectionSecurity.cs`，本地示例接入归属 `src/Modules/TianShu.Remote.Local/LocalRemoteContinuityModule.cs`，测试归属 `tests/TianShu.Contracts.Remote.Tests/RemoteProjectionSecurityTests.cs` 与 `tests/TianShu.Remote.Local.Tests/LocalRemoteContinuityModuleTests.cs`。

已冻结的实现规则：

- `RemoteProjectionSecurityPolicy` 默认不允许 workspace 文件内容随 snapshot/event 暴露；只允许以安全引用、摘要或 redacted marker 表达。
- `RemoteProjectionSecurityProjector.ProjectSnapshot` 必须递归净化 snapshot 中的本地绝对路径、secret、token、疑似 raw credential，并把实际发生的类别写入 `RemoteSnapshotRedaction`。
- `RemoteProjectionSecurityProjector.ProjectEvent` 必须递归净化 `StructuredValue` payload，并合并 `RemoteEventVisibility.Redacted`、`RedactedKinds` 与 policy ref。
- `RemoteToolState` 若副作用等级高于 `ReadOnly`，出站投影必须保持 `RequiresHumanGate=true`；远端投影不得把高风险 tool/module 状态降级为无审批。
- `RemoteCommandScope.AllowsSideEffectFor` 是远端命令准入的最低合同校验：`SubmitMessage` 当前最低为 `ReadOnly`，`Steer`、`Interrupt`、`Resume`、`ApprovalDecision`、`CancelPendingOperation` 最低为 `HostMutation`。Remote Module 在转发命令前必须同时校验 command allow-list 和 side-effect ceiling。

## 12. 断线恢复与幂等

Remote Continuity 必须以 snapshot + event cursor 组合处理断线：

```text
client reconnect
  -> submit last cursor
  -> server replays retained events when possible
  -> if cursor expired, return snapshot_required
  -> client refreshes RemoteThreadSnapshot
```

远端写入命令必须带 idempotency key。重复命令返回同一逻辑结果或明确 `duplicate_ignored`，不得重复执行高风险操作。过期 approval、已取消 run、已消费 checkpoint 和 thread mismatch 必须 fail closed。

P28.9 的本地示例实现归属 `src/Modules/TianShu.Remote.Local/LocalRemoteContinuityModule.cs`，测试归属 `tests/TianShu.Remote.Local.Tests/LocalRemoteContinuityModuleTests.cs`。当前已落地：

- `BuildReconnectReplayPlan`：cursor 仍可补发时返回 `FromCursor`；cursor 过期、未保留或无 cursor 时返回 `SnapshotThenEvents`，并要求 snapshot refresh。
- `RefreshSnapshotAsync`：复用 snapshot 查询与安全投影，作为 cursor 过期后的刷新入口。
- 命令幂等缓存：以 pairing、device、thread、command kind、idempotency key 组合去重；重复命令返回 `DuplicateIgnored`，不重复调用 `IRemoteContinuityBridge.SubmitCommandAsync`。
- 过期审批处理：`ApprovalDecision` 转发前读取 pending approval 快照，发现 `Expired` 或 `ExpiresAt <= now` 时返回 `RemoteCommandAdmissionStatus.Expired`；发现非 pending 状态时返回 `Invalid`。

生产级 Remote Module 可以把 event retention、idempotency 和 pending approval 状态下沉到持久化 store 或云中继，但不得弱化上述语义。

## 13. 验收案例

P28.11 的正式机制验收项目归属 `tools/acceptance/TianShu.RemoteContinuityAcceptance`，并由 `tools/Run-TianShuFinalAcceptance.ps1` 在最终验收主流程前执行。该验收项目不依赖真实移动端、公网监听或云中继，而是用 `src/Modules/TianShu.Remote.Local` 模拟移动端/远端消费者，输出 `remote-continuity/evidence.json`。

验收必须证明：

- 模拟移动端只读跟随：可读取 `RemoteThreadSnapshot` 并订阅 `RemoteContinuityEvent`。
- 安全投影：snapshot/event 出站必须记录 redaction，至少证明本地绝对路径不会原样暴露。
- 断线恢复：cursor 可用时生成 `FromCursor` replay；cursor 过期时要求 snapshot refresh。
- 远程审批：`ApprovalDecision` 通过 `RemoteCommandEnvelope<TPayload>` 转发并返回 accepted。
- 远程中断/恢复：`Interrupt` 与 `Resume` 均通过同一 bridge 转发。
- 远程 follow-up：`SubmitMessage` 可作为远程后续用户消息进入 Host Gateway / Control Plane 边界。
- 重复命令幂等：同一 idempotency key 的重复 follow-up 返回 `DuplicateIgnored`，不得重复转发。

该机制验收只证明 Remote Continuity 合同、Local Remote Module 示例和最终验收脚本的确定性链路可用；真实移动端 UI、Web UI、云中继或生产 HTTP/WebSocket server 不属于 P28.11 的通过条件。

## 14. 多宿主消费收敛

P28.12 的正式收敛点归属 `src/Core/TianShu.HostGateway`。多宿主体不得分别持有 Remote Module、RuntimeComposition、ExecutionRuntime、Kernel 或投影 store 的私有实现，而是统一通过 Host Gateway typed surface 消费远程连续性只读状态。

正式接入形态如下：

- `RemoteContinuityHostGatewayBridge` 实现 `IRemoteContinuityBridge`，内部只依赖 `IHostGateway` 和远程命令 ingress bridge。
- `GetSnapshotAsync` 只能调用 `IHostGateway.SnapshotAsync` 读取 `ProjectionScopeKind.Thread`，并把正式 `ThreadProjection` 映射成 `RemoteThreadSnapshot`。
- `SubscribeAsync` 只能调用 `IHostGateway.SubscribeAsync` 订阅 `ProjectionScopeKind.Thread`，并把 `ThreadProjectionPayload` delta 映射成 `RemoteContinuityEvent`；投影 reset 映射为 `SnapshotRequired`。
- `SubmitCommandAsync` 继续复用 `RemoteCommandHostGatewayBridge`，所有远端写入仍进入 `IHostGateway.InvokeAsync -> ControlPlane.ProcessAsync`。
- 出站 snapshot/event 必须再经过 `RemoteProjectionSecurityProjector`，不得因宿主体不同而放宽脱敏或 human gate。

各宿主职责固定为：

| 宿主 | 允许能力 | 禁止事项 |
| --- | --- | --- |
| CLI | 管理配置、启动本地 remote module、执行验收/doctor。 | 不拥有 remote state，不绕过 HostGateway。 |
| AppHost | 作为进程入口和组合宿主提供 HostGateway/ControlPlane 装配。 | 不把 Remote Module 变成默认公网监听，不暴露 Runtime 内部对象。 |
| VSIX | 通过 Sidecar typed protocol 读取状态、发送用户意图。 | 不引用 HostGateway、ControlPlane、RuntimeComposition、ExecutionRuntime 或 Remote Module 实现。 |
| Config GUI | 只编辑/预览 remote/module 配置和只读状态说明。 | 不启动 turn runtime，不生成 Kernel/Runtime 输入，不直接连接 Remote Module transport。 |

因此，P28.12 验收只要求证明多宿主使用同一 HostGateway remote continuity bridge 可得到一致、安全的 thread projection 和 command ingress 语义；不要求实现移动端 UI、Web UI、公网 server、云中继或 VSIX 可视化远程控制面。

## 15. 默认发布门禁

P28.14 的正式发布门禁归属 `tools/Test-TianShuV08RemoteContinuityReleaseGate.ps1`，英文标识为 `v0.8 remote continuity release gate`。该脚本必须进入 `.github/workflows/ci-release.yml`，并在打包前阻断不满足远程连续性边界的提交。

v0.8 发布前必须由该脚本证明：

- Remote Module 可选关闭，默认不开放公网。
- 未配对或 token 过期时，snapshot、event stream 和 command 全部拒绝。
- 只读订阅不能提交命令。
- 远端审批不能降低原有 human gate。
- 远端命令全部进入 Host Gateway / Control Plane，不直接写 runtime state 或 workspace。
- 断线重连、cursor 过期、重复命令、过期审批都有结构化结果。
- projection 不泄露 secret、raw provider payload、完整本地绝对路径或未授权文件内容。

该 gate 固定运行三类证据：

1. 合同和安全测试：`tests/TianShu.Contracts.Remote.Tests` 必须覆盖 transport 默认非公网、pairing/token/scope、命令 envelope、audit、idempotency 和 projection redaction。
2. 本地 Remote Module 与 Host Gateway 桥测试：`tests/TianShu.Remote.Local.Tests` 与 `tests/TianShu.HostGateway.Tests` 必须证明本地示例只在显式 activation 后工作，只读 token 不能提交命令，所有写入命令通过 Host Gateway / Control Plane，HostGateway bridge 不引用 Runtime、workspace store 或 Remote Module 实现。
3. 确定性机制验收：`tools/acceptance/TianShu.RemoteContinuityAcceptance` 必须输出 `deterministic-remote-continuity` evidence，包含只读跟随事件、`FromCursor` replay、snapshot refresh、远程审批 accepted、interrupt/resume accepted、follow-up accepted、重复 follow-up `DuplicateIgnored`，并记录 `absolute_path` redaction。

该 gate 不要求实现真实移动端 UI、Web UI、公网 server 或云中继。它只验证 v0.8 架构边界和本地可替换 Remote Module 示例已经足以支撑受治理远程连续性能力。
