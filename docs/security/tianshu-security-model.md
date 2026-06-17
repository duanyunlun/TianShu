# TianShu Security Model

本文定义 TianShu 当前公开 CLI 路径的安全模型。它是 v1.0 文档门禁的一部分，用于说明默认边界、凭据处理、治理入口和已知限制。

## 安全目标

TianShu 的核心安全目标：

1. AI 不能绕过 Stable Kernel Core、governance、RuntimeStep approval、module trust、trace/audit 和 rollback 边界。
2. 默认 CLI 路径只开放低风险能力。
3. 所有写入、shell、远端 tool、Sub-Agent 等高风险能力必须显式授权，并留下可审计记录。
4. secret 不进入公开配置、日志、文档、发布包或验收证据。

## 默认边界

| 能力 | 默认状态 | 放行条件 |
| --- | --- | --- |
| 只读文件系统工具 | 可用 | 受 workspace resolver 和工具治理约束。 |
| workspace 写入 | 关闭 | 显式审批态、human gate、workspace-relative path、治理允许。 |
| shell | 关闭 | 后续能力面必须经过命令审批、cwd 限制、环境脱敏和输出截断。 |
| MCP tool | 关闭 | 远端副作用必须进入统一 ToolUse/governance/runtime step。 |
| Sub-Agent | 关闭 | `--enable-subagents --approve-all` 且治理授权 `spawn_agent` 与 `module.sub_agent`。 |
| Remote Module | 关闭 | 显式配对、短期 token、scope 和会话撤销。 |

## 凭据处理

- 配置模板只保存环境变量名，例如 `OPENAI_API_KEY`。
- TianShu 不要求把真实 key 写入 `tianshu.toml`。
- `doctor` 和诊断投影必须对 secret 值脱敏。
- Provider module 读取 secret 时应通过可注入环境读取器，不应在测试中写入常用进程级 key。
- 公开文档、release package、测试证据和 tracker 不得包含真实 key。

## 模块信任边界

第三方模块只能依赖公开 contracts / abstractions。模块不得依赖或调用：

- `TianShu.RuntimeComposition`
- `TianShu.Execution.Runtime`
- Host Gateway 实现
- Control Plane 实现
- Kernel 实现

模块必须经过 discovery、manifest validation、descriptor projection、configuration binding、health probe、trust / version / governance admission 后才能成为 active binding。

失败关闭条件包括：

- 缺 descriptor。
- 缺 health。
- 缺必需配置。
- 版本不兼容。
- trust 不足。
- governance 不允许。

## RuntimeStep 与审批

模型请求的能力必须被物化为 RuntimeStep 或 ModuleCapabilityStep，再由 Execution Runtime 执行。模型不能直接调用实现对象，也不能通过自然语言 plan 绕过 RuntimeStep approval。

写入类能力必须记录：

- 审批引用。
- tool id / module id。
- side effect level。
- workspace-relative path。
- 执行结果和失败原因。

## Provider 与网络

`tianshu doctor` 默认离线执行，不访问 provider。`tianshu doctor --probe` 才允许联网验证 endpoint/auth/protocol。

Provider 返回的原始 payload 属于模块内部实现细节。跨层输出必须投影为 TianShu 的类型化合同对象，避免把厂商私有结构扩散到 Kernel、Control Plane 或 Host Gateway。

## Remote Continuity

Remote Module 是可替换模块，不是内核内置公网服务。默认不得开放公网监听。

远端客户端只能查看脱敏状态、订阅事件、提交受治理命令。模型调用、工具执行、workspace 读写、artifact 生成和审计记录仍发生在运行 TianShu 的本机或受控宿主内。

## 已知限制

- 当前公开 Release 以 CLI 为主要入口；其他宿主的产品化安全体验还在后续路线图中。
- 自演化能力仍是探索性基础设施，不承诺成功，也不得绕过稳定内核。
- live 模型是否自主触发 Sub-Agent 是可观察行为，不作为安全边界放宽依据。
- P31.4 会继续补齐更完整的 secret scan、私有路径 scan、runtime state scan、测试产物 scan 和文档死链 scan。
