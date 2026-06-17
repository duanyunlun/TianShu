<div align="center">

<img src="assets/branding/tianshu-logo.svg" alt="TianShu Logo" width="380" />

**一个以「可控演化内核」为核心的分层 AI Agent 运行时**
*A layered AI agent runtime built around a controlled-evolution kernel*

[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Version](https://img.shields.io/badge/version-0.9.1-blue.svg)](#路线图)
[![Architecture](https://img.shields.io/badge/architecture-6--plane-success.svg)](docs/tianshu-architecture-spec.md)

[中文](#中文文档) · [English](#english) · [快速开始](docs/usage/quickstart.md) · [模块接入](docs/usage/modules.md) · [架构规范](docs/tianshu-architecture-spec.md) · [故障排查](docs/usage/troubleshooting.md)

</div>

---

<a name="中文文档"></a>

## 中文文档

### 简介

**天枢(TianShu)** 是一个基于 .NET 10 构建的 AI Agent 运行时。它不是又一个「把 prompt 串起来调模型」的脚本框架,而是一套**以工程化、可治理、可审计为第一原则**的 Agent 内核。

它有两个核心特性:

- **🧩 极致模块化** — 第三方开发者可以在不碰内核的前提下,接入自己的模型(Provider)、工具(Tool)和能力模块。模块只需实现稳定的类型化契约,装配时接入。详见 **[模块接入指南](docs/usage/modules.md)**。
- **🧬 可控演化(探索中)** — 天枢的长期目标是让 AI 参与编排策略的自我演化,同时保证权限、状态、审计永不被绕过。这是一个尚无业界定论的开放方向,天枢保留了它所需的全部架构挂载点,并在路线图中诚实推进。

> 天枢得名于北斗第一星(枢纽之意):所有意图在此归一,所有执行由此发散。

> 它回答一个核心问题:**当我们把「编排逻辑」交给 AI 自己生成时,如何保证系统的边界、权限、状态机和审计永远不被绕过?** 答案是把内核拆成两层——**不可绕过的稳定内核** 与 **可由 AI 参与演化的编排层**,所有 AI 产出的编排方案都必须物化为**可验证的 StageGraph 中间表示(IR)**,经稳定内核校验后才能执行。架构细节见 [它如何工作](#它如何工作) 与 [架构规范](docs/tianshu-architecture-spec.md)。

### 快速开始

> 当前 P0 公开可用路径聚焦 CLI。Config GUI、AppHost、VSIX 等宿主仍在架构内，但不是首个公开 Release 的阻塞入口。

**环境要求:** [.NET 10 SDK](https://dotnet.microsoft.com/download)

#### 路径一：从源码运行

```bash
git clone https://github.com/duanyunlun/TianShu.git
cd TianShu

dotnet build src/Presentations/TianShu.Cli/TianShu.Cli.csproj
dotnet run --project src/Presentations/TianShu.Cli -- init --provider openai
dotnet run --project src/Presentations/TianShu.Cli -- doctor
```

#### 路径二：使用 Release 包

从 GitHub Release 下载对应平台的目录式压缩包，解压后把目录加入 `PATH`，或直接执行目录内入口：

```powershell
.\tianshu.exe init --provider openai
.\tianshu.exe doctor
```

当前包形态是目录式发布包，不启用 single-file 或 trimming。Windows 包入口为 `tianshu.exe`；非 Windows 包入口为 `tianshu`。

#### 首次配置

`tianshu init` 会在用户目录生成公开默认配置和 provider 模板：

- Windows：`%USERPROFILE%\.tianshu\tianshu.toml`
- Linux/macOS：`~/.tianshu/tianshu.toml`
- 模块模板：`modules/model/provider-instances/default.toml`、`modules/model/route-sets/default.toml`、`modules/model/protocol-rules/default.toml`

默认模板只保存环境变量名，不保存 secret。可选 provider 模板：

| Provider | 协议 | 默认模型 | 环境变量 |
| --- | --- | --- | --- |
| `openai` | OpenAI Responses | `gpt-5.5` | `OPENAI_API_KEY` |
| `anthropic` | Anthropic Messages | `claude-opus-4.8` | `ANTHROPIC_API_KEY` |
| `openai-compatible` | OpenAI-compatible Chat Completions | `openai-compatible-default` | `OPENAI_COMPATIBLE_API_KEY` |

```powershell
tianshu init --provider openai
$env:OPENAI_API_KEY = "<your key>"
tianshu doctor
tianshu doctor --probe
tianshu send --message "帮我分析这个仓库的结构" --json
```

`tianshu doctor` 默认离线执行，不联网、不调用模型、不产生 API 成本；它会同时报告 Provider 配置、模块发现/加载、模块健康、缺失配置、治理风险与修复建议。`tianshu doctor --probe` 才会显式联网探测 endpoint/auth/protocol 基础可达性。凭据缺失时，天枢会**失败关闭**并给出明确 failure code，例如 `provider_api_key_missing`。

#### Windows 清理

删除 CLI Release 解压目录，并删除 `%USERPROFILE%\.tianshu` 即可清理用户配置。若只想重建配置，可保留目录并执行：

```powershell
tianshu init --provider openai --force
```

### 用法与扩展

| 我想…… | 看这里 |
| --- | --- |
| 安装、初始化、自检和发送第一条消息 | **[快速开始](docs/usage/quickstart.md)** |
| 接入自定义模型 / 工具 / 能力模块 | **[模块接入指南](docs/usage/modules.md)** |
| 了解配置结构与 provider 切换 | [快速开始 · 首次配置](#快速开始) |
| 理解整体架构与设计取舍 | [它如何工作](#它如何工作) · [架构规范](docs/tianshu-architecture-spec.md) |
| 排查 provider、模块、写工具、Sub-Agent 或 Release 包问题 | [故障排查](docs/usage/troubleshooting.md) |
| 理解默认安全边界、secret 处理和治理模型 | [安全模型](docs/security/tianshu-security-model.md) |
| 查看发布变化、验证状态和已知限制 | [Release Notes](docs/publishing/release-notes.md) |

> 天枢的核心特性之一是**模块化**:你可以在不修改内核的前提下,接入自己的 Provider、Tool 与能力模块。如果你想扩展天枢,[模块接入指南](docs/usage/modules.md) 是最佳起点。

### 它如何工作

天枢采用严格的**六层主链架构**,每层职责单一、边界清晰,由源码级架构守护测试钉死:

```text
Experience Plane   体验层      CLI / VSIX / Web / 嵌入式宿主
      ▼
Host Gateway       宿主网关    统一、稳定、类型化的控制入口
      ▼
Control Plane      控制层      operation 归一化、治理与路由
      ▼
Kernel / Core Loop 内核层      可控演化编排内核(双子层,见下)
      ▼
Execution Runtime  执行运行时   runtime step 执行、provider 调用、工具分派
      ▼
Module Plane       模块层      可装配、可替换、可第三方实现的能力模块
```

最关键的是**内核层的双子层设计**:

```text
Kernel / Core Loop
  ├─ Stable Kernel Core           不可绕过:边界、状态机、不变量、权限、审计、
  │                               checkpoint、StageGraph 验证器与解释器
  └─ Adaptive Orchestration Layer  AI 参与生成/选择/修正/评估编排资产
```

**核心不变量:AI 不能只用自然语言 plan 影响内核。** 编排层产出的任何方案,都必须先物化为**可验证的 StageGraph IR**,由稳定内核校验通过后才能执行。固定 turn 图的反应式执行链为:

```text
prepare-context  →  model-reason  ⇄  tool-exec  →  finalize
                        ↑______________│   (按模型 toolRequests[] 物化工具 step,
                                            结果作为 function_call_output 回流下一轮)
```

> 完整的层职责、跨层不变量、生命周期与演化结论见 [架构规范](docs/tianshu-architecture-spec.md)。

### 设计文档

天枢的架构以文档为 source-of-truth,代码必须与文档边界对齐:

| 文档 | 内容 |
| --- | --- |
| [总体架构规范](docs/tianshu-architecture-spec.md) | 六层职责、跨层不变量、生命周期、可控演化内核的验收基线 |
| [分层架构索引](docs/architecture/tianshu-planes-architecture.md) | 各层专项设计入口 |
| [内核核心循环设计](docs/architecture/tianshu-kernel-core-loop-design.md) | Stable Kernel Core + Adaptive Orchestration Layer |
| [固定 StageGraph 设计](docs/architecture/tianshu-builtin-stage-graph-design.md) | 内置 turn / interrupt / resume StageGraph 与演化结论 |
| [执行运行时设计](docs/architecture/tianshu-execution-runtime-design.md) | runtime step 执行、provider/tool bridge、metrics/trace |
| [控制层设计](docs/architecture/tianshu-control-plane-design.md) | operation 归一化、治理、路由 |
| [宿主网关设计](docs/architecture/tianshu-host-gateway-design.md) | typed host surface 投影 |
| [契约架构](docs/architecture/tianshu-contracts-architecture.md) | 跨层类型化契约边界 |
| [安全模型](docs/security/tianshu-security-model.md) | 默认能力边界、secret 处理、模块信任和 RuntimeStep 审批 |
| [Release Notes](docs/publishing/release-notes.md) | 用户可见变化、验证状态与已知限制 |

### 路线图

当前公开发布版本 **v0.9.1**。这是面向公开仓库的 CLI / Windows x64 便携包发布基线。v0.6-v0.9 的模块化、受治理能力面、远程连续性接口和多 Agent 机制已进入发布门禁；v0.10 自演化仍是探索性基础设施，v1.0 稳定化继续收口。

#### ✅ v0.5.0 · 可控内核可用

- [x] 可控演化内核 + StageGraph IR(双子层:稳定内核 + 编排层)
- [x] 六层主链架构 + 源码级架构守护测试
- [x] 固定 turn 反应式 loop:prepare-context → model-reason ⇄ tool-exec → finalize
- [x] 多 provider live:OpenAI Responses / Anthropic Messages / OpenAI-compatible Chat Completions
- [x] 只读工具默认开放,写工具 human gate + 审批 + 路径沙箱
- [x] 串行 sub-agent 机制(嵌套受治理 turn + 结构闸门防 fork 炸弹)
- [x] 目录式 Release 包 + 首次自举(`init`)+ 离线自检(`doctor`)+ 联网探测(`doctor --probe`);Windows 已 smoke, Linux/macOS 包形态已生成,跨平台 smoke 进入后续补验

#### 🧩 v0.6.0 · 模块化开放

- [x] Module SDK + 模板 / 示例项目,第三方可在不碰内核的前提下开发模块
- [x] Provider 模块公开接入规范 + 从零写一个自定义 provider 教程
- [x] Tool 模块公开接入规范 + 注册一个自定义工具教程
- [x] Memory 模块公开接入规范
- [x] 模块发现 / 加载 / 治理的稳定公开契约
- [x] 模块装配文档(组合层注入、信任边界、健康检查)

#### 🛠 v0.7.0 · 能力面扩展

- [x] 受治理的 write / apply_patch(审批 + 工作区沙箱 + 冲突/回滚)
- [x] 受治理的 shell(命令审批 + cwd 限制 + 环境脱敏 + 输出截断)
- [x] MCP 接入(resource 只读接入 + tool 远端副作用治理)
- [x] 结构化上下文管理(token 作压缩触发器 + 分层降级 + supersede 取舍,压缩可逆可审计)
- [x] Memory 模块能力开放(检索 / 形成 / 取代)

#### 📡 v0.8.0 · 远程连续性接口与 Remote Module

- [x] 线程状态投影接口:thread snapshot、run state、stage/tool/sub-agent 状态、pending approval、artifact、diagnostics
- [x] 事件流订阅接口:SSE/WebSocket/event cursor/reconnect,远端设备可只读跟随当前工作线程
- [x] 远程控制命令:submit message、steer、interrupt、resume、approval decision,全部回到 Host Gateway / Control Plane
- [x] Remote Module 示例实现:可替换的远端收发模块,承载 HTTP/SSE/WebSocket、device pairing、短期 token、会话撤销
- [x] 远端安全边界:远端不直接访问本地 workspace/runtime state,不绕过 Kernel/Runtime,高风险动作继续走 human gate
- [x] 多宿主体验收敛:VS 扩展(VSSDK Sidecar + VSExtension)、Config GUI、AppHost 作为 Host Gateway 消费端接入
- [x] 移动端/Web/云中继作为消费形态,不要求移动设备本地执行工作负载

远程连续性的边界是**状态/控制接口 + 可替换 Remote Module**。移动端、Web 页面或云中继只负责查看脱敏线程状态、订阅事件、提交 follow-up/interrupt/resume/approval 等受治理命令；模型调用、工具执行、workspace 读写、artifact 生成和审计记录仍发生在运行 TianShu 的本机或受控宿主内。云中继也只是 transport adapter,不拥有内核控制权。

#### ✅ v0.9.0 · 多 Agent 成熟

- [x] 并行 fanout(激活 `maxConcurrentAgents` 并发闸门 + 结果 fan-in)
- [x] sub-agent 自主触发观测矩阵(v0.5 已完成串行基线观测:27 次 / 0 自主触发;v0.9 在并行 fanout 场景下扩展多协议 × 多任务 × 多轮观测,诚实记录触发率)
- [x] 子树治理 / 预算切分 / 整树复盘完善
- [x] v0.9 多 Agent 发布门禁:默认不暴露 `spawn_agent`,仅在 `--enable-subagents --approve-all` 与治理同时授权时开放;确定性 fanout/fan-in 机制必过,live 自主触发只作为公开观察报告记录,不承诺一定触发

#### 🧬 v0.10.0 · 自演化基础设施(探索中)

- [ ] Adaptive orchestrator 候选生成(propose 多个 StageGraph 变体)
- [ ] 候选闭环:validate(确定性内核校验)→ trial(影子/试运行)
- [ ] **Evaluator 度量基础设施** — 攻「如何判断一张图比另一张更好」这一开放难题
- [ ] 异质交叉评审实验:不同厂商模型(A 执行 / B、C 评审)产出带分歧度的信号
- [ ] 客观锚点校准:用测试通过 / 编译成功 / 标准答案校准模型裁判的可信度
- [ ] 统计聚合:从单次结果评审上升到「策略 X 优于策略 Y」的判断
- [ ] strategy registry:promotion / rollback 生命周期

> ⚠️ 自演化方向尚无业界定论。P31.8 正式报告当前结论为**部分可行**:受控实验链路可行,真实长期收益和可靠自主进化仍未证明,不承诺成功。

#### 🚀 v1.0.0 · 稳定 + 自演化可行性结论

- [ ] 核心 API 稳定承诺 + 版本兼容策略
- [ ] 模块生态成型(若干示范第三方模块)
- [ ] **自演化可行性报告**(当前结论:部分可行;控制链路可行,收益可测性和可靠自主进化仍未证明)
- [ ] 生产级文档与发布门禁

> 自演化是天枢最有野心的方向。天枢的态度是:**保留它所需的全部架构挂载点,诚实地分阶段推进,不在能客观度量收益之前伪装它已成功。**

### 愿景

天枢的长期目标是成为一个**工程化、可治理、可被信任的开源 Agent 运行时**——让「AI 自主编排」不再以牺牲安全边界为代价。

我们相信下一代 Agent 系统的竞争力不在于「能不能调用工具」,而在于:

1. **能否在 AI 自主决策的同时,保证权限、状态、审计永不被绕过;**
2. **能否把每一次执行都变成可重放、可归因、可度量的工程事件;**
3. **能否诚实地区分「已验证的能力」与「尚未度量的设想」。**

天枢用「稳定内核 + 可演化编排层 + 可验证 StageGraph IR」这套架构,为这三个问题给出了一个可落地的答案。

### 贡献

欢迎 issue 与 PR。提交前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md) 了解协作约定与架构纪律。核心原则:**任何实现都必须与架构规范的层边界对齐;跨层交互必须走类型化契约;新功能必须走 `CoreIntent → StageGraph → RuntimeStep` 主线。**

### 开源协议

本项目采用 **[Apache License 2.0](LICENSE)**。

---

<a name="english"></a>

## English

### Introduction

**TianShu** is an AI agent runtime built on .NET 10. It is not another "chain-some-prompts-and-call-a-model" scripting framework, but an agent kernel with **engineering rigor, governability, and auditability as first principles**.

It has two core features:

- **🧩 Deep modularity** — Third-party developers can plug in their own models (Provider), tools (Tool), and capability modules **without touching the kernel**. Modules only implement stable typed contracts and are wired in at composition time. See the **[Module Integration Guide](docs/usage/modules.md)**.
- **🧬 Controlled evolution (exploratory)** — TianShu's long-term goal is to let AI participate in the self-evolution of orchestration strategy while keeping permissions, state, and audit un-bypassable. This is an open problem with no industry consensus yet; TianShu retains every architectural mount point it needs and advances it honestly on the roadmap.

> TianShu (天枢) is named after the first ("pivot") star of the Big Dipper: all intents converge here, all execution radiates from here.

> It answers one core question: **when we let an AI generate the orchestration logic itself, how do we guarantee that the system's boundaries, permissions, state machine, and audit trail can never be bypassed?** The answer is a two-layer kernel — an **un-bypassable Stable Kernel Core** plus an **AI-evolvable Adaptive Orchestration Layer** — where every plan the AI produces must be materialized as a **verifiable StageGraph IR** and validated by the stable core before it can execute. See [How It Works](#how-it-works) and the [architecture spec](docs/tianshu-architecture-spec.md).

### Quick Start

> The public P0 usability path is CLI-first. Config GUI, AppHost, VSIX, and other hosts remain part of the architecture, but they are not blockers for the first public Release.

**Requirements:** [.NET 10 SDK](https://dotnet.microsoft.com/download)

#### Path 1: run from source

```bash
git clone https://github.com/duanyunlun/TianShu.git
cd TianShu

dotnet build src/Presentations/TianShu.Cli/TianShu.Cli.csproj
dotnet run --project src/Presentations/TianShu.Cli -- init --provider openai
dotnet run --project src/Presentations/TianShu.Cli -- doctor
```

#### Path 2: use a Release package

Download the platform archive from GitHub Releases, extract it, then add the extracted directory to `PATH` or run the entry directly:

```powershell
.\tianshu.exe init --provider openai
.\tianshu.exe doctor
```

Release packages are directory-style archives. TianShu does not use single-file publish or trimming for P0. The Windows entry is `tianshu.exe`; non-Windows packages use `tianshu`.

#### First-run configuration

`tianshu init` creates public default configuration and provider templates:

- Windows: `%USERPROFILE%\.tianshu\tianshu.toml`
- Linux/macOS: `~/.tianshu/tianshu.toml`
- Module templates: `modules/model/provider-instances/default.toml`, `modules/model/route-sets/default.toml`, `modules/model/protocol-rules/default.toml`

Templates store environment variable names only, never secret values.

| Provider | Protocol | Default model | Environment variable |
| --- | --- | --- | --- |
| `openai` | OpenAI Responses | `gpt-5.5` | `OPENAI_API_KEY` |
| `anthropic` | Anthropic Messages | `claude-opus-4.8` | `ANTHROPIC_API_KEY` |
| `openai-compatible` | OpenAI-compatible Chat Completions | `openai-compatible-default` | `OPENAI_COMPATIBLE_API_KEY` |

```powershell
tianshu init --provider openai
$env:OPENAI_API_KEY = "<your key>"
tianshu doctor
tianshu doctor --probe
tianshu send --message "Analyze the structure of this repository" --json
```

`tianshu doctor` is offline by default: no network, no model call, no API cost. It also reports provider configuration, module discovery/loading, module health, missing configuration, governance risks, and repair suggestions. `tianshu doctor --probe` explicitly performs a network probe for endpoint/auth/protocol reachability. When credentials are missing, TianShu **fails closed** with an explicit failure code such as `provider_api_key_missing`.

#### Windows cleanup

Delete the extracted CLI Release directory and `%USERPROFILE%\.tianshu`. To regenerate configuration without deleting the directory:

```powershell
tianshu init --provider openai --force
```

### Usage & Extension

| I want to… | Go to |
| --- | --- |
| Install, initialize, self-check, and send the first message | **[Quickstart](docs/usage/quickstart.md)** |
| Plug in a custom model / tool / capability module | **[Module Integration Guide](docs/usage/modules.md)** |
| Understand config layout & provider switching | [Quick Start · First-run configuration](#quick-start) |
| Understand the overall architecture & trade-offs | [How It Works](#how-it-works) · [Architecture Spec](docs/tianshu-architecture-spec.md) |
| Troubleshoot provider, module, write-tool, Sub-Agent, or Release package issues | [Troubleshooting](docs/usage/troubleshooting.md) |
| Understand default safety boundaries, secret handling, and governance | [Security Model](docs/security/tianshu-security-model.md) |
| Read release changes, validation status, and known limitations | [Release Notes](docs/publishing/release-notes.md) |

> One of TianShu's core features is **modularity**: you can plug in your own Provider, Tool, and capability modules without modifying the kernel. The [Module Integration Guide](docs/usage/modules.md) is the best starting point.

<a name="how-it-works"></a>
### How It Works

TianShu uses a strict **six-plane architecture**, each plane single-purpose with clear boundaries pinned by source-level guard tests:

```text
Experience Plane     CLI / VSIX / Web / Embedded Host
      ▼
Host Gateway         Unified, stable, typed control entry
      ▼
Control Plane        Operation normalization, governance, routing
      ▼
Kernel / Core Loop   Controlled-evolution kernel (dual-sublayer, below)
      ▼
Execution Runtime    Runtime step execution, provider calls, tool dispatch
      ▼
Module Plane         Composable, replaceable, third-party capabilities
```

The key is the **dual-sublayer kernel**:

```text
Kernel / Core Loop
  ├─ Stable Kernel Core             un-bypassable: boundaries, state machine, invariants,
  │                                 permissions, audit, checkpoint, StageGraph validator
  └─ Adaptive Orchestration Layer   AI generates / selects / revises / evaluates assets
```

**Key invariant: AI cannot influence the kernel through natural-language plans alone.** Any plan must first be materialized as a **verifiable StageGraph IR**, validated by the stable core before execution. The fixed turn graph's reactive chain:

```text
prepare-context  →  model-reason  ⇄  tool-exec  →  finalize
                        ↑______________│   (tool steps materialized from the model's
                                            toolRequests[], results fed back as
                                            function_call_output on the next turn)
```

> Full plane responsibilities, cross-plane invariants, lifecycle, and evolution conclusions are in the [architecture spec](docs/tianshu-architecture-spec.md).

### Roadmap

Current public release **v0.9.1**. This is the public-repository baseline for the CLI / Windows x64 portable package. v0.6-v0.9 modularity, governed capability surfaces, remote continuity interfaces, and multi-agent mechanisms are now covered by release gates; v0.10 self-evolution remains exploratory infrastructure, and v1.0 stabilization continues.

#### ✅ v0.5.0 · Controlled kernel usable

- [x] Controlled-evolution kernel + StageGraph IR (dual sublayer: stable core + orchestration layer)
- [x] Six-plane architecture + source-level architecture guard tests
- [x] Fixed reactive turn loop: prepare-context → model-reason ⇄ tool-exec → finalize
- [x] Multi-provider live: OpenAI Responses / Anthropic Messages / OpenAI-compatible Chat Completions
- [x] Read-only tools open by default; write tools require human gate + approval + path sandbox
- [x] Serial sub-agent mechanism (nested governed turn + structural gates against fork bombs)
- [x] Directory-style Release packages + first-run bootstrap (`init`) + offline self-check (`doctor`) + network probe (`doctor --probe`); Windows has been smoke-tested, Linux/macOS packages are generated, and cross-platform smoke remains a follow-up validation item

#### 🧩 v0.6.0 · Modularity opened

- [x] Module SDK + template projects; third parties author modules without touching the kernel
- [x] Public Provider integration spec + "write a custom provider from scratch" tutorial
- [x] Public Tool integration spec + "register a custom tool" tutorial
- [x] Public Memory module integration spec
- [x] Stable public contracts for module discovery / loading / governance
- [x] Module composition docs (composition-root injection, trust boundaries, health checks)

#### 🛠 v0.7.0 · Capability surface expansion

- [x] Governed write / apply_patch (approval + workspace sandbox + conflict/rollback)
- [x] Governed shell (command approval + cwd restriction + env redaction + output truncation)
- [x] MCP integration (read-only resource access + governed remote-side-effect tools)
- [x] Structured context management (token as compaction trigger + tiered demotion + supersede-based pruning, reversible & auditable)
- [x] Memory module capabilities opened (retrieve / form / supersede)

#### 📡 v0.8.0 · Remote continuity interfaces and Remote Module

- [x] Thread state projection interfaces: thread snapshot, run state, stage/tool/sub-agent state, pending approvals, artifacts, diagnostics
- [x] Event stream subscriptions: SSE/WebSocket/event cursor/reconnect so remote devices can follow the active work thread read-only
- [x] Remote control commands: submit message, steer, interrupt, resume, approval decision, all routed back through Host Gateway / Control Plane
- [x] Reference Remote Module implementation: replaceable remote send/receive module for HTTP/SSE/WebSocket, device pairing, short-lived tokens, session revocation
- [x] Remote safety boundary: remote clients do not directly access local workspace/runtime state, cannot bypass Kernel/Runtime, and high-risk actions still go through human gate
- [x] Multi-host experience convergence: VS extension (VSSDK Sidecar + VSExtension), Config GUI, and AppHost connect as Host Gateway consumers
- [x] Mobile, Web, and cloud relay are consumption forms; mobile devices are not required to execute local workloads

The remote-continuity boundary is **state/control interfaces + replaceable Remote Modules**. Mobile clients, Web pages, and cloud relays only view redacted thread state, subscribe to events, and submit governed commands such as follow-up, interrupt, resume, or approval decisions. Model calls, tool execution, workspace access, artifact generation, and audit records still happen on the machine or controlled host running TianShu. A cloud relay is only a transport adapter; it does not own kernel control.

#### ✅ v0.9.0 · Multi-agent maturation

- [x] Parallel fanout (activate `maxConcurrentAgents` gate + result fan-in)
- [x] Sub-agent autonomous-trigger observation matrix (v0.5 completed the serial baseline observation: 27 runs / 0 autonomous triggers; v0.9 expands observation across multi-protocol × multi-task × multi-round parallel fanout scenarios, honestly recording trigger rate)
- [x] Sub-tree governance / budget split / whole-tree replay refinement
- [x] v0.9 multi-agent release gate: hide `spawn_agent` by default, expose it only with `--enable-subagents --approve-all` plus governance authorization; deterministic fanout/fan-in must pass, while live autonomous triggering is recorded as a public observation report without promising it will happen

#### 🧬 v0.10.0 · Self-evolution infrastructure (exploratory)

- [ ] Adaptive orchestrator candidate generation (propose multiple StageGraph variants)
- [ ] Candidate loop: validate (deterministic kernel check) → trial (shadow/trial run)
- [ ] **Evaluator measurement infrastructure** — tackle the open problem of "how to judge one graph better than another"
- [ ] Heterogeneous cross-review experiment: different vendors' models (A executes / B, C review) produce signals with disagreement scores
- [ ] Objective-anchor calibration: use tests passing / compilation success / ground truth to calibrate judge reliability
- [ ] Statistical aggregation: rise from single-result review to "strategy X is better than strategy Y"
- [ ] Strategy registry: promotion / rollback lifecycle

> ⚠️ Self-evolution has no industry consensus yet. The P31.8 report currently concludes **partially feasible**: the governed experiment loop is feasible, while long-term benefit and reliable autonomous evolution remain unproven.

#### 🚀 v1.0.0 · Stable + self-evolution feasibility conclusion

- [ ] Core API stability commitment + version compatibility policy
- [ ] Mature module ecosystem (several reference third-party modules)
- [ ] **Self-evolution feasibility report** (current conclusion: partially feasible; the control loop is feasible, while measurable benefit and reliable autonomous evolution remain unproven)
- [ ] Production-grade docs and release gating

> Self-evolution is TianShu's most ambitious direction. The stance: **retain every architectural mount point it needs, advance honestly in phases, and never pretend it has succeeded before its benefit can be objectively measured.**

### Vision

TianShu's long-term goal is to become an **engineering-grade, governable, trustworthy open-source agent runtime** — making "autonomous AI orchestration" no longer come at the cost of sacrificing safety boundaries. We believe the competitiveness of next-generation agent systems lies not in *whether they can call tools*, but in whether they can keep permissions, state, and audit un-bypassable while the AI decides autonomously; turn every execution into a replayable, attributable, measurable engineering event; and honestly distinguish *verified capabilities* from *unmeasured ideas*.

### Contributing

Issues and PRs welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) for collaboration conventions and architectural discipline. Core rule: **every implementation must align with the architecture spec's plane boundaries; cross-plane interaction must go through typed contracts; new features must follow the `CoreIntent → StageGraph → RuntimeStep` main line.**

### License

Licensed under the **[Apache License 2.0](LICENSE)**.

---

<div align="center">
<sub>天枢 · TianShu — orchestration pivot for trustworthy AI agents</sub>
</div>
