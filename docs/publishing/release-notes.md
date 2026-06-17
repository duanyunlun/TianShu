# TianShu Release Notes

本文记录公开 Release 的用户可见变化、验证状态和已知限制。自动生成的 GitHub Release notes 可以补充 PR 列表，但不得替代本文的人工维护结论。

## v0.9.1

当前公开可用版本，目标是把 CLI 路径、可控内核主线、模块化能力、受治理工具面、远程连续性接口、多 Agent 机制和便携 TianShuHome 发布包统一收敛到 Windows x64 可验收发布物。

### 用户可见能力

- CLI 首次自举：便携包自带默认 `tianshu.toml` 和模块模板，`tianshu init --force` 可在包根内重建。
- 离线自检：`tianshu doctor` 检查配置、Provider、模块、健康和治理风险。
- 联网探测：`tianshu doctor --probe` 显式检查 provider endpoint/auth/protocol。
- 真实 provider 协议覆盖：OpenAI Responses、Anthropic Messages、OpenAI-compatible Chat Completions。
- 固定 turn loop：`prepare-context -> model-reason <-> tool-exec -> finalize`。
- Module SDK、内置模块模板和 provider/tool/memory 示例模块已进入发布门禁，可作为第三方模块开发基线。
- 受治理能力面覆盖 write/apply_patch、shell、MCP、memory 和 Sub-Agent；默认只读，外部副作用能力必须显式审批与治理授权。
- Remote continuity interface / remote module boundary 已具备默认关闭、loopback/local 优先和安全门禁基线，可用于后续远程状态同步与远端控制模块。
- 多 Agent fanout/fan-in 机制、deterministic gate 和最终验收案例已纳入发布回归，当前承诺的是受治理协作链路可用，不承诺模型一定会在任意任务中自主拆分。
- 便携 Release 包，入口位于 `bin/tianshu(.exe)`，包根就是 TianShuHome，包含 `tianshu.toml`、`modules/`、`runtime/apphost/`、`README.md`、`LICENSE`、`VERSION.txt`、manifest、checksum 和运行时依赖。

### 验证状态

- Windows 本地 smoke 已作为首要可用性基线。
- 当前本地硬验收以 Windows `tianshu-v0.9.1-win-x64.zip` 为准。
- Linux/macOS 便携包结构和 CI wiring 已保留，但因当前 GitHub Actions 账号额度限制，本轮不把 Linux/macOS 远端 package smoke 作为阻塞项。
- release manifest 会校验 schemaVersion、layout、entry/config/modules/AppHost path、asset name、runtime id、文件大小和 SHA-256。
- v0.6、v0.7、v0.8、v0.9 gate 已作为能力面回归门禁接入 CI 与本地发布检查。
- Release acceptance baseline 见 `docs/publishing/tianshu-release-acceptance.md`，发布前必须覆盖 tag 发布、资产命名、manifest/checksum、Windows smoke、升级和卸载说明。

### 已知限制

- 当前公开可用路径聚焦 CLI。
- Google provider 暂不作为当前阻塞验证路径。
- Sub-Agent 机制已具备受治理执行链路；模型是否自主触发仍受任务复杂度、提示词和 provider 表现影响，不作为 `v0.9.1` 的确定性能力承诺。
- 自演化方向仍是探索性基础设施；P31.8 正式报告结论为部分可行，不承诺已经实现可靠自主进化，见 `docs/audit/tianshu-self-evolution-feasibility-report.md`。

## Unreleased

后续版本按 README 路线图推进：

- v0.6.0：模块化开放。
- v0.7.0：受治理能力面扩展。
- v0.8.0：远程连续性接口与 Remote Module。
- v0.9.0：多 Agent 成熟。
- v0.10.0：自演化基础设施探索。
- v1.0.0：稳定 API、生产级文档、发布门禁和自演化可行性结论继续收敛；当前自演化结论为部分可行。

每次发布前必须确认：

1. README 与 quickstart 仍能指导新用户完成安装、初始化、自检和首条消息。
2. module guide 与当前 Module SDK 边界一致。
3. architecture spec 与当前核心代码边界一致。
4. troubleshooting 覆盖当前最常见 failure code。
5. security model 没有弱化默认安全边界。
6. release notes 记录用户可见变化、验证状态和已知限制。
