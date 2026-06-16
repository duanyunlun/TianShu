# TianShu CLI 交互设计

## 1. 文档定位

CLI 属于 Experience Plane。它只负责命令解析、交互状态、终端展示和 Host Gateway 调用，不拥有 Control、Kernel、Execution 或 Module 的内部语义。

## 2. 当前项目

| 项目 | 当前用途 | 新基线下的职责 |
| --- | --- | --- |
| `src/Presentations/TianShu.Cli` | CLI 命令、chat、人类终端、脚本输出。 | 只通过 Host Gateway 消费 typed surface。 |
| `src/Contracts/TianShu.Contracts.Host` | 宿主请求与视图契约。 | CLI 构造 host operation 和消费 host projection。 |
| `src/Core/TianShu.HostGateway` | 宿主网关。 | CLI 的唯一内部入口。 |

## 3. 接口骨架归属

归属项目：`src/Presentations/TianShu.Cli/TianShu.Cli.csproj`。

```csharp
public interface ICliHostClient
{
    ValueTask<HostOperationResult> InvokeAsync(
        HostOperationRequest request,
        CancellationToken cancellationToken);

    IAsyncEnumerable<HostViewUpdate> SubscribeAsync(
        HostSubscriptionRequest request,
        CancellationToken cancellationToken);
}
```

CLI 可以维护 presentation state，但不能复制 Kernel state。

## 4. 交互边界

- `/model-route`、`/config`、`/threads` 等控制命令是 host operation，不写入对话内容。
- chat 输入必须进入 Host Gateway，由 Control Plane 分类。
- follow-up、interrupt、resume 只发送 typed host operation。
- 默认输出只展示 assistant、tool、plan、error、status 的用户可见投影。

## 5. 验收标准

- CLI 不直接调用 provider、tool、Execution Runtime。
- CLI 不构造 `CoreIntent`、`StageGraph` 或 `RuntimeStep`。
- CLI transcript 只记录用户可见内容和可审计事件引用。
