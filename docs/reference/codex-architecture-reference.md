# Codex 架构参考文档

## 1. 文档定位

本文基于曾随仓库附带的 Codex 源码快照整理 Codex 的架构设计，目标是为 TianShu 的目标架构提供历史参考材料，而不是做逐文件注释式说明。当前仓库已不再内置 `external/codex` 子仓库，后续实现不得依赖该本地源码路径作为事实来源。

本文刻意排除以下内容：

- 账号、登录、订阅与商业化相关设计。
- 遥测、埋点、反馈回传。
- 封禁、风控、账号恢复等治理细节。

本文关注的是源码中真正影响“代理运行时与产品内核边界”的部分。

## 2. 总体判断

从源码职责分布看，Codex 不是一个“CLI 套壳调用模型”的简单程序，而是一个由三层核心组成的系统：

1. `typed protocol`：稳定的 northbound 契约层。
2. `thread/session/turn runtime`：会话与执行内核。
3. `transport / gateway`：面向 CLI、IDE、Sidecar 的接入层。

它的关键特点不是某个具体模型 API，而是：

- 对外先定义协议，再定义 UI。
- 把 thread 当成权威对象，而不是聊天记录数组。
- 把 session 级稳定状态和 turn 级瞬时状态分开。
- 把 app-server 视为接入网关，而不是领域中心。

## 3. 仓库结构中的核心层次

按当前源码，最值得关注的是以下几个 crate。

| 模块 | 主要职责 | 对 TianShu 的启发 |
| --- | --- | --- |
| `codex-rs/protocol` | 定义 `Submission`、`Op`、`Event`、审批、权限、用户补录、plan tool、实时事件等强类型协议 | “先协议、后宿主”是核心边界 |
| `codex-rs/app-server-protocol` | 定义 northbound JSON-RPC 相关协议，并提供 schema / TS 导出 | 接入层协议导出应独立于执行内核 |
| `codex-rs/core` | 实现 thread manager、turn loop、model client、skills/plugins/mcp/runtime 协调 | 真正的运行时内核在这里 |
| `codex-rs/app-server` | 负责 stdio / websocket transport、JSON-RPC 消息处理、连接管理、配置 API 暴露 | gateway 是 transport 适配层，不应承载领域真义 |
| `codex-rs/state` | 将 rollout 元数据镜像进本地 SQLite，形成查询/回放辅助状态 | 状态镜像是 projection，不是主运行时 |

这几个模块合起来，形成的不是“一个程序”，而是一套有明确 northbound / runtime / projection 分层的代理系统。

## 4. 核心对象模型

### 4.1 ThreadManager 是顶层运行时入口

`core/src/thread_manager.rs` 表明 `ThreadManager` 是内存中 thread 集合的管理者，同时聚合以下运行时依赖：

- `ModelsManager`
- `SkillsManager`
- `PluginsManager`
- `McpManager`
- `EnvironmentManager`
- `AuthManager`

这意味着 Codex 并不是让每个 thread 自己去各处找依赖，而是由一个上层 manager 统一装配线程运行所需的共享能力。

`ThreadManager` 同时负责：

- 创建 thread。
- 维护 thread 映射。
- 对外广播 thread 创建事件。
- 管理技能 watcher 等全局旁路能力。

从职责上看，它更接近“会话空间中的线程控制器”。

### 4.2 CodexThread 是 thread 外观对象

`core/src/codex_thread.rs` 中的 `CodexThread` 并不自己实现完整推理循环，它更像 thread 的稳定外观：

- `submit()` / `submit_with_trace()`：向线程提交 `Op`。
- `next_event()`：从线程取事件。
- `steer_input()`：向活动 turn 注入补充输入。
- `shutdown_and_wait()`：结束线程。
- `config_snapshot()`：读取 thread 配置快照。
- `state_db()` / `rollout_path()`：暴露回放与状态镜像句柄。

这说明 Codex 的 thread 不是简单 DTO，而是一个长期存在、可交互、可恢复、可观察的运行对象。

### 4.3 Codex 才是真正的 turn 执行核心

`core/src/codex.rs` 的规模和依赖面说明，真正的执行主循环在 `Codex` 内部，而不是在 CLI 或 app-server。

从文件顶部依赖可以看出，`Codex` 同时协调：

- `ModelClient` / `ModelClientSession`
- `ContextManager`
- `ExecPolicyManager`
- `RealtimeConversationManager`
- hooks
- MCP 连接与工具调用
- compaction / memory / rollout
- agent mailbox / status
- proposed plan / turn items / stream parser

这说明 `Codex` 的角色并不是“模型客户端”，而是 `Submission -> Turn execution -> Event stream` 的执行机。

### 4.4 ModelClient 与 ModelClientSession 明确拆分 session / turn

`core/src/client.rs` 开头的注释非常重要，它明确指出：

- `ModelClient` 是 `session-scoped`。
- `ModelClientSession` 是 `turn-scoped`。

源码里这一拆分承担了非常明确的语义边界：

- `ModelClient` 保存稳定配置与状态：provider 选择、auth、conversation id、transport fallback 状态。
- `ModelClientSession` 只服务于单个 turn：缓存本 turn 的 websocket 连接、保存 `x-codex-turn-state` sticky token、保存本 turn 的请求上下文。

这是 Codex 最值得借鉴的设计之一：稳定会话状态与单次执行状态被强制分离，而不是混在一个“万能 runtime”对象里。

## 5. 协议层设计

### 5.1 protocol crate 是真正的 northbound 契约中心

`protocol/src/protocol.rs` 明确了 Codex 的基础交互模型：

- `Submission`
- `Op`
- `Event`
- `W3cTraceContext`

文件头部注释直接写明它采用 `SQ / EQ` 模式，也就是：

- Submission Queue：客户端提交操作。
- Event Queue：运行时回推事件。

这不是某个 UI 层的约定，而是运行时的正式协议。

### 5.2 审批、权限、用户补录、计划都是 typed 对象

从 `protocol` crate 导出的模块看，Codex 把高阶产品语义做成了独立协议，而不是散落在 UI 里拼 JSON：

- `approvals`
- `permissions`
- `request_permissions`
- `request_user_input`
- `plan_tool`

也就是说，在 Codex 里：

- “需要授权”是协议对象。
- “等待用户补录”是协议对象。
- “更新计划”是协议对象。

这套设计对 TianShu 非常关键，因为它说明真正稳定的边界不是“文本消息”，而是“控制平面事件与命令”。

### 5.3 app-server-protocol 负责 northbound 导出，而不是 core

`app-server-protocol/src/lib.rs` 明确承担了：

- JSON schema 导出。
- TypeScript 类型导出。
- JSON-RPC 协议对象导出。
- common / thread_history / v1 / v2 协议组织。

这个边界非常干净：

- `core` 负责语义和运行。
- `app-server-protocol` 负责 northbound contract export。

这意味着接入层协议是独立工件，而不是从运行时类型随手暴露。

## 6. 执行链路视角下的 Codex

把关键对象串起来，Codex 的主链路可以理解为：

1. 客户端通过 app-server 或 in-process 接口提交 `Submission(Op)`。
2. `ThreadManager` 定位或创建目标 thread。
3. `CodexThread` 作为 thread 外观接收操作。
4. `Codex` 将操作压入内部执行链，驱动 turn 运行。
5. turn 内部通过 `ModelClientSession` 调用 provider，并协调工具、审批、上下文、plan、事件映射。
6. 运行过程中生成 `Event`，通过 event queue 向外发布。
7. rollout 与 state mirror 在旁路持续记录，用于恢复、查询和回放。

这条链路最重要的特征是：对外永远看到的是 `Op/Event`，而不是 provider 原始协议。

## 7. manager 集群设计

从 `ThreadManager` 的构造与依赖可以看到，Codex 使用了一组 manager 作为 thread 共享能力：

### 7.1 ModelsManager

`models_manager/manager.rs` 的职责包括：

- 维护模型目录缓存。
- 按策略在线刷新或离线读取。
- 管理 collaboration mode 相关模型预设。
- 持有 provider 级远程模型发现能力。

它不是单纯返回 `models.json`，而是把模型目录变成可缓存、可刷新、可策略化的运行时服务。

### 7.2 SkillsManager / SkillsWatcher

技能能力并不是 turn 内部临时扫描，而是通过 manager + watcher 机制维护。其意义在于：

- 技能发现和缓存不是 UI 逻辑。
- 技能变更可以影响后续 turn 注入。
- 技能系统是运行时的一等能力，不是简单 prompt 拼接。

### 7.3 PluginsManager / McpManager

插件与 MCP 并非直接耦合在某个 UI 适配层里，而是由 manager 管理，再由 thread / turn 在需要时解析成实际工具面。

这使得 “能力目录” 与 “一次 turn 实际用到哪些能力” 两件事保持分离。

### 7.4 EnvironmentManager

虽然环境执行能力不在本文重点，但从 `ThreadManager` 对 `EnvironmentManager` 的依赖可以看出，运行环境也是共享基础设施，而不是分散在 tool handler 里即兴处理。

## 8. app-server 的角色

`app-server/src/lib.rs` 体现了 Codex 对 northbound transport 的定位：

- stdio / websocket 连接接入。
- JSON-RPC 消息分发。
- outbound message routing。
- 连接打开/关闭管理。
- graceful restart drain。
- 配置加载与 warning 转发。

最关键的一点是，它自身并不拥有 thread / turn 的业务真义。它管理的是：

- transport
- request dispatch
- connection lifecycle

这说明在 Codex 设计里，app-server 是网关，而不是领域中心。

## 9. 状态与持久化设计

### 9.1 rollout 是运行时旁路记录

从 `core` 导出的 rollout 相关 API 可以判断，Codex 会持续把 thread / turn 过程写入 rollout。

但 rollout 的角色更接近：

- 回放材料
- 历史记录
- state backfill 来源

而不是运行中的唯一状态容器。

### 9.2 state crate 是 projection，不是主状态机

`state/src/lib.rs` 的文件头注释写得非常清楚：

- 它负责把 rollout metadata 镜像进本地 SQLite。
- backfill orchestration 仍在 `codex-core`。

这意味着 Codex 明确区分：

- 主运行时状态。
- 只读查询/镜像状态。

这是非常成熟的边界划分。

## 10. 从架构思想看 Codex 的优点

### 10.1 northbound typed contract 非常稳定

Codex 的 `protocol` 和 `app-server-protocol` 让客户端面对的是：

- 命令
- 事件
- 审批对象
- 权限对象
- 计划对象

而不是模型厂商原始包体。

### 10.2 thread 是真正的一等对象

thread 有自己稳定的：

- 生命周期
- 配置快照
- 事件流
- rollout
- state db

这使得恢复、分叉、回放都不是事后补丁。

### 10.3 session / turn 分离非常清晰

`ModelClient` / `ModelClientSession` 的拆分，是 Codex 能做 websocket 复用、sticky routing、transport fallback，又不把 turn 状态污染到整个会话里的关键。

### 10.4 gateway 与内核分层明确

app-server 负责 transport，core 负责语义，state 负责投影。这个边界对于后续支持多宿主、多前端非常重要。

## 11. TianShu 应该借鉴什么

最值得借鉴的是以下几点：

1. 先定义 northbound typed protocol，再做 host / sidecar / UI。
2. 把 thread 当成权威运行对象，而不是消息数组。
3. 把 session 级稳定状态和 turn 级执行状态拆开。
4. 把模型目录、技能、插件、MCP、环境视为可组合的运行时 manager，而不是散落的 util。
5. 把持久化状态视为 projection / mirror，而不是直接暴露底层日志文件给上层。

## 12. TianShu 不应照搬什么

Codex 中也有一些内容不适合直接搬到 TianShu：

1. `OpenAI Responses / WebSocket sticky turn-state` 这套 provider 特化设计，不应上升为 TianShu 的通用控制平面。
2. `ModelClient` 内部对具体 provider header、fallback、transport 细节的处理，应下沉到 Provider Adapter，而不是成为 TianShu 北向契约。
3. rollout / JSONL / SQLite 的具体实现方式不必复刻，TianShu 只需要保留“运行态”和“投影态”分离的思想。
4. collaboration mode prompt、产品文案、Codex 特定 event 形状不能直接当作 TianShu 领域模型。

## 13. 对 TianShu 的直接结论

如果只保留一句最重要的结论，那就是：

> Codex 真正成熟的地方，不是某个模型调用技巧，而是它把“协议层、thread/session/turn 运行时、transport gateway、状态投影”拆成了不同边界。

TianShu 要学习的应是这套边界意识，而不是 OpenAI 特化的执行细节。
