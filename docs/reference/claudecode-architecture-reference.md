# ClaudeCode 架构参考文档

## 1. 文档定位

本文基于曾随仓库附带的 ClaudeCode 恢复源码快照整理 ClaudeCode 的架构设计，目标是为 TianShu 设计控制平面提供历史参考样本。当前仓库已不再内置 `external/claude-code-sourcemap` 子仓库，后续实现不得依赖该本地源码路径作为事实来源。

本文刻意排除以下内容：

- 账号、订阅、远程身份与登录流程。
- 遥测、GrowthBook、A/B 实验、内部分析事件。
- 封禁、企业策略、商业化治理逻辑。

本文关注的是 ClaudeCode 作为“代理产品运行时”的结构思想。

## 2. 总体判断

如果说 Codex 的核心是“typed protocol + thread runtime”，那么 ClaudeCode 的核心更像：

1. `启动装配层`
2. `全局会话状态层`
3. `QueryEngine + query loop`
4. `工具运行时`
5. `计划 / 任务 / 团队 / 子代理` 这些高阶产品对象

也就是说，ClaudeCode 的强项不是 northbound 协议设计，而是把代理产品语义做成了运行时中的一等对象。

## 3. 启动层设计

### 3.1 main.tsx 是总装配入口

`restored-src/src/main.tsx` 的职责非常重，它不是单纯的 CLI 启动文件，而是启动编排层：

- 读取配置、环境、特性开关。
- 初始化内建插件、技能、MCP、LSP、远程会话等外围能力。
- 组装 commands、tools、agents、session 状态。
- 决定当前会话模式、model、permission、工作目录等启动参数。

从这个文件可以明确看到，ClaudeCode 把“会话启动时需要装配的世界”视为一级问题，而不是让后续 query loop 临时去发现一切。

### 3.2 启动层已经体现产品化取向

从 `main.tsx` 的 import 面可以看到，它启动时就关心：

- 会话恢复
- mode 切换
- team / swarm / coordinator
- hooks
- 插件与技能
- session storage
- MCP 资源预取
- prompt suggestion / fast mode / thinking

这说明 ClaudeCode 从一开始就把自己当成“可治理的代理产品”，而不是单一模型 SDK 包装器。

## 4. 全局状态层设计

### 4.1 bootstrap/state.ts 是会话级权威状态容器

`restored-src/src/bootstrap/state.ts` 是 ClaudeCode 非常关键的设计点。它承载的不是纯 UI 状态，而是 session 级控制状态，包括但不限于：

- `projectRoot`
- `cwd`
- `sessionId`
- `parentSessionId`
- `mainLoopModelOverride`
- `sessionSource`
- `mainThreadAgentType`
- `allowedChannels`
- `registeredHooks`
- `hasExitedPlanMode`
- `sessionCreatedTeams`
- `invokedSkills`

可以看到，这里持有的是产品状态、运行约束、会话记忆和模式位，而不只是渲染层信息。

### 4.2 状态层承担的是“会话治理”职责

这层最值得关注的不是字段数量，而是字段性质。它们大多属于以下几类：

- 会话身份与 lineage
- 模式切换与功能门控
- 权限与行为策略
- 会话级缓存与上下文复用
- 与子代理/团队相关的长期状态

这和普通 CLI 程序里“当前命令的选项参数”完全不是一回事。

### 4.3 这说明 ClaudeCode 的真正中心是 Session

从状态层设计可以判断，ClaudeCode 的真正中心对象不是“单条消息”，而是整个 session。

Query loop、计划模式、团队、子代理、工具缓存、上下文折叠，都是围绕 session 工作，而不是围绕一次 API 调用工作。

## 5. QueryEngine 设计

### 5.1 一个 QueryEngine 对应一条持续会话轨道

`restored-src/src/QueryEngine.ts` 的类注释已经写得很清楚：

- 一个 `QueryEngine` 对应一个 conversation。
- 每次 `submitMessage()` 都是在同一 conversation 上开启一个新 turn。
- `messages`、`usage`、`readFileState`、`permissionDenials` 等状态跨 turn 持续存在。

这说明 ClaudeCode 把 query engine 视为“会话壳”，而不是“一次请求包装器”。

### 5.2 QueryEngine 负责的是会话级持续资源

它内部维护的关键资源包括：

- `mutableMessages`
- `AbortController`
- `permissionDenials`
- `totalUsage`
- `readFileState`
- `discoveredSkillNames`
- `loadedNestedMemoryPaths`

这意味着 QueryEngine 的职责是：

- 承接用户 turn。
- 保管跨 turn 资源。
- 调用真正的 `query()` 主循环。

也就是说，ClaudeCode 在对象层面明确区分了：

- 会话壳：`QueryEngine`
- 单次 turn 主循环：`query.ts`

## 6. query.ts 的主循环设计

### 6.1 query.ts 是真正的 agentic turn loop

`restored-src/src/query.ts` 是 ClaudeCode 的执行主循环，其文件顶部 import 面已经清楚显示出它的职责广度：

- auto compact
- reactive compact
- context collapse
- attachment / memory 处理
- command queue
- tool summary
- stop hooks
- token budget
- tool orchestration
- streaming tool execution
- API fallback / retry 配合

这说明 ClaudeCode 的“思考-调模型-跑工具-继续”循环不在 UI 中，而在一个专门的 turn loop 中。

### 6.2 这个循环的本质是状态机

从 `QueryParams`、`State` 和 `queryLoop()` 的组织方式看，ClaudeCode 的 turn 不是简单的：

`prompt -> API -> answer`

而更像：

1. 组装当前 turn 的 messages / system prompt / contexts。
2. 评估 token budget、cache、compact、collapse 策略。
3. 发起模型调用。
4. 流式接收输出。
5. 一旦出现 tool_use，就交给工具运行时。
6. 把 tool_result 再送回循环，决定是否继续。
7. 直到终态。

这是一个典型的 agentic state machine。

### 6.3 compact / collapse 是主循环的一部分

ClaudeCode 并没有把 context 管理当作“聊天 UI 的附加功能”，而是把以下能力纳入 turn 主循环：

- auto compact
- reactive compact
- context collapse
- microcompact
- compact boundary 管理

这说明在 ClaudeCode 的架构里，“上下文治理”就是 runtime 的一部分。

## 7. 工具运行时设计

### 7.1 工具执行被拆成三层

ClaudeCode 在工具运行时上的拆分相当成熟：

| 模块 | 职责 |
| --- | --- |
| `toolOrchestration.ts` | 负责按并发安全性分批执行工具 |
| `StreamingToolExecutor.ts` | 在流式生成过程中边接收边调度工具，并处理 streaming fallback / 中断场景 |
| `toolExecution.ts` | 单个工具的权限检查、执行、结果包装 |

这三个层次分别处理：

- 批次调度
- 流式并发与中断
- 单工具执行细节

### 7.2 toolOrchestration 把并发安全性变成正式概念

`toolOrchestration.ts` 不是简单地 `Promise.all()` 工具调用，而是显式区分：

- `isConcurrencySafe` 的工具：允许并发批量执行。
- 非并发安全工具：串行执行。

这代表 ClaudeCode 已经把“工具之间的冲突与共享状态风险”做成运行时规则，而不是调用方约定。

### 7.3 StreamingToolExecutor 解决的是流式代理的难点

`StreamingToolExecutor.ts` 的职责非常有代表性，它处理了真实代理系统里最麻烦的几个问题：

- 工具一边流出一边开始执行。
- 可并发工具与独占工具的混合调度。
- 用户中断时如何为正在执行的工具补 synthetic error。
- streaming fallback 时如何丢弃未完成的工具执行。
- 当某个并发工具失败时，如何取消兄弟工具。

这说明 ClaudeCode 不是把工具调用视为“模型返回后一次性处理”，而是把它纳入流式 runtime。

### 7.4 工具运行时与 UI 解耦

尽管 ClaudeCode 有大量 UI 代码，但工具运行时本身并不依赖 UI 组件。它依赖的是：

- tool definition
- canUseTool
- tool context
- message model

这说明其工具系统本质上是产品 runtime，而不是界面逻辑。

## 8. API 调用、重试与缓存设计

### 8.1 withRetry 是独立基础设施层

`services/api/withRetry.ts` 说明 ClaudeCode 把重试逻辑独立成一层基础设施，而不是散落在 query loop 中。

这个模块处理的不是普通 HTTP retry，而是带产品语义的策略：

- 根据 `querySource` 决定是否对 529 重试。
- 支持 fallback model。
- 区分前台阻塞查询与后台非关键查询。
- 在网络或容量抖动时做渐进退避。
- 在特定场景下处理 stale connection、OAuth 过期等问题。

这说明它已经把“请求治理”视为正式系统设计，而不是客户端小技巧。

### 8.2 querySource 是可治理维度

在 ClaudeCode 中，`querySource` 不是调试字段，而是影响行为的正式输入。它参与决定：

- 重试策略。
- fallback 行为。
- 某些缓存与统计路径。

这对于 TianShu 的启发非常直接：控制平面需要能表达“这次查询属于什么产品链路”，而不是只有“发给哪个模型”。

### 8.3 prompt cache 被纳入主设计

从 `bootstrap/state.ts` 与 `withRetry.ts` 以及相关 import 可以看出，prompt cache 不是偶然优化，而是 ClaudeCode 架构中的一等考虑：

- 1h TTL allowlist
- cache 相关 sticky header / latch
- compaction 对 cache 的影响

也就是说，它在架构上承认“上下文缓存命中”会反向影响运行策略。

## 9. 高阶产品对象

### 9.1 计划、任务、团队、子代理不是 UI 附件

从以下工具名可以明确看出 ClaudeCode 的产品内核已经超出“聊天”：

- `EnterPlanModeTool`
- `TodoWriteTool`
- `TaskCreateTool`
- `TeamCreateTool`
- `AgentTool`

这些对象不是页面装饰，而是 runtime 中可操作的实体。

### 9.2 Plan / Task / Team / Agent 形成了产品对象层

它表达的其实是四类产品语义：

- `Plan`：模式和步骤约束。
- `Task`：工作单元。
- `Team`：协作关系。
- `Agent`：执行主体。

也就是说，ClaudeCode 的“代理产品”已经进入控制平面层次，只是它没有像 Codex 那样先抽成一套独立 northbound protocol。

### 9.3 LocalAgentTask 与 AgentTool 表明子代理是一等能力

从 `LocalAgentTask`、`AgentTool` 这些模块命名可以判断，子代理不是普通工具调用，而是具有独立生命周期的任务体：

- 可启动。
- 可接收通知。
- 可回传结果。
- 可和主会话并行。

这对于 TianShu 极具启发意义，因为它说明多代理协作应该建模为运行时对象，而不是“某个 tool name 的偶然约定”。

## 10. coordinator mode 的架构意义

`coordinator/coordinatorMode.ts` 表明 ClaudeCode 明确区分了两类角色：

- `coordinator`
- `worker`

在 coordinator mode 下，系统会改变：

- 提示词上下文
- 可委派工具语义
- worker 的能力提示
- 协作流程约束

这说明角色并非提示词花样，而是运行时模式。

对 TianShu 来说，这意味着：

- 协调者/执行者不应只由 prompt 决定。
- 应该成为控制平面中的 agent role / execution envelope。

## 11. ClaudeCode 架构的长处

### 11.1 产品语义非常成熟

ClaudeCode 的突出优势在于，它已经把这些东西做成产品内核：

- 计划模式
- 任务列表
- 子代理
- 团队
- 协调模式

这比单纯的“模型+工具”更接近真正的代理产品。

### 11.2 会话治理很强

它显式维护：

- 模式位
- 预算
- hook
- 技能调用记录
- prompt cache 状态
- session lineage

这让它更像“会话操作系统”，而不是一次性聊天请求器。

### 11.3 工具运行时非常成熟

流式工具执行、中断、fallback、并发安全批次，这些都是代理 runtime 的难点，而 ClaudeCode 已经把它们系统化了。

## 12. ClaudeCode 不适合直接照搬的部分

虽然 ClaudeCode 很成熟，但它也有一些不适合 TianShu 直接复制的地方：

1. 它把很多产品语义直接嵌进单一 TypeScript 应用内，northbound 控制平面边界没有像 Codex 那样独立出来。
2. `bootstrap/state.ts` 的全局状态很强，但对 TianShu 来说应进一步抽成清晰的 bounded contexts，而不是继续堆全局变量。
3. query loop 与具体 provider 请求细节、缓存 header、产品实验开关有较多耦合，这些在 TianShu 中应下沉到 Provider Adapter 或 Engine Policy。
4. 很多模式切换仍以特性开关、环境变量、工具提示词为主要驱动，TianShu 应把其中稳定部分收敛为正式控制平面对象。

## 13. TianShu 应借鉴什么

最值得借鉴的是以下几点：

1. 会话状态应被视为产品核心，而不是 UI 状态。
2. QueryEngine 和 turn query loop 应拆开，分别承担会话级与 turn 级职责。
3. 工具运行时应独立分层，至少拆出批次编排、流式执行、单工具执行三个层次。
4. plan / task / team / agent / coordinator 这些对象必须上升为正式产品模型。
5. 重试、fallback、缓存命中策略应成为运行时治理层，而不是零散 if/else。

## 14. 对 TianShu 的直接结论

如果只保留一句最重要的结论，那就是：

> ClaudeCode 最成熟的地方，不是 Anthropic API 的调用方式，而是它把“会话治理、turn 主循环、工具运行时、计划/任务/团队/子代理”组合成了一套产品运行时。

TianShu 应该吸收这套产品对象层设计，但要把它进一步抽象成独立的控制平面，而不是复制成另一份 provider 特化 CLI 程序。
