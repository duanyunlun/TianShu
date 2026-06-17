# TianShu Quickstart

本文是 TianShu CLI 的公开快速开始。当前公开可用路径以 CLI 为主；Config GUI、AppHost、VSIX 等宿主仍在架构内,但不是首个公开 Release 的阻塞入口。

## 1. 安装方式

### 从 Release 便携包安装

公开 Release 的目标形态是便携包。下载当前平台压缩包后,解压到一个固定目录,直接运行 `bin/` 下的 CLI。

Windows:

```powershell
.\tianshu-v0.9.1-win-x64\bin\tianshu.exe --help
```

Linux/macOS:

```bash
./tianshu-v0.9.1-linux-x64/bin/tianshu --help
```

便携包根目录就是 TianShuHome。主配置、模块、AppHost 和默认运行产物都相对该目录定位:

| 路径 | 作用 |
| --- | --- |
| `tianshu.toml` | 主配置入口。 |
| `bin/tianshu(.exe)` | CLI 入口。 |
| `runtime/apphost/` | CLI 完整功能需要的 AppHost 运行时。 |
| `modules/` | Provider、Tool、Memory、Policy、Prompt 等模块。 |
| `runtime/.../<workspace-key>/` | 默认 run artifacts、checkpoint、host-control 和诊断产物。 |

便携包默认不向当前工作区写入 `.tianshu/`、`.tianshu-cli/` 或运行产物。CWD 只用于确定工作区和生成 workspace key。只有显式传入输出路径的命令,例如 `--artifacts <path>`,才会把对应产物写到该路径。

### 从源码运行

```bash
git clone https://github.com/duanyunlun/TianShu.git
cd TianShu
dotnet build src/Presentations/TianShu.Cli/TianShu.Cli.csproj --configuration Release
dotnet run --project src/Presentations/TianShu.Cli -- init --provider openai
```

源码运行和开发者安装脚本可以继续使用用户级 `~/.tianshu` 作为非便携回退目录。

## 2. 初始化配置

便携包应自带默认 `tianshu.toml` 和 `modules/model/**` 模板,正常情况下无需先运行 `init`。

如果需要重建默认配置:

```powershell
tianshu init --provider openai --force
```

便携模式下,`init` 应在包根内就地初始化或重置配置。非便携模式下,`init` 可以继续写入用户级 TianShuHome:

| 平台 | 非便携默认目录 |
| --- | --- |
| Windows | `%USERPROFILE%\.tianshu` |
| Linux/macOS | `~/.tianshu` |

模板只保存环境变量名,不保存 API key。

## 3. 配置 Provider 凭据

TianShu 默认通过环境变量读取凭据。

| Provider | 协议 | 推荐测试模型 | 环境变量 |
| --- | --- | --- | --- |
| `openai` | OpenAI Responses | `gpt-5.5` | `OPENAI_API_KEY` |
| `anthropic` | Anthropic Messages | `claude-opus-4.8` | `ANTHROPIC_API_KEY` |
| `openai-compatible` | OpenAI-compatible Chat Completions | `openai-compatible-default` | `OPENAI_COMPATIBLE_API_KEY` |

示例:

```powershell
$env:OPENAI_API_KEY = "<your key>"
```

不要把真实 key 写进 `tianshu.toml`、README、issue、日志或验收证据。

## 4. 自检

离线自检:

```powershell
tianshu doctor
```

`doctor` 默认不联网、不调用模型、不产生 API 成本。它会检查配置、Provider、模块发现/加载、模块健康、治理风险与修复建议。便携模式还必须检查包根、AppHost、默认运行产物目录可写性和平台包匹配。

联网探测:

```powershell
tianshu doctor --probe
```

`doctor --probe` 会访问配置的 provider endpoint,用于验证 endpoint、auth 和协议基础可达性。

## 5. 发送第一条消息

```powershell
tianshu send --message "帮我总结当前目录结构" --json
```

常用安全开关:

| 场景 | 命令边界 |
| --- | --- |
| 只读分析 | 默认路径,只开放只读文件系统工具。 |
| 写入 workspace | 必须显式审批,写路径必须是 workspace-relative path。 |
| Sub-Agent | 必须显式 `--enable-subagents --approve-all`,并满足治理授权。 |
| 显式 artifact 输出 | 使用 `--artifacts <path>` 时,本次 send artifact 可写入用户指定路径。 |

## 6. 清理或重建

便携包自身的清理方式是删除解压目录。默认运行产物也应位于该目录下的 `runtime/` 中。

非便携模式下,删除 `%USERPROFILE%\.tianshu` 或 `~/.tianshu` 可清理用户级安装与配置。

若命令显式指定了 artifact 输出目录,该目录由用户自行管理。

## 7. 下一步阅读

- [Module Integration Guide](modules.md)
- [Architecture Spec](../tianshu-architecture-spec.md)
- [Portable Distribution Design](../architecture/tianshu-portable-distribution-design.md)
- [Troubleshooting](troubleshooting.md)
- [Security Model](../security/tianshu-security-model.md)
- [Release Notes](../publishing/release-notes.md)
