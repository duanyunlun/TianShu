# TianShu Host Gateway 设计

## 1. 文档定位

Host Gateway 是不同消费宿主进入 TianShu 的统一 typed control surface。它隔离 Experience 与内部 Control / Kernel / Runtime 实现。

## 2. 当前项目

| 项目 | 当前用途 | 新基线下的职责 |
| --- | --- | --- |
| `src/Contracts/TianShu.Contracts.Host` | Host 契约。 | 定义宿主请求、响应、view update、snapshot/reset。 |
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
- Host Gateway 不直接调用 Kernel。
- Host Gateway 不直接执行 RuntimeStep。
- Host Gateway 不直接调用 Module Plane。

## 5. 验收标准

- AppHost 只做 transport、lifecycle、composition root。
- Host Gateway 输出的是 Host projection，不是 Kernel 内部状态。
- snapshot/reset 必须走 typed contract。
