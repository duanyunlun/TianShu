# TianShu 工作目录实施规则

本文件适用于当前仓库根目录。

## 1. 文档分工

### 1.1 设计规范主文档

文件：`docs/tianshu-architecture-spec.md`

规则：

- 它是 TianShu 的详细设计文档与未来设计规范主入口。
- 任何已经讨论通过、并准备长期保留的架构结论，都必须实时并入这份文档。
- 任何会改变对外契约、顶层分层、核心对象模型、控制平面边界、执行引擎边界、能力模型的实现工作，都必须同步更新这份文档。

### 1.2 参考文档

文件：

- `docs/reference/codex-architecture-reference.md`
- `docs/reference/claudecode-architecture-reference.md`

规则：

- 它们仅作为参考入口和索引。
- 默认不修改。
- 未来若需要参考，优先通过这两份文档定位，再回到对应源代码中查证。
- 除非用户明确要求更新参考文档，否则不得主动编辑。

### 1.3 实施跟踪文档

文件：`docs/tianshu-implementation-tracker.md`

规则：

- 它是从当前时刻开始的唯一实施跟踪入口。
- 所有实际落地工作都必须在此追踪。
- 文档必须精简，只记录“正在做”和“已完成”的内容。
- 不写长篇设计说明，不写讨论过程，不写大段背景解释。

## 2. 工作流程规则

### 2.1 固定工作顺序

从当前时刻开始，TianShu 的默认工作顺序固定如下：

1. 先讨论需要落地的内容。
2. 讨论形成稳定结论后，先写入对应设计文档。
3. 再根据该部分设计文档落地代码，并同时补齐对应测试代码。
4. 落地代码与测试的同时，持续更新 `docs/tianshu-implementation-tracker.md`。
5. 最终以代码、测试和实际可验证结果为准，而不是以讨论文本或设计文档表述为准。

除非用户明确要求跳过某一步，否则不得打乱上述顺序。

### 2.2 讨论通过后先改规范

若某项讨论已经形成稳定结论，并将作为 TianShu 的正式设计约束：

- 先更新 `docs/tianshu-architecture-spec.md`
- 再开始对应落地工作

### 2.3 实施开始前先更新跟踪文档

在开始任何实质性的设计落地、接口骨架、测试骨架、代码实现之前，必须先更新：

- `docs/tianshu-implementation-tracker.md` 的“正在做”

### 2.4 实施完成后立即回写

每完成一项实际落地工作后，必须同步：

- 将对应条目从“正在做”移动到“已完成”
- 若该工作影响正式设计，同步更新 `docs/tianshu-architecture-spec.md`

### 2.5 讨论结论直接归档到设计文档

对话中若出现以下内容，并已经形成稳定结论，应直接写入 `docs/tianshu-architecture-spec.md` 或对应模块设计文档：

- 新的架构抽象
- 新的边界划分
- 新的对象模型
- 新的跨平台/账户/记忆方案
- 值得后续继续讨论的问题

以下内容默认不写入：

- 重复确认
- 临时性执行细节
- 纯进度汇报
- 无保留价值的来回试探

## 3. 文档优先级

若多份文档内容出现冲突，按以下优先级解释：

1. `AGENTS.md`
2. `docs/tianshu-architecture-spec.md`
3. `docs/tianshu-implementation-tracker.md`
4. 参考文档

## 4. 实施约束

- 后续所有设计驱动实现，默认采用“讨论 + 文档 + 接口骨架 + 测试骨架”的方式推进。
- 若需要新建合同层、接口层、测试骨架，必须先在规范文档与实施跟踪文档中有对应记录。
- 未进入实施跟踪文档的工作，不应直接开始。
- 若设计文档与已落地代码不一致，应以当前代码与测试结果为准，并立即回写设计文档修正偏差。
- 新增的非测试代码应尽量补充中英文双语注释，优先覆盖公共类型、公共属性、构造器、命令/事件/投影契约以及复杂逻辑，尽量减少未来开源后的外部文档依赖；若注释量过大，至少保证类型级摘要与关键行为说明是双语的。
- 涉及 `src/Presentations/TianShu.VSSDK.VSExtension` 或其他 VisualStudio SDK / VSIX / 旧式 `.csproj` 工程时，禁止默认使用 `dotnet build` 作为构建入口；必须优先使用 Visual Studio 2026 (`18.x`) 的 `MSBuild.exe` 或 `devenv`。
- Windows / Visual Studio 构建链路需要 `vswhere.exe` 时，默认位置为 `C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe`；若 NativeAOT、VSIX 或脚本构建阶段提示找不到 `vswhere.exe`，先把该文件所在目录加入当前进程 `PATH`，再继续构建。
- 若仓库脚本需要构建 VSIX，必须统一走 `tools/Build-TianShuVsix.ps1`，或复用其中同等规则解析出的 VS2026 构建链路；不得在新脚本中再次直接写死 `dotnet build <VSIX项目>`。
- 每次完成仓库代码、文档、脚本或配置变更并提交/推送后，必须把最新版安装到用户级 TianShu 目录；默认执行 `powershell -NoProfile -ExecutionPolicy Bypass -File tools\Install-TianShuCli.ps1 -PreserveConfig -TianShuHome "$env:USERPROFILE\.tianshu"`，至少刷新 `tianshu.exe`、`TianShu.ConfigGui.exe` 与 `TianShu.AppHost.exe` 三个入口，并确保保留既有 `tianshu.toml`、`default_prompt.toml` 与 `prompt/` 内容。若本次只触及文档且不影响已安装可执行文件，应明确说明跳过用户级安装及原因。
