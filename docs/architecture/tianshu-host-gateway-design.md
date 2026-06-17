# TianShu Host Gateway 设计

## 1. 文档定位

Host Gateway 是不同消费宿主进入 TianShu 的统一 typed control surface。它隔离 Experience 与内部 Control / Kernel / Runtime 实现。

## 2. 当前项目

| 项目 | 当前用途 | 新基线下的职责 |
| --- | --- | --- |
| `src/Contracts/TianShu.Contracts.Host` | Host 契约。 | 定义宿主请求、响应、view update、snapshot/reset。 |
| `src/Contracts/TianShu.Contracts.Remote` | Remote 连续性契约。 | 定义远程命令 envelope、scope、idempotency、audit 和 ingress。 |
| `src/Core/TianShu.HostGateway` | Host Gateway 实现。 | 统一处理 host operation 和 projection。 |
| `src/Hosting/TianShu.AppHost` | 进程宿主、transport、composition。 | 装配 Host Gateway、Control、Kernel、Execution、Module，不拥有编排语义。 |
| `src/Hosting/TianShu.AppHost.Configuration` | AppHost 配置读取。 | 只提供配置加载和投影，不成为 Control 或 Kernel。 |

## 3. 接口骨架归属

归属项目：`src/Core/TianShu.HostGateway/TianShu.HostGateway.csproj`。

```csharp
public interface IHostGateway
{
    ValueTask<HostOperationResult> InvokeAsync(
        HostOperationRequest request,
        CancellationToken cancellationToken);

    IAsyncEnumerable<HostViewUpdate> SubscribeAsync(
        HostSubscriptionRequest request,
        CancellationToken cancellationToken);

    ValueTask<HostSnapshot> SnapshotAsync(
        HostSnapshotRequest request,
        CancellationToken cancellationToken);
}
```

## 4. 边界

- Host Gateway 可以调用 Control Plane。
- Host Gateway 可以读取 projection。
- Host Gateway 可以实现 Remote command bridge，但 bridge 只能调用 `IHostGateway.InvokeAsync`。
- Host Gateway 不直接调用 Kernel。
- Host Gateway 不直接执行 RuntimeStep。
- Host Gateway 不直接调用 Module Plane。
- Host Gateway 不直接写 runtime state、workspace 或任何 store。

远程写入命令的正式入口为 `IRemoteCommandIngress.SubmitCommandAsync`，当前实现是 `RemoteCommandHostGatewayBridge`。它把 `RemoteCommandEnvelope<TPayload>` 映射为 `HostOperationRequest`，统一经 `TianShuHostGateway.InvokeAsync -> ControlPlane.ProcessAsync` 进入控制平面。`remote.steer` 和 `remote.cancel_pending_operation` 在受控 handler 未接入前必须保持 fail closed。

## 5. 验收标准

- AppHost 只做 transport、lifecycle、composition root。
- Host Gateway 输出的是 Host projection，不是 Kernel 内部状态。
- snapshot/reset 必须走 typed contract。
- Remote Module 写入不得绕过 Host Gateway / Control Plane。
