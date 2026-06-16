# src/Features

本目录用于存放“功能类项目”（可选能力/独立功能模块），用于承载未来从外部仓库迁移进来的功能子系统，避免挤占 `Infrastructure/Provider/Presentations` 三层结构。

## 约定

- **功能项目优先做成可复用模块**：对外暴露清晰的 API/CLI 入口，避免 UI/业务直接依赖内部实现细节。
- **不改动三层主结构**：`Infrastructure/Provider/Presentations` 仍作为主线分层；`Features` 仅承载可选能力与周边工具链。
- **文档统一上收**：功能模块若自带 docs，请统一整理到仓库根 `docs/` 下（按 `docs/<type>/<feature>/` 分类）。

## 当前包含

- `WindowsAgent/`：Windows-Agent（Windows 桌面自动化能力）。

