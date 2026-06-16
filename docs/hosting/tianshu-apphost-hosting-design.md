# TianShu AppHost / Hosting 设计

## 1. 文档定位

Hosting 负责进程宿主、transport、lifecycle 和 composition root。它不是 Kernel、Control Plane 或 Execution Runtime 的语义归属地。

## 2. 当前项目

| 项目 | 当前用途 | 新基线下的职责 |
| --- | --- | --- |
| `src/Hosting/TianShu.AppHost` | AppHost 进程、RPC、WebSocket、stdio、运行时装配。 | 只保留宿主入口、transport、lifecycle、composition root。 |
| `src/Hosting/TianShu.AppHost.Configuration` | AppHost 配置加载。 | 提供宿主配置读取和 projection。 |
| `src/Hosting/TianShu.AppHost.State` | AppHost 状态存储。 | 作为 Host / projection / runtime state 实现输入。 |
| `src/Hosting/TianShu.AppHost.Tools` | 宿主工具 surface。 | 作为 Tool Module 和 Execution Runtime bridge 输入。 |
| `src/Hosting/TianShu.AppHost.Tools.Runtime` | 现有工具运行时实现。 | 迁移为 Execution Runtime / Module bridge，不拥有 Kernel ToolUse 语义。 |
| `src/Core/TianShu.RuntimeComposition` | 运行时装配。 | 装配 Control、Kernel、Execution、Module，不拥有编排语义。 |

## 3. 接口骨架归属

归属项目：`src/Hosting/TianShu.AppHost/TianShu.AppHost.csproj`。

```csharp
public interface IAppHostRuntime
{
    ValueTask StartAsync(
        AppHostStartupOptions options,
        CancellationToken cancellationToken);

    ValueTask StopAsync(
        CancellationToken cancellationToken);
}
```

运行时装配接口归属 `src/Core/TianShu.RuntimeComposition/TianShu.RuntimeComposition.csproj`。

```csharp
public interface ITianShuRuntimeComposition
{
    IHostGateway HostGateway { get; }
    IControlPlane ControlPlane { get; }
    IStableKernelCore Kernel { get; }
    IExecutionRuntime ExecutionRuntime { get; }
}
```

## 4. 迁移规则

- AppHost 内现有 Kernel-like 文件只能作为迁移输入。
- 新 Kernel 语义必须进入 `TianShu.Kernel.*` 目标项目。
- AppHost 可以装配 Provider / Tool / State / Diagnostics Module，但不能直接调用它们绕过 Execution Runtime。

## 5. 验收标准

- AppHost 不构造 StageGraph。
- AppHost 不生成 RuntimeStep。
- AppHost 不晋升 strategy。
- AppHost 输出只能是 Host Gateway projection 或 transport message。
