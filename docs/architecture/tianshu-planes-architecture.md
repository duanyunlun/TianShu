# TianShu 六层架构模块落地规范

## 1. 文档定位

本文细化 `docs/tianshu-architecture-spec.md` 的六层主链，作为各模块设计文档的索引和验收入口。本文只记录当前有效分层，不保留历史方案。

正式主链固定为：

```text
Experience Plane
  -> Host Gateway Plane
    -> Control Plane
      -> Kernel / Core Loop Plane
        -> Execution Runtime Plane
          -> Module Plane
```

## 2. 当前项目现状

| 层级 | 当前涉及项目 | 当前状态 |
| --- | --- | --- |
| Experience Plane | `TianShu.Cli`、`TianShu.ConfigGui`、`TianShu.VSSDK.Sidecar`、`TianShu.VSSDK.VSExtension` | 入口项目已存在，仍需要按 Host Gateway typed surface 收敛。 |
| Host Gateway Plane | `TianShu.Contracts.Host`、`TianShu.HostGateway`、`TianShu.AppHost` | typed host surface 已存在，AppHost 仍承担较多运行时装配职责。 |
| Control Plane | `TianShu.ControlPlane.Abstractions`、`TianShu.ControlPlane`、控制相关 contracts | 控制面项目已存在，职责必须收敛为 operation 归一化、治理和路由。 |
| Kernel / Core Loop Plane | 当前散落在 `TianShu.RuntimeComposition`、`TianShu.AppHost`、`TianShu.AppHost.Tools.Runtime`、`TianShu.Execution.Runtime` | 目标项目尚未建立，必须按 Kernel 专项文档迁出。 |
| Execution Runtime Plane | `TianShu.Execution.Protocol`、`TianShu.Execution.Runtime` | 运行时项目已存在，目标是只执行 Kernel 批准的 `ExecutionPlan` / `RuntimeStep`。 |
| Module Plane | `TianShu.Provider.*`、`TianShu.Tools.*`、`TianShu.IdentityMemory`、`TianShu.ArtifactStore`、`TianShu.Diagnostics`、`TianShu.ProjectionStores`、`TianShu.Configuration` | 具体能力项目已存在，需统一 Module / Tool 描述、权限、副作用和审计契约。 |

## 3. 目标项目归属

| 层级 | 目标项目 |
| --- | --- |
| Experience | 继续使用 `src/Presentations/*`。 |
| Host Gateway | 继续使用 `src/Core/TianShu.HostGateway`，宿主进程使用 `src/Hosting/TianShu.AppHost`。 |
| Control Plane | 继续使用 `src/Core/TianShu.ControlPlane.Abstractions` 与 `src/Core/TianShu.ControlPlane`。 |
| Kernel | 新建 `TianShu.Contracts.Kernel`、`TianShu.Kernel.Abstractions`、`TianShu.Kernel`、`TianShu.Kernel.Adaptive`、`TianShu.Kernel.Strategies`。 |
| Execution Runtime | 继续使用 `src/Execution/TianShu.Execution.Runtime` 与 `src/Execution/TianShu.Execution.Protocol`。 |
| Module Plane | 能力契约进入对应 `TianShu.Contracts.*`；默认实现进入 `Provider`、`Tools`、`Core` 下的模块项目。 |

## 4. 跨层不变量

- Experience 只能通过 Host Gateway 进入 TianShu。
- Host Gateway 只能暴露 typed host surface 和 projection。
- Control Plane 只负责 operation normalization、governance、routing。
- Kernel 是唯一拥有 StageGraph 解释和智能编排权的层。
- Execution Runtime 只执行已批准 runtime step。
- Module Plane 只响应授权 capability call，不反向调用上层。
- AppHost 只能是进程宿主、transport、lifecycle 和 composition root。

## 5. 专项文档入口

| 模块 | 文档 |
| --- | --- |
| Experience | `docs/architecture/tianshu-experience-plane-design.md` |
| Host Gateway | `docs/architecture/tianshu-host-gateway-design.md` |
| Control Plane | `docs/architecture/tianshu-control-plane-design.md` |
| Kernel / Core Loop | `docs/architecture/tianshu-kernel-core-loop-design.md` |
| Execution Runtime | `docs/architecture/tianshu-execution-runtime-design.md` |
| Module Plane | `docs/architecture/tianshu-module-plane-design.md` |
| Contracts | `docs/architecture/tianshu-contracts-architecture.md` |
| AppHost / Hosting | `docs/hosting/tianshu-apphost-hosting-design.md` |
| Provider Module | `docs/provider/tianshu-provider-module-design.md` |
