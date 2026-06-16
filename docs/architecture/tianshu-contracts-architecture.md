# TianShu Contracts 架构设计

## 1. 文档定位

本文定义 TianShu contracts 的当前有效归属。所有 contracts 必须服务于 `docs/tianshu-architecture-spec.md` 的六层主链和 `docs/architecture/tianshu-kernel-core-loop-design.md` 的 Kernel 接口基线。

Contracts 不代表独立运行层。它们是层与层之间的 typed boundary。

## 2. 当前项目现状

| 项目 | 当前用途 | 新基线下的定位 |
| --- | --- | --- |
| `TianShu.Contracts.Primitives` | 基础值对象。 | 所有 contracts 的低层依赖。 |
| `TianShu.Contracts.Host` | 宿主 surface。 | Host Gateway northbound 契约。 |
| `TianShu.Contracts.Configuration` | 配置契约。 | 配置模块和 Host Gateway projection 的 typed schema 来源。 |
| `TianShu.Contracts.Governance` | 治理、审批、权限。 | `GovernanceEnvelope` 和 tool permission 的来源之一。 |
| `TianShu.Contracts.Sessions`、`TianShu.Contracts.Workflows`、`TianShu.Contracts.Conversations` | 会话、工作流、对话。 | Control Plane operation 归一化输入和 projection 来源。 |
| `TianShu.Contracts.Orchestration` | 现有编排契约。 | 作为迁移输入，不再作为最终 Kernel IR 归属。 |
| `TianShu.Contracts.Execution` | 执行契约。 | `ExecutionPlan`、`RuntimeStep`、Execution Runtime 结果契约的归属。 |
| `TianShu.Contracts.Tools` | 工具契约。 | `ITianShuTool`、`ToolDescriptor`、schema、permission、side effect、audit 的归属。 |
| `TianShu.Contracts.Provider` | Provider 契约。 | Provider Module 能力声明和模型调用契约来源。 |
| `TianShu.Contracts.Memory`、`TianShu.Contracts.Identity` | 身份与记忆契约。 | Memory / Identity Module 的公共契约。 |
| `TianShu.Contracts.Artifacts`、`TianShu.Contracts.Diagnostics`、`TianShu.Contracts.Projections` | 工件、诊断、投影。 | RuntimeStep 结果、trace、Host Gateway projection 的公共契约。 |

## 3. 目标新增 Contracts

必须新增：

```text
src/Contracts/TianShu.Contracts.Kernel/TianShu.Contracts.Kernel.csproj
```

归属内容：

- `CoreIntent`
- `KernelSubjectRef`
- `StageGraph`
- `StageNode`
- `StageEdge`
- `KernelProposal`
- `KernelOperation`
- `KernelProjection`
- `KernelTrace`
- `KernelEvaluationResult`
- `StrategyRecord`
- strategy lifecycle enum

`TianShu.Contracts.Kernel` 可以引用基础 contracts，但不得引用 `Hosting`、`Execution.Runtime`、`Provider`、`Tools` 的实现项目。

## 4. 契约分布规则

| 契约类型 | 归属项目 |
| --- | --- |
| 宿主请求、宿主视图、snapshot/reset | `TianShu.Contracts.Host` |
| operation 归一化结果、governance envelope 引用 | `TianShu.Contracts.Governance` + `TianShu.Contracts.Kernel` |
| Kernel IR、proposal、operation、trace、strategy | `TianShu.Contracts.Kernel` |
| runtime step、execution plan、step result | `TianShu.Contracts.Execution` |
| tool descriptor、tool schema、tool permission、side effect | `TianShu.Contracts.Tools` |
| provider capability、model invocation input/output | `TianShu.Contracts.Provider` |
| module-owned domain payload | 对应 `TianShu.Contracts.*` |

## 5. 验收标准

- 新增跨层交互必须先有 typed contract。
- `TianShu.Contracts.Kernel` 不得依赖 AppHost、Execution Runtime 或具体 Module 实现。
- `TianShu.Contracts.Execution` 不得包含 StageGraph 解释逻辑。
- `TianShu.Contracts.Tools` 只能定义工具调用外壳和声明，不定义 Kernel 编排策略。
- 所有 public contract 必须能映射到六层之一。
