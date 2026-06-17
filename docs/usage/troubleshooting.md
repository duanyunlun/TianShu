# TianShu Troubleshooting

本文记录公开 CLI 路径的常见故障。排查时优先运行 `tianshu doctor`；只有需要验证 provider endpoint/auth/protocol 时才运行 `tianshu doctor --probe`。

## `provider_api_key_missing`

含义：配置声明了 provider 的凭据环境变量，但当前进程没有读取到对应变量。

处理：

1. 确认 `modules/model/provider-instances/default.toml` 中的 `api_key_env`。
2. 在当前 shell 设置同名环境变量。
3. 重新运行 `tianshu doctor`。

示例：

```powershell
$env:OPENAI_API_KEY = "<your key>"
tianshu doctor
```

不要把真实 key 写入配置文件或提交到仓库。

## `doctor` 失败但 `doctor --probe` 未运行

`doctor` 默认离线执行。它不会调用模型，也不会验证远端 endpoint 是否真实可达。

如果离线 `doctor` 失败，优先修配置、模块和治理问题。只有离线检查通过后，再用：

```powershell
tianshu doctor --probe
```

## Provider 返回 4xx/5xx

常见原因：

- base URL 指向了错误网关。
- 当前模型不支持所选 wire API。
- provider key 与 base URL 所属服务不匹配。
- 远端服务临时不可用。

处理：

1. 确认 provider instance 的 `base_url`。
2. 确认 route set 中的模型确实支持当前协议。
3. 确认环境变量里的 key 与该 base URL 匹配。
4. 运行 `tianshu doctor --probe` 获取 endpoint/auth/protocol 诊断。

当前建议的协议覆盖组合：

| Provider | 协议 | 推荐测试模型 |
| --- | --- | --- |
| `openai` | OpenAI Responses | `gpt-5.5` |
| `anthropic` | Anthropic Messages | `claude-opus-4.8` |
| `openai-compatible` | OpenAI-compatible Chat Completions | `openai-compatible-default` |

## 模块没有加载

`doctor` 会报告模块发现、加载和健康检查投影。常见原因：

- manifest 缺少必需字段。
- `min_tianshu_version` 高于当前版本。
- `sdk_contract_version` 或 `capability_schema_version` 显式声明了未知大版本。
- 第三方模块未进入 allow-list。
- 必需配置未绑定。
- 健康检查失败。

旧 module manifest 可以缺少可选版本字段；但一旦显式声明非法版本或未知大版本，加载必须 fail-closed。

## 写文件失败

写入能力默认不开放。写工具必须满足：

- CLI 显式审批态。
- 治理信封允许对应 tool id 和 module id。
- side effect 上限不低于 `WorkspaceWrite`。
- 路径是 workspace-relative path。

绝对路径即使落在 workspace 内，也应被拒绝。

## Sub-Agent 没有触发

`spawn_agent` 默认隐藏。只有同时满足以下条件才会暴露：

- `send --kernel-runtime-loop --enable-subagents --approve-all`
- Sub-Agent module descriptor 和治理授权存在。
- human gate / approval 边界满足当前策略。

即使工具面已暴露，真实模型也可能不自主触发 `spawn_agent`。这属于可观察行为，不等同机制不可用。

## Release 包运行失败

确认：

1. 下载的是当前平台对应的包。
2. 解压后没有只复制单个可执行文件；便携包需要保留完整目录结构。
3. 包内存在 `bin/tianshu(.exe)`、`tianshu.toml`、`modules/`、`runtime/apphost/`、`README.md`、`LICENSE` 和 `VERSION.txt`。
4. 从非包目录运行 `bin/tianshu --help` 能返回退出码 `0`。
5. `tianshu doctor --json` 能报告 `portableMode=true`、`appHostExists=true` 和 `runtimeWritable=true`。
6. `tianshu init --force` 只重建包根内 `tianshu.toml` 和 `modules` 模板。

发布包完整性由 `tools/Test-TianShuCliReleasePackage.ps1` 和 `tools/Test-TianShuReleaseManifest.ps1` 验证。
