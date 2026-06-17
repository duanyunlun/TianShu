# TianShu Release Acceptance

本文定义 TianShu CLI Release 的发布验收基线。它不替代 GitHub Actions，而是把 tag 发布、便携包资产命名、manifest、checksum、Windows smoke、升级和卸载说明固定成可审查的发布条件。

## 1. Tag 发布

正式 Release 必须从 Git tag 触发：

```bash
git tag v0.9.1
git push origin v0.9.1
```

GitHub Actions 只在 `refs/tags/v*` 上执行 GitHub Release 发布。普通 push 和 pull request 可以运行构建、测试、打包和 smoke，但不得创建正式 Release。

## 2. 资产命名

便携 TianShuHome 包使用固定资产名：

| Runtime | Asset |
| --- | --- |
| Windows x64 | `tianshu-<version>-win-x64.zip` |
| Linux x64 | `tianshu-<version>-linux-x64.tar.gz` |
| macOS arm64 | `tianshu-<version>-osx-arm64.tar.gz` |

其中 `<version>` 必须等于 tag 名或手工传入的 release version。发布包内入口：

| Runtime | Entry |
| --- | --- |
| Windows | `bin/tianshu.exe` |
| Linux/macOS | `bin/tianshu` |

包根就是 TianShuHome，必须包含 `tianshu.toml`、`modules/`、`runtime/apphost/`、`README.md`、`LICENSE` 与 `VERSION.txt`。

## 3. Manifest 与 checksum

每次发布必须生成 `release-manifest.json`。Manifest schemaVersion 当前固定为 `1`。

每个 archive record 必须包含：

- `runtimeIdentifier`
- `assetName`
- `relativePath`
- `sha256`
- `sizeBytes`
- `layout = portable-tianshu-home`
- `entryPath`
- `configPath`
- `modulesPath`
- `appHostPath`
- `selfContained`

`tools/Test-TianShuReleaseManifest.ps1` 必须校验：

- manifest version 与期望 version 一致。
- `publishSingleFile=false`、`publishTrimmed=false`、`selfContained=true`、`layout=portable-tianshu-home`。
- 正式全平台 Release 应包含 Windows、Linux、macOS runtime identifier；当前 `v0.9.1` 因 GitHub Actions 额度限制，先以 Windows x64 本地发布物作为硬验收线，Linux/macOS 不作为本轮本地阻塞项。
- 每个 archive 文件存在，大小与 manifest 一致。
- 每个 archive 的 SHA-256 与 manifest 一致。
- 若传入 GitHub repository 和 release tag，GitHub Release 必须包含 `release-manifest.json` 与每个 archive asset。

## 4. Windows smoke

Windows smoke 是首个公开 Release 的最低本地验收线。`tools/Test-TianShuCliReleasePackage.ps1 -RuntimeIdentifier win-x64` 必须验证：

1. Release 包可解压。
2. 包内存在 `README.md`、`LICENSE`、`VERSION.txt`、`tianshu.toml`、`modules/` 和 `runtime/apphost/`。
3. 包内不存在 `AGENTS.md`。
4. 从非包目录 CWD 运行 `bin/tianshu.exe --help` 返回退出码 `0`，且不依赖 `TIANSHU_HOME`。
5. 未运行 `init` 时，`bin/tianshu.exe doctor --json` 能识别 `portableMode=true`、包根配置、包内 AppHost 和可写 runtime root。
6. 无 provider 凭据时，`doctor` fail-closed，报告 `provider_api_key_missing`。
7. `doctor` 包含模块 discovery/loading 投影，且不报告 `packaged_assembly_missing` 或 `apphost_missing`。
8. `bin/tianshu.exe init --provider openai --force --json` 只重建包根内 `tianshu.toml`、provider instance、route set 和 protocol rule 模板。
9. 无 provider 凭据时，`bin/tianshu.exe send --kernel-runtime-loop --json` 允许失败，但默认 artifacts 必须落在包根 `runtime/`，不得在 smoke workspace 创建 `.tianshu/` 或 `.tianshu-cli/`。

Linux/macOS package smoke 由同一脚本在 CI 矩阵中覆盖；在 GitHub Actions 额度恢复前，不把 Linux/macOS 远端 smoke 作为 `v0.9.1` 发布阻塞项。

## 5. 升级说明

便携包升级规则：

1. 下载新版本对应平台 archive。
2. 解压到新的安装目录，或在备份旧包根后替换旧包根内容。
3. 包根就是 TianShuHome；若保留旧配置，保留旧包根内 `tianshu.toml`、`modules/`、`prompt/` 和需要的 `runtime/` 数据。
4. 运行 `bin/tianshu doctor`。
5. 若需要刷新默认配置模板，运行 `bin/tianshu init --provider openai --force`，但不要覆盖真实 secret。

Release 包不应把用户配置、provider key、runtime state 或 workspace 内容打进 archive。升级默认只替换程序文件，不删除用户配置。

## 6. 卸载说明

目录式包卸载规则：

1. 删除 Release 包解压目录。
2. 从 `PATH` 中移除包内 `bin/`。
3. 如果要保留配置，先备份包根内 `tianshu.toml`、`modules/`、`prompt/` 或需要的 `runtime/` 数据。
4. 非便携开发者安装若使用了用户级 TianShuHome，才需要额外删除 `%USERPROFILE%\.tianshu` 或 `~/.tianshu`。

卸载不需要额外后台服务清理；当前公开 CLI 路径不安装系统服务。

## 7. Gate 命令

发布前至少运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-TianShuReleaseAcceptanceGate.ps1 -Configuration Release
```

打包后继续运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-TianShuReleaseManifest.ps1 -PackagesRoot artifacts/release/packages -Version v0.9.1 -RuntimeIdentifiers win-x64
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-TianShuCliReleasePackage.ps1 -PackagesRoot artifacts/release/packages -RuntimeIdentifier win-x64
```
