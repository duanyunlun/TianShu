# TianShu Experience Plane 设计

## 1. 文档定位

Experience Plane 是用户接触 TianShu 的入口层。它只负责交互体验、命令解析和展示投影，不拥有 Control、Kernel、Execution 或 Module 的内部语义。

## 2. 当前项目

| 项目 | 当前用途 | 新基线下的职责 |
| --- | --- | --- |
| `src/Presentations/TianShu.Cli` | CLI、chat、命令行交互。 | 通过 Host Gateway / RuntimeComposition 产品桥消费 typed host surface。 |
| `src/Presentations/TianShu.ConfigGui` | 配置 GUI。 | 作为配置编辑宿主，只消费 Configuration projection / preview / apply contract，不进入 turn runtime。 |
| `src/Presentations/TianShu.VSSDK.Sidecar` | VS 侧边宿主进程。 | 作为 VSIX 的进程边界与 composition bridge，装配 runtime 后只向 VSIX 暴露 typed sidecar protocol、Host Gateway operation 和 Control Plane projection。 |
| `src/Presentations/TianShu.VSSDK.VSExtension` | VSIX UI。 | 只通过 sidecar typed protocol 发送宿主操作、响应审批/输入并消费投影。 |

## 3. 接口骨架归属

归属项目：`src/Contracts/TianShu.Contracts.Host/TianShu.Contracts.Host.csproj`。

```csharp
public sealed record HostOperationRequest(
    string OperationId,
    string HostId,
    string OperationKind,
    object Payload,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record HostOperationResult(
    string OperationId,
    string Status,
    object? Projection,
    IReadOnlyList<HostDiagnosticRef> Diagnostics);
```

Experience 项目只能构造 `HostOperationRequest` 或等价的宿主协议 DTO，不能构造 `CoreIntent`、`StageGraph`、`RuntimeStep` 或 Module 私有输入。

ConfigGUI 是配置编辑例外：它不触发 turn，不要求通过 Host Gateway 创建对话操作；它只能调用正式 Configuration loader / schema projection / preview / apply 契约，不能生成 Kernel / Runtime 输入，也不能读取 Runtime 私有对象。

VSSDK.Sidecar 是进程内 composition bridge：它可以持有 `IExecutionRuntime` 生命周期、创建 Control Plane 与 Host Gateway wrapper，并转译 VSIX typed protocol；这不是允许它访问 Kernel / Runtime 内部语义。Sidecar 源码不得引用 `CoreIntent`、`StageGraph`、`RuntimeStep`、`StableKernelCore`、`AdaptiveRuntimeExecutionLoop` 或 `KernelRuntimeProductTerminalProjection`。

## 4. 验收标准

- CLI、VSIX、Sidecar、ConfigGUI 不直接构造 Kernel / Runtime 内部对象。
- ConfigGUI 只编辑配置 projection / preview / apply contract；provider 连接探测若保留，只能作为配置诊断，不能成为模型路由或 runtime 执行决策。
- Sidecar 的 runtime 引用只能停留在 composition / lifecycle boundary；VSIX 不得引用 HostGateway、ControlPlane、RuntimeComposition 或 Execution.Runtime 项目。
- Experience 输出必须来自 Host Gateway projection。
- 用户操作必须先进入 Host Gateway，再由 Control Plane 分类。
- 默认用户可见输出不得暴露 Kernel 内部可变对象、secret 或 raw provider payload。
