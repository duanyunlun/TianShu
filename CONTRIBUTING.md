# Contributing to TianShu

## 中文

欢迎通过 issue 和 PR 参与 TianShu。

提交变更前请确认：

- 变更与 `docs/tianshu-architecture-spec.md` 中的六层架构边界一致。
- 跨层交互必须通过类型化契约，不绕过 Host Gateway、Control Plane、Kernel 或 Execution Runtime 的职责边界。
- 新功能默认进入 `CoreIntent -> StageGraph -> RuntimeStep` 主线。
- 涉及对外契约、配置、协议、架构边界或模块接入方式的变更，必须同步更新 `docs/` 下的相关设计文档。
- 新增行为应补充对应测试；至少提供可复现的最小验证步骤。
- 不提交本地运行状态、私有配置、API key、token、日志、缓存或测试临时产物。

公开仓库是无历史快照仓库。如果你基于公开仓库提交 PR，请直接针对当前 `master` 分支发起。

## English

Issues and pull requests are welcome.

Before submitting a change, please check that:

- The change respects the six-plane architecture boundaries in `docs/tianshu-architecture-spec.md`.
- Cross-plane interaction goes through typed contracts and does not bypass Host Gateway, Control Plane, Kernel, or Execution Runtime responsibilities.
- New capabilities follow the `CoreIntent -> StageGraph -> RuntimeStep` main line by default.
- Changes to public contracts, configuration, protocols, architecture boundaries, or module integration must update the relevant design documents under `docs/`.
- New behavior should include tests, or at least a minimal reproducible verification path.
- Local runtime state, private configuration, API keys, tokens, logs, caches, and temporary test artifacts must not be committed.

The public repository is a clean snapshot repository without private development history. For public contributions, open pull requests against the current `master` branch.
