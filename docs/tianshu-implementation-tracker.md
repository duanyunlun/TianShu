# TianShu 实施跟踪

## 待做


## 正在做

### P31.11 · v0.9.1 公有仓库发布同步

- [ ] P31.11.5 同步当前 `master` 到公有仓库 `duanyunlun/TianShu`。
- [ ] P31.11.6 创建或更新公有仓库 `v0.9.1` GitHub Release，并上传 Windows x64 便携包与 manifest。
- [ ] P31.11.7 复核公有 Release 资产可见性、下载命名与 manifest 版本。

## 已完成

### P31.11 · v0.9.1 公有仓库发布同步

- [x] P31.11.1 同步公开文档版本口径：README、quickstart、release acceptance、release notes 与便携包设计示例统一指向 `v0.9.1`。
- [x] P31.11.2 本地生成 Windows x64 便携包：产物为 `artifacts/release/packages/tianshu-v0.9.1-win-x64.zip` 与对应 `release-manifest.json`。
- [x] P31.11.3 本地执行发布门禁：manifest、package smoke、release acceptance、public safety scan 与文档发布门禁必须通过。
- [x] P31.11.4 提交并推送开发仓库 `duanyunlun/TianShu-dev` 的当前 `master`。

### P31.13 · Kernel Runtime follow-up 能力透传补口

- [x] P31.13.1 清理 tracker 顶部空待做标题，避免历史路线图占位污染实施状态。
- [x] P31.13.2 为 `follow-up --kernel-runtime-loop --mode steer` 补齐 shell/MCP/memory 显式 opt-in 解析与治理传递。
- [x] P31.13.3 锁定 resume 当前为 checkpoint host-control/read-only 路径，不隐式继承执行能力。
- [x] P31.13.4 补 CLI / RuntimeComposition 聚焦测试并完成本地验证。

### P31.12 · v0.7 能力面接入 CLI Kernel Runtime 主线

- [x] P31.12.1 补齐 `send --kernel-runtime-loop` 的能力开关与治理传递：write/apply_patch、shell、MCP、memory 不再只停留在合同或 AppHost 侧。
- [x] P31.12.2 RuntimeComposition 注册对应真实 binding：workspace mutating tool、shell tool、MCP resource/tool、memory module 必须和 governance allow-list 对齐。
- [x] P31.12.3 补 CLI / RuntimeComposition 回归测试：证明未授权默认 fail-closed，授权后 provider tool surface 与 RuntimeStep binding 包含对应能力。
- [x] P31.12.4 运行 v0.7 capability gate 与相关最小测试，并根据结果回写 tracker。

### P31.10.R5 · follow-up CWD 不污染测试补口

- [x] 为普通 `follow-up` 自动响应测试补 CWD 默认运行产物不落盘断言，覆盖 `.tianshu/kernel-runtime` 与 `.tianshu-cli`。
- [x] 为 kernel-runtime `steer` follow-up 测试补同类 CWD 不污染断言，与 interrupt / resume 保持一致。

### P31.10.R · 便携式分发包复核补缺

- [x] P31.10.R1 补齐 `doctor` 平台/RID 匹配检查：从包内 manifest 或 VERSION 记录读取构建 RID，输出结构化诊断字段，并在 RID 与当前平台不匹配时 fail-closed 或 error issue。
- [x] P31.10.R2 补齐运行产物不可写的中英文结构化引导：fail-closed 诊断不得只有英文消息，JSON issue 与异常消息都应包含可面向中文用户的修复提示。
- [x] P31.10.R3 补齐实际命令路径测试：覆盖 `send` 执行后不污染 CWD、`follow-up` / `resume` 默认运行产物不落 CWD、`init --force` 便携包内重建，以及 package smoke 对 RID 诊断的断言。
- [x] P31.10.R4 复核公开默认 provider 模板：确认发布包默认 provider 模板继续保持公开默认端点，不把本地私有网关写入公开发布面。

### P31.10 · 便携式分发包与默认运行产物落盘收敛

- [x] P31.10.1 文档基线审核：确认 `docs/architecture/tianshu-portable-distribution-design.md`、`docs/tianshu-architecture-spec.md` 与 `docs/usage/quickstart.md` 对齐为“包根即 TianShuHome、默认运行产物不写 CWD、Project 层只保留读取、System 层便携隔离”。
  - [x] 审核并确认公开 Release TianShuHome 为便携包根。
  - [x] 审核并确认主配置为 `<TianShuHome>/tianshu.toml`,模块根为 `<TianShuHome>/modules/`,AppHost 为 `<TianShuHome>/runtime/apphost/`。
  - [x] 审核并确认 CWD 只用于 workspace 解析、Project 层配置读取和 workspace-key 生成,不得作为默认运行产物落盘根。
  - [x] 审核并确认显式 `--artifacts <path>` 是写入 CWD 或任意用户路径的唯一 send artifact 例外。
- [x] P31.10.2 便携包根解析合同：新增或改造 TianShuHome 解析,实现 `<pkg>/bin` -> `<pkg>` 的自动识别。
  - [x] 识别信号至少包含 `<candidate>/tianshu.toml` 与 `<candidate>/modules/`。
  - [x] 已识别便携包时不得被 `TIANSHU_HOME` 或用户目录覆盖。
  - [x] 保留显式 `--config-file`、`TIANSHU_HOME`、`~/.tianshu` 的非便携回退链路。
  - [x] 补单元测试:包根推导、CWD 不污染、`TIANSHU_HOME` 不覆盖便携包根、非便携回退。
- [x] P31.10.3 默认配置与模块路径接入：让 CLI / RuntimeComposition 默认配置路径在便携模式下指向 `<TianShuHome>/tianshu.toml`。
  - [x] 确认 `ResolveModulePathFromConfig(<TianShuHome>/tianshu.toml, ...)` 继续推导到 `<TianShuHome>/modules/...`。
  - [x] 确认 `init` 在便携模式下只重建包根内配置和模块模板,不默认写用户目录。
  - [x] 补测试:免 init 读取包内配置、`init --force` 包内重建、模块模板路径正确。
- [x] P31.10.4 System 层隔离：便携模式下不读取宿主机 `%PROGRAMDATA%\TianShu` 或 `/etc/tianshu` 配置。
  - [x] 在配置加载器中增加便携模式语义,跳过宿主机 System 层或将 system root 显式隔离到包内/空目录。
  - [x] 保留 `<cwd>/.tianshu/tianshu.toml` Project 层读取,但不得由此产生默认 CWD 写入。
  - [x] 补测试:宿主机 System 层存在时便携加载结果不含其内容;Project 层仍按 CWD 参与合并。
- [x] P31.10.5 默认运行产物落盘根迁移：把历史 CWD 默认写入迁移到 `<TianShuHome>/runtime/.../<workspace-key>/`。
  - [x] 设计并实现稳定 workspace-key 生成:基于 normalized CWD,避免泄露完整本机路径到目录名。
  - [x] 迁移 `KernelRuntimeTurnLoopBridge` 当前 `<cwd>/.tianshu/kernel-runtime/host-control` 默认根;其下 `active-runs/`、`cancellations/`、`checkpoints/`、`pending-steers/` 和 cancel signal 文件必须全部迁移到 `<TianShuHome>/runtime/kernel-runtime/<workspace-key>/...`。
  - [x] 迁移 checkpoint / pending steer 默认根,保持同一 normalized CWD 命中同一 workspace-key,不同 CWD 不碰撞。
  - [x] 迁移 `KernelRuntimeProductEvidenceWriter` turn evidence 默认根,避免继续写入 `<cwd>/.tianshu/kernel-runtime/<thread>/<turn>/...`。
  - [x] 迁移 `send` 默认 artifacts root,不再使用 `<cwd>/.tianshu-cli/runs`。
  - [x] 明确只读 TianShuHome 的运行时写入降级策略:默认应 fail-closed 并提示用户把便携包放到可写位置,不得静默回退到 CWD 或用户目录。
  - [x] 保留 `--artifacts <path>` 显式输出例外,并在结果/summary 中标明显式输出位置。
  - [x] 补测试:默认 send / follow-up / resume 不创建 CWD `.tianshu/` 或 `.tianshu-cli/`;同一 CWD 命中同一 workspace-key;不同 CWD 分桶隔离;只读 TianShuHome fail-closed。
- [x] P31.10.6 CLI AppHost 探测：让 CLI 从 `<pkg>/bin` 找到 `<pkg>/runtime/apphost/TianShu.AppHost.exe`。
  - [x] 在 `CliAppHostLaunchResolver` 中新增 BaseDirectory 父目录(包根)作为独立探测根;问题是当前缺少包根探测根,不是仅调整既有探测顺序。
  - [x] 探测顺序应为 workingDirectory -> BaseDirectory -> BaseDirectory 父目录(包根) -> 用户级 `~/.tianshu`;包根探测必须先于用户级回退。
  - [x] 补测试:便携包布局命中 AppHost;源码树/用户级安装路径不回归;缺少包根探测时的失败案例被覆盖。
- [x] P31.10.7 doctor / init 便携诊断：补齐便携模式下真实可验收的离线诊断。
  - [x] `doctor` 检查包根识别、`tianshu.toml`、`modules/`、`runtime/apphost/`。
  - [x] `doctor` 检查默认运行产物根可写性,失败时返回结构化 code 和中英文引导。
  - [x] `doctor` 增加平台/RID 匹配检查所需信号,例如 manifest 或 VERSION 记录构建 RID。
  - [x] 保持 `doctor` 默认离线;`doctor --probe` 才允许联网。
- [x] P31.10.8 发布脚本改造：把 `tools/Publish-TianShuCliRelease.ps1` 从 CLI 平铺包改为完整便携 TianShuHome 包。
  - [x] CLI publish 到 `<pkg>/bin/`。
  - [x] AppHost publish 到 `<pkg>/runtime/apphost/`。
  - [x] 内置 provider/tool/memory/policy/prompt/diagnostic/artifact 能力由随包程序集与内置 descriptor 暴露,第三方模块后续放入 `<pkg>/modules/`。
  - [x] 默认 `tianshu.toml` 与 `modules/model/**` 模板预置到包内。
  - [x] 不复制 `AGENTS.md`、测试证据、私有路径、API key 或本机 runtime state。
  - [x] 决定并落实 self-contained 真值;若承诺解压即用,CLI 与 AppHost 都必须 self-contained。
- [x] P31.10.9 release manifest / smoke / gate 改造：同步便携包结构和验收门禁。
  - [x] manifest 增加或更新 `layout`, `entryPath`, `configPath`, `modulesPath`, `appHostPath`, `selfContained`, `runtimeIdentifier`, checksum 与 size。
  - [x] package smoke 解压后清空 `TIANSHU_HOME`,从非包目录 CWD 运行 `<pkg>/bin/tianshu.exe`。
  - [x] package smoke 断言默认运行不会在 smoke workspace 创建 `.tianshu/` 或 `.tianshu-cli/`。
  - [x] release acceptance gate 锁定 `tianshu-<version>-<rid>` 便携包命名,不再使用旧 CLI 平铺包名。
  - [x] public release safety scan 覆盖公开发布面;package smoke 额外断言包内 `AGENTS.md`、测试证据、secret/private path 不进入 release 包。
- [x] P31.10.10 Windows 本地便携包验收：先只把 Windows 作为可用性硬验收平台。
  - [x] 生成 win-x64 便携包。
  - [x] 校验 manifest、checksum、包结构、`AGENTS.md` 排除和 secret/private path 排除。
  - [x] 从任意非包目录运行 `bin/tianshu.exe --help`。
  - [x] 未运行 `init` 直接执行离线 `doctor --json`,验证 fail-closed 与模块/AppHost/包根诊断。
  - [x] 配置测试凭据后运行最小 `send --json`,验证不会在工作区创建 `.tianshu/` 或 `.tianshu-cli/`;本地解压副本使用私有网关 base URL 与 `OPENAI_API_KEY` 复跑通过,`doctor --probe` 返回 200,`send --kernel-runtime-loop` 返回 `RuntimeCompleted`。
  - [x] 记录 Linux/macOS 当前只做构建/结构检查,未做人工运行验收。
- [x] P31.10.11 公开文档收口：便携包 Windows smoke 通过后再同步 README、release notes、release acceptance、release smoke 和 troubleshooting。
  - [x] README 不得提前声称未验收平台可用。
  - [x] quickstart 去掉过渡说明,转为正式便携包使用说明。
  - [x] release notes 明确 Windows 已验收、Linux/macOS 验收状态、self-contained 状态和已知限制。
  - [x] troubleshooting 增加包根不可写、AppHost 缺失、平台包不匹配、默认运行产物根不可写等条目。

### P31 · v1.0.0 稳定化与发布门禁

- [x] P31.8 发布自演化可行性报告：正式结论为部分可行；控制链路、可观测性和回滚边界有证据，真实长期收益和可靠自主进化仍未证明。
  - [x] 新增 `docs/audit/tianshu-self-evolution-feasibility-report.md`，给出明确结论、证据范围、已证明能力、未证明能力、失败样例、后续准入条件和正式边界。
  - [x] 更新自演化设计文档、总体架构规范、README 与 release notes，使 P31.8 正式报告成为当前结论入口，并清理“等待结论”的旧口径。
  - [x] 补文档回归测试，锁定正式报告存在、结论口径、证据边界和不得宣称可靠自主进化。
  - [x] 运行 `SelfEvolutionDocs_ShouldDeclareExploratoryBoundaryAndStableKernelCoreGate` 文档回归测试通过。
  - [x] 运行 `TianShu.Contracts.Kernel.Tests`、`TianShu.Kernel.Abstractions.Tests`、`TianShu.Kernel.Tests`、`TianShu.Kernel.Adaptive.Tests`、`TianShu.Kernel.Strategies.Tests` 通过。
  - [x] 运行 `tools/Test-TianShuV10DocumentationReleaseGate.ps1 -Configuration Release` 通过。
  - [x] 运行 `tools/Test-TianShuPublicReleaseSafetyScan.ps1 -Configuration Release -SkipRegressionTests` 通过。
- [x] P31.9 锁定 v1.0 发布门禁：当前只验收 Windows 本地发布物可正常生成、校验和运行；因 GitHub Actions 账号额度耗尽，远端 Actions 与 Linux/macOS 暂不作为本轮完成条件。
  - [x] 重新基于当前 `master` 生成 `v0.5.0` win-x64 release package。
  - [x] 校验 release manifest、Windows archive 存在性、文件大小与 SHA-256 checksum。
  - [x] 解压 Windows archive 并验证 `tianshu.exe --help`、`init --json`、无凭据 `doctor --json` fail-closed、必需文件存在和 `AGENTS.md` 排除。
  - [x] 运行公开发布安全扫描，确认文档、脚本、release 面未包含 secret、私有路径或测试产物。
  - [x] 运行本地 release acceptance gate，确认发布脚本、manifest 校验、package smoke、release notes 与 CI wiring 的静态契约仍一致。
  - [x] 运行生产级文档发布门禁，确认公开 README、quickstart、module guide、architecture spec、troubleshooting、security model 与 release notes 入口仍一致。
  - [x] 记录当前边界：GitHub Actions 因账号 Actions included minutes 用尽无法启动；Linux/macOS 发布物暂未验收。
- [x] P31.7 形成模块生态示例：至少一个第三方 Provider、一个第三方 Tool、一个 Memory 示例模块。
  - [x] 新增 `samples/modules/provider/TianShu.Samples.Provider.Echo` 与测试项目，演示第三方 Provider descriptor、manifest、streaming text、usage projection 和 access validation。
  - [x] 新增 `samples/modules/tool/TianShu.Samples.Tool.WordCount` 与测试项目，演示只读 Tool schema、governance envelope、handler invocation 和 tool result projection。
  - [x] 新增 `samples/modules/memory/TianShu.Samples.Memory.InMemory` 与测试项目，演示 Memory retrieve、form、supersede、compress-reserved manifest、context policy、unsupported mutation 降级。
  - [x] 新增 `tools/Build-TianShuModuleSamples.ps1`，串行执行三套 sample 测试并关闭 shared compilation。
  - [x] 更新 `tools/Test-TianShuV06ModuleReleaseGate.ps1`，v0.6 module release gate 默认同时验证模板和 sample。
  - [x] 更新 module plane、module guide、release smoke baseline 与 README，移除 sample “未来”表述并指向当前 sample 项目。
  - [x] 运行 `tools/Build-TianShuModuleSamples.ps1 -Configuration Release` 通过。
  - [x] 运行 `tools/Test-TianShuV06ModuleReleaseGate.ps1 -Configuration Release` 通过。
  - [x] 运行 `tools/Test-TianShuV10DocumentationReleaseGate.ps1 -Configuration Release` 通过。
  - [x] 运行 `tools/Test-TianShuPublicReleaseSafetyScan.ps1 -Configuration Release -SkipRegressionTests` 通过。
  - [x] 运行 `git diff --check` 通过，仅提示既有 LF/CRLF 工作区换行转换 warning。
- [x] P31.9.1 Windows 本地发布物验收：因当前账号 GitHub Actions 额度耗尽，先只验证 win-x64 release 包生成、manifest/checksum、解压、`--help`、`init`、离线 `doctor` 与 `AGENTS.md` 排除；Linux/macOS 远端 CI/package smoke 暂不作为本轮完成条件。
  - [x] 运行 `tools/Publish-TianShuCliRelease.ps1 -Configuration Release -Version v0.5.0 -RuntimeIdentifiers win-x64`，生成 `artifacts/release/packages/tianshu-v0.5.0-win-x64.zip` 与 `release-manifest.json`。
  - [x] 运行 `tools/Test-TianShuReleaseManifest.ps1 -PackagesRoot artifacts/release/packages -Version v0.5.0 -RuntimeIdentifiers win-x64`，验证 Windows archive 存在、大小与 SHA-256 checksum 匹配。
  - [x] 运行 `tools/Test-TianShuCliReleasePackage.ps1 -PackagesRoot artifacts/release/packages -RuntimeIdentifier win-x64`，验证包可解压、`README.md`/`LICENSE`/`VERSION.txt` 存在、`AGENTS.md` 被排除、`tianshu.exe --help`、`init --json`、无凭据 `doctor --json` fail-closed 与模块投影。
  - [x] 运行 `tools/Test-TianShuPublicReleaseSafetyScan.ps1 -Configuration Release -SkipRegressionTests` 通过。
  - [x] 运行 `git diff --check` 通过，仅提示 tracker LF/CRLF 工作区换行转换 warning。
  - [x] 当前限制：`TianShu-dev` 远端 GitHub Actions 因账号 Actions included minutes 用尽无法启动；Linux/macOS 远端 CI/package smoke 需等额度恢复或配置预算后再验收。
- [x] P31.6 完善 release 验收：tag 发布、资产命名、manifest、checksum、Windows smoke、升级/卸载说明。
  - [x] 新增 `docs/publishing/tianshu-release-acceptance.md`，定义 tag 发布、资产命名、manifest/checksum、Windows smoke、升级与卸载验收基线。
  - [x] 新增 `tools/Test-TianShuReleaseAcceptanceGate.ps1`，静态检查 release chain、发布脚本、manifest 校验、package smoke、release notes、release smoke 和 CI wiring。
  - [x] 将 release acceptance gate 接入 `.github/workflows/ci-release.yml` 的 Windows P0 build-test 链路。
  - [x] 更新 release smoke baseline 和 release notes，明确 release acceptance baseline 的职责与发布前检查入口。
  - [x] 补文档回归测试 `P31_6_ReleaseAcceptanceGate_ShouldBeDocumentedAndWiredIntoCi`，锁定 gate、CI、发布文档和脚本契约。
  - [x] 运行 `tools/Test-TianShuReleaseAcceptanceGate.ps1 -Configuration Release` 通过。
  - [x] 运行 `tools/Test-TianShuPublicReleaseSafetyScan.ps1 -Configuration Release -SkipRegressionTests` 通过。
  - [x] 运行 `git diff --check` 通过，仅提示既有 LF/CRLF 工作区换行转换 warning。
- [x] P31.5 完善跨平台 CI 矩阵：Windows 必测；Linux/macOS 至少覆盖 restore/build/init/doctor/package smoke。
  - [x] 新增 `tools/Test-TianShuCrossPlatformCliSourceSmoke.ps1`，跨平台执行 CLI restore、build、`init --json`、离线 `doctor --json`。
  - [x] source smoke 清空 provider 凭据环境变量，验证 `doctor` 在无凭据时 fail-closed 并报告 `provider_api_key_missing` 与模块 discovery/loading 投影。
  - [x] GitHub Actions 新增 `cross-platform-source-smoke` 矩阵，覆盖 `windows-latest/win-x64`、`ubuntu-latest/linux-x64`、`macos-26/osx-arm64`。
  - [x] `package-cli` 依赖 Windows P0 gate 与跨平台 source smoke，现有 `release-smoke` 继续覆盖三平台 package smoke。
  - [x] 更新 release smoke baseline，明确 source restore/build/init/doctor 与 package smoke 的跨平台责任。
  - [x] 补文档回归测试 `P31_5_CrossPlatformCiMatrix_ShouldCoverSourceAndPackageSmoke`，锁定 CI 矩阵、脚本、package smoke 和发布文档。
  - [x] 运行 `tools/Test-TianShuCrossPlatformCliSourceSmoke.ps1 -Configuration Release` 通过当前 Windows source smoke。
  - [x] 运行 `P31_5_CrossPlatformCiMatrix` 回归测试与公开发布安全扫描通过。
- [x] P31.4 完善安全发布扫描：secret scan、私有路径 scan、runtime state scan、测试产物 scan、README/文档死链 scan。
  - [x] 新增 `tools/Test-TianShuPublicReleaseSafetyScan.ps1`，扫描发布面文档/CI/脚本中的真实 secret 字面量、私有本地路径和 codex clipboard 路径。
  - [x] 扫描 git 跟踪文件，阻止 runtime state、`Test/`、`artifacts/`、`docs/audit/evidence/`、agent local state、debug request body 等测试/运行产物入库。
  - [x] 校验 `.gitignore` 必须覆盖 `Test/`、`artifacts/`、`.codex/`、`.claude/`、`.serena/` 和 `request-body*.json`。
  - [x] 扫描 README/docs 相对 Markdown 链接，防止公开文档文件级死链。
  - [x] 静态校验 release package smoke 仍禁止 `AGENTS.md` 进入发布包。
  - [x] 将公开发布安全扫描接入 `.github/workflows/ci-release.yml` 与 release smoke baseline。
  - [x] 补文档回归测试 `P31_4_PublicReleaseSafetyScan_ShouldBeDocumentedAndWiredIntoCi`，锁定扫描脚本、CI wiring、release smoke wiring 和忽略规则。
  - [x] 运行 `tools/Test-TianShuPublicReleaseSafetyScan.ps1 -Configuration Release` 通过。
- [x] P31.3 建立生产级文档门禁：README、quickstart、module guide、architecture spec、troubleshooting、security model、release notes。
  - [x] 新增公开 quickstart，覆盖 Release 包安装、源码运行、初始化配置、provider 凭据、自检、首条消息和清理重建。
  - [x] 新增 troubleshooting，覆盖 `provider_api_key_missing`、`doctor --probe`、provider 4xx/5xx、模块加载、写工具、Sub-Agent 和 Release 包问题。
  - [x] 新增 security model，明确默认能力边界、secret 处理、模块信任、RuntimeStep 审批、Provider/网络和 Remote Module 安全边界。
  - [x] 新增 release notes，记录 v0.5.0 用户可见能力、验证状态、已知限制和后续版本维护要求。
  - [x] 更新 README，将 quickstart、module guide、architecture spec、troubleshooting、security model、release notes 作为公开导航入口。
  - [x] 新增 `tools/Test-TianShuV10DocumentationReleaseGate.ps1`，验证生产级文档集合、README 链接、CI wiring、release smoke wiring、关键内容和基础公开文档卫生。
  - [x] 将 v1.0 production documentation gate 接入 `.github/workflows/ci-release.yml` 和 release smoke baseline。
  - [x] 补文档回归测试 `P31_3_ProductionDocumentationGate_ShouldBeDocumentedAndWiredIntoCi`，锁定文档入口和门禁接线。
  - [x] 运行 `tools/Test-TianShuV10DocumentationReleaseGate.ps1 -Configuration Release` 通过。
- [x] P31.2 建立版本兼容测试：旧配置、旧 module manifest、旧 StageGraph fixture、旧 release package 的加载与诊断。
  - [x] 补旧配置兼容测试，覆盖旧 `tianshu.toml` 字段仍可加载、legacy alias 产生诊断、旧 `apiKey` 等敏感字段会脱敏。
  - [x] 补 module manifest 版本兼容测试，覆盖旧 manifest 缺少可选 SDK / capability schema 版本可继续，显式非法或未知大版本 fail-close。
  - [x] 补 StageGraph fixture 兼容测试，覆盖同主版本增量字段可映射验证、未知大版本在映射前拒绝。
  - [x] 补旧 release package / manifest 验证测试，覆盖 schemaVersion 1 可验收、未知 schemaVersion 拒绝，并为 manifest 校验脚本补 SHA256 fallback。
  - [x] 更新核心 API 稳定承诺文档，记录 Module SDK manifest 版本字段、旧可选版本兼容边界和失败诊断码。
  - [x] 复跑 P31.2 聚焦测试：AppHost.Configuration、Configuration、Contracts.Modules、Kernel、Execution.Integration 相关兼容测试全部通过。
- [x] P31.1 梳理核心 API 稳定承诺：Contracts、Module SDK、Host Gateway、Control Plane、RuntimeStep、StageGraph 的兼容策略。
  - [x] 新增 `docs/architecture/tianshu-core-api-stability-design.md`，定义核心 API 稳定等级、总体兼容策略、稳定边界和非稳定边界。
  - [x] 明确 Contracts、Module SDK、Host Gateway、Control Plane、RuntimeStep / ExecutionPlan、StageGraph 的 v1.0 兼容责任。
  - [x] 明确 raw provider payload、secret、私有路径、产品宿主私有 DTO、RuntimeComposition / Execution Runtime bridge 实现、测试 helper 和 runtime state 不属于稳定 API。
  - [x] 将 P31.2 的旧配置、旧 module manifest、旧 StageGraph fixture、旧 release package 兼容测试基线写入稳定承诺文档。
  - [x] 更新总架构、Contracts 架构和 Module Plane 设计文档，统一指向核心 API 稳定承诺。
  - [x] 补文档回归测试，锁定稳定承诺文档存在性、六类稳定面、兼容策略、非稳定边界和 P31.2 基线。

### P30 · v0.10.0 自演化基础设施（探索）

- [x] P30.11 产出阶段性可行性报告草案：当前能度量什么、不能度量什么、失败样例、下一步实验边界。
  - [x] 新增 `docs/audit/tianshu-self-evolution-feasibility-draft.md`，明确 P30 阶段只证明受控实验基础设施闭环，不证明可靠自主进化收益。
  - [x] 报告草案列出当前能度量的候选生成、候选验证、plan-only trial、evaluator、cross-review、objective anchor、策略级聚合和 lifecycle audit。
  - [x] 报告草案列出当前不能度量的真实长期收益、真实 provider 评审质量、真实 cost、自动 promotion 正确性、高风险外部副作用安全性和模型自主改写稳定内核。
  - [x] 报告草案列出失败样例和 P31.8 正式报告需要补齐的 benchmark、runtime evidence、usage/cost、交叉评审、客观锚点、canary/rollback 与 human gate 证据。
  - [x] 更新自演化设计文档索引，明确 P30.11 草案不是 P31.8 正式结论。
  - [x] 补文档回归测试，锁定草案存在性、关键章节和非最终结论口径。
- [x] P30.10 补齐自演化测试：非法候选拒绝、评审分歧、客观锚点矛盾、promotion gate、rollback。
  - [x] 补 `AdaptiveCandidateValidationService` 测试，直接覆盖非法候选因无界预算与治理副作用上限越界被拒绝，并校验拒绝分类。
  - [x] 补 `StrategyRegistry` 生命周期测试，验证已 promoted strategy rollback 后从 promoted 查询和 candidate 列表中移除，并保留 candidate -> trial -> promoted -> rolled_back 审计链。
  - [x] 补策略级聚合测试，验证 promotion-ready report 不会自动修改 registry，仍必须通过 registry promotion gate 且需要 human approval。
  - [x] 补模型评审分歧聚合测试，验证 `ModelJudgeDisagreement` 会阻断 promotion-ready 并要求 human gate。
  - [x] 复跑 Contracts Kernel、Kernel Abstractions、Kernel Strategies、Kernel、Kernel Adaptive 与自演化/架构文档回归过滤测试。
- [x] P30.9 实现 strategy registry 生命周期：candidate、trial、promoted、rolled_back、deprecated 与可审计变更记录。
  - [x] 新增正式 `StrategyLifecycleState.Candidate`，将正式主链收敛为 candidate -> trial -> promoted -> deprecated / rolled_back；`Draft` / `Validated` 仅保留为兼容边界。
  - [x] 新增 `StrategyLifecycleAuditRecord` 公共契约，记录 strategy id、前后状态、evidence refs、metric refs、human approval、reason ref 与 occurred at。
  - [x] 扩展 `IStrategyRegistry`，新增 `SaveCandidateAsync` 和 `ListAuditRecordsAsync`。
  - [x] 更新默认 `StrategyRegistry`，candidate 注册、trial、promotion、deprecation 和 rollback 均写入 lifecycle audit record。
  - [x] 保持 promotion gate：晋升必须有 trace / evaluation metric refs 和必要 human approval；非法 transition fail-closed。
  - [x] 补 Contracts / Strategies / Kernel validator 测试，覆盖 candidate 默认状态、audit record、候选注册、deprecation、rollback、非法 transition 和 promotion gate。
  - [x] 更新总架构、自演化设计和 Kernel core loop 设计文档，移除 Draft -> Validated 作为正式主链的旧表述。
- [x] P30.8 实现统计聚合：从单次结果评审上升到策略级比较，避免单轮模型偏好直接驱动 promotion。
  - [x] 新增 `KernelStrategyEvaluationSample`、`KernelStrategyMetricAggregate`、`KernelStrategyComparison`、`KernelStrategyEvaluationAggregationRequest`、`KernelStrategyEvaluationAggregationReport` 公共契约。
  - [x] 新增 `IKernelStrategyEvaluationAggregationService` 抽象，纳入 Kernel Abstractions 正式边界。
  - [x] 新增默认 `KernelStrategyEvaluationAggregationService`，只聚合已生成的 evaluation / cross-review / objective-anchor calibration 证据，不调用 provider，不执行 Runtime，不执行 registry transition。
  - [x] 聚合报告输出 sample count、均值、范围、置信度、分歧数量、客观锚点冲突、缺失证据、model-judge-only 状态和 promotion readiness。
  - [x] 样本不足、只有模型裁判、存在 human-gate 分歧或客观锚点冲突时不得输出 promotion-ready。
  - [x] 补 Contracts / Abstractions / Strategies 测试，覆盖聚合契约、接口暴露、多样本 ready、单样本不足、锚点冲突阻断和纯模型裁判不可晋升。
  - [x] 更新总架构、自演化设计和 Kernel core loop 设计文档，明确策略级统计只是 promotion evidence，不得绕过 promotion gate。
- [x] P30.7 实现客观锚点校准：编译成功、测试通过、标准答案、人工标注结果用于校准模型评审可信度。
  - [x] 新增 `KernelObjectiveAnchorKind`、`KernelObjectiveAnchorObservation`、`KernelObjectiveAnchorCalibrationRequest`、`KernelObjectiveAnchorCalibrationReport` 公共契约。
  - [x] 新增 `IKernelObjectiveAnchorCalibrationService` 抽象，纳入 Kernel Abstractions 正式边界。
  - [x] 新增默认 `KernelObjectiveAnchorCalibrationService`，只消费已采集的 build/test/golden answer/human label 锚点，不直接执行 build/test 或外部命令。
  - [x] 将客观锚点投影为 `ObjectiveAnchor` / `ObjectiveAnchor` 结构化 observation，并按 `sourceMetricId` 校准模型裁判 confidence。
  - [x] 客观锚点与模型裁判分数差异达到阈值时生成 `ObjectiveAnchorConflict`，降低 calibrated confidence，并设置 `requiresHumanGate=true`。
  - [x] 更新总架构、自演化设计和 Kernel core loop 设计文档，明确锚点校准只是 evaluation evidence，不得直接 promotion。
  - [x] 补 Contracts / Abstractions / Strategies 测试，覆盖四类锚点、非法待校准对象拒绝、锚点冲突和无冲突校准。
- [x] P30.6 实现异质交叉评审实验：A 执行、B/C 评审，输出结构化评分、理由、分歧与不确定性。
  - [x] 新增 `KernelCrossReviewReviewerSpec`、`KernelCrossReviewMetricScore`、`KernelCrossReviewSubmission`、`KernelCrossReviewExperimentRequest`、`KernelCrossReviewExperimentReport` 等公共契约。
  - [x] 新增 `IKernelCrossReviewExperimentService` 抽象，纳入 Kernel Abstractions 正式边界。
  - [x] 新增默认 `KernelCrossReviewExperimentService`，聚合已提交的 B/C 结构化评审结果，不直接调用 provider，不执行 Runtime，不晋升 strategy。
  - [x] 评审评分投影为 `ModelJudge` / `ModelJudge` 结构化 observation，保留 reviewer、model route、provider、model、reason、source metric id 和 uncertainty。
  - [x] 同一指标评分差异达到阈值时生成 `ModelJudgeDisagreement`，并设置 `requiresHumanGate=true`。
  - [x] 更新总架构、自演化设计和 Kernel core loop 设计文档，明确交叉评审只是 evaluation evidence，不得直接 promotion。
  - [x] 补 Contracts / Abstractions / Strategies 测试，覆盖异质评审者要求、评分/不确定性契约、未知评审者拒绝和分歧输出。
- [x] P30.5 设计 Evaluator 度量基础设施：定义可观测指标、客观锚点、模型裁判信号、置信度与分歧度。
  - [x] 新增 `KernelEvaluationEvidenceSet`、`KernelEvaluationMetricObservation`、`KernelEvaluationDisagreement` 与对应枚举，正式区分 observable、objective anchor、model judge、confidence、disagreement 和 estimated signal。
  - [x] 扩展 `KernelEvaluationResult`，兼容旧 `metricScores`，同时携带 evidence、observations、disagreements、overall confidence 和 disagreement score。
  - [x] 默认 `KernelEvaluator` 从 run result / trace 投影 `success`、`replay_compatible`、`policy_violation_attempt` 三类结构化基础指标。
  - [x] 明确 P30.5 边界：provider usage、真实 cost 和人工反馈扩展仍归属后续阶段；策略级统计已由 P30.8 接入；估算值和模型裁判不得直接驱动 promotion。
  - [x] 更新总架构、自演化设计和 Kernel core loop 设计文档，保持 evaluator 度量边界与当前代码一致。
  - [x] 补 Contracts / Strategies 测试，覆盖估算 usage、客观锚点、模型裁判、置信度、分歧和默认 evaluator 结构化观测。
- [x] P30.4 实现 trial/shadow run 机制：候选可试运行、记录差异、不得直接提升为默认策略。
  - [x] 新增 `IAdaptiveCandidateTrialService` 与 trial/shadow 报告模型，覆盖 `ShadowRun` 和 `BoundedPlanTrial`。
  - [x] 新增默认 trial 服务，已验证候选可物化为候选 `ExecutionPlan`，并记录相对基线的 step、budget、side effect 和 step kind 差异。
  - [x] `BoundedPlanTrial` 只重新验证候选 RuntimeStep，不调用 Execution Runtime，不产生真实外部副作用。
  - [x] `StableKernelCore` 在默认 `ExecutionPlan` 获批后运行 plan-only trial，写入 `ProposalReviewed` trace，并把报告投影到 `KernelRunResult.Metadata`。
  - [x] 保持 P30.4 边界：trial 报告显式 `executedRuntime=false`、`promotedStrategy=false`，不得直接提升为默认策略。
  - [x] 补 Abstractions / Kernel 测试，覆盖接口暴露、trial 报告、差异记录、未执行 Runtime、未 promotion、默认 graph 不被替换。
- [x] P30.3 实现候选验证闭环：schema validation、deterministic kernel checks、governance checks、budget checks、capability checks。
  - [x] 新增 `IAdaptiveCandidateValidationService` 与候选验证报告模型，正式承载多候选审查结果。
  - [x] 新增默认候选验证服务，复用 `IKernelValidator` 并按 schema、deterministic kernel、governance、budget、capability 分类记录检查结果。
  - [x] `StableKernelCore` 在 adaptive 模式下生成候选后立即执行候选验证，写入 `ProposalReviewed` trace，并把报告投影到 `KernelRunResult.Metadata`。
  - [x] 保持 P30.3 边界：候选只验证、不执行、不 trial、不晋升、不替换默认执行图。
  - [x] 补 Abstractions / Kernel 测试，覆盖接口暴露、候选接受/拒绝分类、拒绝候选不影响默认 graph 执行。
- [x] P30.2 实现 Adaptive orchestrator 候选生成接口：propose 多个 StageGraph 变体，输出结构化候选而非自然语言 plan。
  - [x] 新增 `IAdaptiveStageGraphCandidateGenerator`，将 StageGraph 候选生成定义为正式抽象边界。
  - [x] 新增默认候选生成器，默认产出 `direct`、`context_guarded`、`recovery_checked` 三类结构化 `StageGraphProposal`。
  - [x] 让 `AdaptiveOrchestrator.ProposeAsync` 返回多候选 `KernelProposalSet`，并继续禁止 Adaptive 层返回 `RuntimeStep`。
  - [x] 更新自演化与 Kernel 设计文档，记录候选生成接口、默认候选类型和仍需 Stable Kernel Core / P30.3 验证门禁批准。
  - [x] 补 Abstractions / Adaptive 测试，验证多候选、唯一 graph id、结构化 StageGraph、只读 policy、recovery edge 和无 RuntimeStep 边界。
- [x] P30.1 更新自演化设计文档：明确探索性质、不可承诺成功、稳定内核不可被模型绕过。
  - [x] 新增 `docs/architecture/tianshu-self-evolution-design.md`，将自演化定义为探索性基础设施，不承诺成功。
  - [x] 对齐总架构和六层索引，明确 Stable Kernel Core、Kernel validator、governance、RuntimeStep approval、module trust、trace/audit 与 rollback 不可被模型绕过。
  - [x] 对齐 Kernel 专项文档中的项目现状，移除已建立项目的“未来新建”旧状态描述。
  - [x] 补文档回归测试，锁定自演化探索边界、KernelTool 输出边界和 promotion gate 证据要求。

### P29 · v0.9.0 多 Agent 成熟

- [x] P29.10 建立 v0.9 发布门禁：并行多 Agent 默认受限、可关闭、可审计，触发观测报告公开记录。
  - [x] P29.10.1 核对当前代码默认行为：未显式 `--enable-subagents` 时不得向 provider 暴露 `spawn_agent`，显式启用时必须同时满足授权边界。
  - [x] P29.10.2 新增 v0.9 release gate 脚本，串行执行默认受限、可关闭、确定性多 Agent 机制和观测报告 schema 检查。
  - [x] P29.10.3 更新多 Agent 设计文档与公开路线图，记录 v0.9 门禁口径、观测报告公开记录和非承诺式自主触发结论。
  - [x] P29.10.4 补回归测试，锁定 gate 脚本、README 和最终验收文档中的 v0.9 多 Agent 发布门禁口径。
  - [x] P29.10.5 运行 gate/tests，提交、推送并按规则刷新用户级安装。
- [x] P29.9 新增多 Agent 最终验收案例：并行 fanout、子树治理、预算切分、结果 fan-in、失败隔离与整树复盘必须在同一案例中可观察。
  - [x] P29.9.1 在最终验收文档中定义确定性多 Agent 机制案例，明确它与 live 自主触发观察矩阵分层。
  - [x] P29.9.2 让最终验收脚本硬性检查同一证据链中的 fanout、子树治理、预算切分、fan-in、失败隔离和整树复盘字段。
  - [x] P29.9.3 补回归测试，防止最终验收口径退回“只看 live 自主触发”或“只看旧 agent job UI”。
  - [x] P29.9.4 运行脚本解析与相关测试，提交、推送并按规则刷新用户级安装。
- [x] P29.8 补齐多 Agent 测试：并发限制、预算耗尽、子任务失败、取消传播、结果归并、fork bomb 防护。
  - [x] P29.8.1 覆盖并发限制：`MaxConcurrentAgents > 0` 时 bounded fanout 不超过上限，并在终止后回收活跃计数。
  - [x] P29.8.2 覆盖预算耗尽：整树预算或子任务预算耗尽时，未开始 item 以 `blocked` / `subagent.*budget*` 结构化回流。
  - [x] P29.8.3 覆盖子任务失败：单个 child failed 不污染父状态机，父收到 `SubAgentRunResult.Failure` 并进入 fan-in。
  - [x] P29.8.4 覆盖取消传播：父 cancellation / item timeout 进入子执行 token，子结果以 `cancelled` 或 `timeout` 结构化回流。
  - [x] P29.8.5 覆盖结果归并：fan-in 保留全部 child 结果、冲突、失败、artifact / diagnostics 引用。
  - [x] P29.8.6 覆盖 fork bomb 防护：深度、扇出、树节点、子任务数和并发闸门超限均 fail-closed 且不启动越界子 run。
- [x] P29.7 建立 sub-agent 自主触发观测矩阵：补写或复核 v0.5 串行基线观测结论（27 次 / 0 自主触发），再在 v0.9 并行 fanout 场景下扩展到多协议、多模型、多任务、多轮，记录触发率、有效率、误触发。
- [x] P29.6 补齐多 Agent artifact/diagnostics：每个子树独立 trace、最终汇总 report、失败原因投影。
- [x] P29.5 补齐子树治理：子 agent 不可绕过父级 policy、tool budget、workspace sandbox、human gate 与 trace。
- [x] P29.4 实现结果 fan-in：子任务结果归并、冲突标记、证据引用、模型裁判预留但不作为唯一判断标准。
- [x] P29.3 实现并行 fanout 调度：任务拆分、agent job 创建、取消传播、超时、资源占用记录。
- [x] P29.2 激活并发闸门：`maxConcurrentAgents`、全局预算、每 agent 预算、最大深度、最大子任务数。
- [x] P29.1 更新多 Agent 设计文档：串行、并行 fanout、fan-in、预算拆分、子树治理、失败隔离、整树复盘。

### P28 · v0.8.0 远程连续性接口与 Remote Module

- [x] P28.14 建立 v0.8 发布门禁：Remote Module 可选关闭、默认不开放公网、所有远程副作用可审计。
- [x] P28.10 补齐 Remote Module 测试：未配对拒绝、token 过期、只读订阅、审批通过/拒绝、断线重连、重复命令。
- [x] P28.11 新增远程连续性验收案例：模拟移动端只读跟随、远程审批、远程中断/恢复、远程提交 follow-up。
- [x] P28.12 完成多宿主体验收敛：VSIX、Config GUI、AppHost 作为 Host Gateway 消费端接入远程连续性/状态投影，不与多 Agent 协作混在同一验收主题。
- [x] P28.13 更新公开文档：说明移动端/Web/云中继只是消费形态，不要求远端设备本地执行工作负载。
- [x] P28.9 实现断线恢复：event cursor resume、thread snapshot refresh、重复命令幂等、过期审批处理。
- [x] P28.8 补齐远端安全边界：远端不暴露本地绝对路径 secret、workspace 文件内容按权限投影、高风险动作继续 human gate。
- [x] P28.7 实现基础 Remote Module 示例：本地 HTTP/SSE 或 WebSocket 收发、只读状态查看、提交消息、审批、interrupt/resume。
- [x] P28.6 设计 Remote Module 合同：transport abstraction、pairing、short-lived token、session revocation、device identity、scope。
- [x] P28.5 规定远程命令入口：所有远程写入必须进入 Host Gateway / Control Plane，不允许直接写 runtime state 或 workspace。
- [x] P28.4 定义远程控制命令：submit message、steer、interrupt、resume、approval decision、cancel pending operation。
- [x] P28.3 定义事件流订阅接口：event cursor、SSE/WebSocket 抽象、重连、补发、最终一致性与事件保留策略。
- [x] P28.2 定义线程状态投影接口：thread snapshot、run state、current stage、tool/sub-agent 状态、pending approval、artifact、diagnostics。
- [x] P28.1 新增远程连续性设计文档：明确它是状态/控制接口 + 可替换 Remote Module，不是内核内置移动端。

### P27 · v0.7.0 受治理能力面扩展

- [x] P27.15 建立 v0.7 发布门禁：能力面默认安全、写/shell/MCP 均有 human gate 覆盖、关键路径 live smoke 可复现。
- [x] P27.14 开放 Memory 能力面：retrieve/form/supersede 进入 StageGraph 与 RuntimeStep，接入 context policy。
- [x] P27.13 补齐上下文测试：token 阈值、压缩可逆性、supersede 优先级、不可压缩内容保护、provider usage 缺失时估算降级。
- [x] P27.12 实现上下文策略执行链：context slice、compaction candidate、supersede decision、checkpoint 与 trace。
- [x] P27.11 设计结构化上下文管理：token 触发、分层降级、supersede 取舍、可逆压缩、审计记录、provider usage 输入。
- [x] P27.10 补齐 MCP 测试：只读 resource、远端 tool gate、server 不可达、schema 不合法、结果投影。
- [x] P27.9 实现 MCP 基础接入：resource 查询、tool schema 投影、tool invocation 进入统一 ToolUse/governance/runtime step。
- [x] P27.8 设计 MCP 接入合同：resource 只读访问、tool 远端副作用治理、server manifest、权限声明、连接失败降级。
- [x] P27.7 补齐 shell 测试：危险命令 gate、cwd 越界、env 脱敏、超时、非零退出码、输出截断。
- [x] P27.6 实现 shell runtime step：命令计划、审批、执行、流式输出、截断、诊断投影与安全失败。
- [x] P27.5 设计受治理 shell 合同：命令审批、cwd 限制、环境变量脱敏、输出截断、超时、退出码、可审计 transcript。
- [x] P27.4 补齐 write/apply_patch 测试：只读模式拒绝、越界路径拒绝、审批拒绝、冲突失败、成功写入、回滚记录。
- [x] P27.3 实现 write/apply_patch runtime step：所有写入必须经过 workspace resolver、governance envelope、human gate 与 artifact/trace 投影。
- [x] P27.2 设计受治理 write/apply_patch 合同：变更计划、diff 预览、审批、路径沙箱、冲突检测、回滚/补偿记录。
- [x] P27.1 更新能力面设计文档：write/apply_patch、shell、MCP、结构化上下文、Memory 能力的治理边界与失败关闭原则。

### P25 · v0.5.x 发布物真实性补验

- [x] P25.1 补齐发布物 smoke 设计：明确 Windows 本地 smoke、Linux/macOS CI smoke、源码归档一致性、Release asset manifest、checksum 的验收口径。
- [x] P25.2 建立跨平台 CI smoke：Windows/Linux/macOS 至少覆盖 release 包解压、入口可执行、`init`、`doctor`、配置模板生成与退出码。
- [x] P25.3 验证发布包运行时完整性：模块动态加载、配置 schema、内置资源、provider/module manifest、依赖文件均随包可用。
- [x] P25.4 验证公开 tag 源码归档一致性：`v0.5.0` tag 已对齐公开 master；公开 tag workflow 通过；GitHub source archive 不包含 `AGENTS.md`，且包含发布门禁脚本、workflow 与说明文档。
- [x] P25.5 补齐 release-manifest 校验：资产名、版本号、平台、checksum、文件大小、生成时间与 GitHub Release 附件一致。
- [x] P25.6 建立 v0.5.x 补验门禁：未完成跨平台 smoke 前，不再把“跨平台已验证”作为已完成能力表述。

### P26 · v0.6.0 模块化开放

- [x] P26.1 明确 Module SDK 的正式边界：Provider、Tool、Memory、Diagnostics、Projection、Remote 预留扩展点分别属于哪些合同项目与运行时装配边界。
- [x] P26.2 更新模块化设计文档：补齐第三方模块生命周期、信任边界、版本兼容、健康检查、卸载/禁用、失败降级与治理原则。
- [x] P26.3 定义公开 Module SDK 包结构：`TianShu.Contracts.Modules`、公共 abstractions、模板项目、示例模块与测试夹具的项目归属。
- [x] P26.4 收敛模块描述符契约：module id、kind、capabilities、side effect level、required config、runtime dependencies、health probe、minimum TianShu version。
- [x] P26.5 补齐模块发现契约：本地目录发现、manifest 解析、内置模块与第三方模块优先级、重复 id 处理、禁用模块处理。
- [x] P26.6 补齐模块加载/装配契约：组合根注入、依赖服务注册、模块隔离边界、失败关闭策略、诊断事件。
- [x] P26.7 实现 Provider 模块公开接入路径：Provider manifest、protocol binding、model route set、usage/metrics 投影、错误规范。
- [x] P26.8 实现 Tool 模块公开接入路径：tool schema、governance envelope、side effect classification、human gate、tool result projection。
- [x] P26.9 实现 Memory 模块公开接入路径：检索、形成、取代、压缩预留接口与上下文策略对接边界。
- [x] P26.10 新增模板项目：自定义 Provider 模板、自定义 Tool 模板、自定义 Memory 模板，并保证可独立 build/test。
- [x] P26.11 新增模块集成测试矩阵：内置模块、第三方 fake 模块、重复模块、损坏 manifest、缺失配置、禁用模块、健康检查失败。
- [x] P26.12 新增公开教程：从零写 Provider、注册 Tool、接入 Memory、模块装配与故障排查。
- [x] P26.13 更新 CLI/doctor：显示模块发现结果、模块健康状态、缺失配置、治理风险与修复建议。
- [x] P26.14 建立 v0.6 发布门禁：模块 SDK 示例可运行、公开文档无内部路径、CI 覆盖模块模板 build/test。
