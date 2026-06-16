# TianShu Control Plane 设计

## 1. 文档定位

Control Plane 负责 operation 归一化、分类、治理和路由。它不编排 turn/stage，不执行工具，不生成 RuntimeStep。

## 2. 当前项目

| 项目 | 当前用途 | 新基线下的职责 |
| --- | --- | --- |
| `src/Core/TianShu.ControlPlane.Abstractions` | 控制面抽象。 | 定义 operation surface 和 Control -> Kernel bridge。 |
| `src/Core/TianShu.ControlPlane` | 控制面实现。 | 归一化 Host operation，执行 governance，输出 CoreIntent 或 query result。 |
| `src/Contracts/TianShu.Contracts.Governance` | 治理契约。 | 提供 policy、approval、permission、human gate。 |
| `src/Contracts/TianShu.Contracts.Sessions`、`Workflows`、`Conversations` | 控制对象契约。 | 作为 operation 归属和 projection 来源。 |

## 3. 接口骨架归属

归属项目：`src/Core/TianShu.ControlPlane.Abstractions/TianShu.ControlPlane.Abstractions.csproj`。

```csharp
public interface IControlPlane
{
    ValueTask<ControlOperationResult> InvokeAsync(
        ControlOperationRequest request,
        CancellationToken cancellationToken);
}

public sealed record ControlOperationResult(
    string OperationId,
    ControlOperationKind Kind,
    CoreIntent? CoreIntent,
    object? QueryResult,
    GovernanceEnvelope Governance);
```

`CoreIntent` 归属 `TianShu.Contracts.Kernel`。

## 4. 路由规则

| Operation | 处理方式 |
| --- | --- |
| query / catalog / diagnostics / projection | Control Plane 直接返回 typed result。 |
| session / thread / workflow control | Control Plane 更新受控状态或返回拒绝。 |
| turn / resume / recovery / evaluation | Control Plane 生成 `CoreIntent`，交给 Kernel。 |

## 5. 验收标准

- Control Plane 不包含 StageGraph 解释逻辑。
- Control Plane 不直接调用 provider、tool、state store 私有实现。
- 所有进入 Kernel 的请求必须带 `GovernanceEnvelope`。
