# TianShu 便携式分发包设计

## 1. 文档定位

本文是 TianShu **CLI 便携式(绿色版)分发包**的当前设计基线,定义解压即用、自包含、无需安装的公开发布形态与配置定位规则。

它细化 `docs/tianshu-architecture-spec.md` 的 Experience / Host Gateway 边界在「对外发布物」维度上的落地形态。公开分发入口采用便携包;用户目录安装脚本保留为开发者/本机刷新辅助路径,不是首版公开用户主路径。

> **状态:已进入 P31.10 实施基线。** 当前代码已实现 `<pkg>/bin -> <pkg>` 的便携 TianShuHome 推导、便携配置优先、System 层隔离、默认运行产物迁移到包根 `runtime/`、CLI AppHost 包根探测、doctor 可写性/AppHost 投影,以及 Windows 便携包打包与 smoke。

核心命题:**一个 TianShu CLI 发布包 = 一个解压即用的 TianShuHome 目录;二进制、默认配置、模块、AppHost 运行时、状态数据全部相对包根定位,不依赖用户目录、系统 PATH 或环境变量指路。**

## 2. 设计原则

1. **解压即用。** 用户下载平台压缩包、解压到任意可写目录,即可直接用完整路径运行其中的 CLI;无需安装、无需配置 PATH。
2. **包根就是 TianShuHome。** 便携包根目录直接承载现有 TianShuHome 语义:`tianshu.toml`、`modules/`、`runtime/`、`data/` 都位于包根下,避免新增第二套配置根模型。
3. **分发资源与默认运行产物自包含。** 配置、模块、AppHost 运行时和默认运行产物目录都在解压目录内自成一体。默认情况下不得向用户工作区 CWD 写入 `.tianshu/`、`.tianshu-cli/`、run artifacts、checkpoint 或 host-control 文件;只有用户显式指定输出路径(如 `--artifacts`)时才允许写入该路径。
4. **程序位置定位,不依赖外部位置。** 便携模式下,属于 TianShu 自身的资源都由 `AppContext.BaseDirectory` 推导包根,而非当前工作目录、用户目录或环境变量。
5. **工作区与程序目录严格分离。** 用户的工作区(agent 读写的目标)由当前工作目录(CWD)或显式 `--cwd` 决定,与包根正交,两者不得混用。
6. **可写性是用户责任,但提示必须到位。** TianShu 不替用户解决"解压到只读位置"的问题;当包根不可写时,必须 fail-closed 并给出清晰引导,不得静默退回用户目录。
7. **Secret 绝不入包。** 默认配置随包分发,但只保存环境变量名,绝不保存任何 API key、token、secret 或个人路径。

## 3. 适用范围

| 项 | 本版处理 |
| --- | --- |
| CLI 便携式平台包(Windows / Linux / macOS) | **本版主线**:解压即用、包根即 TianShuHome、程序位置定位。 |
| 默认 `tianshu.toml` / 默认模块模板 | **本版主线**:包内预置根级 `tianshu.toml` 与 `modules/`,首次运行无需 `init` 即可进入诊断或发送路径。 |
| AppHost 运行时 | **本版主线的依赖组件**:CLI 的完整功能仍需要 `<pkg>/runtime/apphost/`,因此必须随包发布并被 CLI 自动探测。 |
| Config GUI / VSIX | **不作为首版公开便携包主线**:可后续单独纳入便携包或独立分发。 |
| 安装到用户目录 `~/.tianshu` + 配 PATH | **非公开主线**:安装脚本可保留为开发者辅助路径;便携模式下不参与配置根解析。 |
| 全局安装 / NuGet global tool / winget / Homebrew | **不在本版范围**:后续版本可选,不阻塞首个公开 Release。 |
| single-file / trimming / NativeAOT | **不在本版范围**:第一版使用目录式发布,不启用 single-file 或 trimming,优先保证模块动态加载稳定。 |

### 3.1 self-contained 合同

公开便携包固定采用**目录式 self-contained**:

- 用户体验与"解压即用"一致。
- 避免用户因缺少 .NET Runtime 无法启动。
- 不启用 single-file/trimming,保留动态加载模块的稳定性。

## 4. 包结构

三平台包结构保持一致,仅入口文件名与压缩格式不同。

```text
tianshu-<version>-win-x64.zip
└─ tianshu-<version>-win-x64/
   ├─ bin/
   │   ├─ tianshu.exe
   │   └─ <CLI self-contained runtime files / deps>
   ├─ runtime/
   │   └─ apphost/
   │      ├─ TianShu.AppHost.exe
   │      └─ <AppHost runtime files / deps>
   ├─ modules/
   │   ├─ model/
   │   │   ├─ provider-adapters/
   │   │   ├─ provider-instances/
   │   │   ├─ route-sets/
   │   │   └─ protocol-rules/
   │   ├─ tools/
   │   ├─ memory/
   │   ├─ policies/
   │   ├─ prompts/
   │   └─ ...
   ├─ data/
   ├─ logs/
   ├─ tianshu.toml
   ├─ README.md
   ├─ LICENSE
   ├─ VERSION.txt
   └─ THIRD-PARTY-NOTICES.txt
```

- **包根目录** = 解压后的顶层目录,也是 TianShuHome。
- **CLI 入口** 位于 `bin/` 下;Windows 为 `tianshu.exe`,非 Windows 为 `tianshu`。
- **主配置** 位于包根 `tianshu.toml`,不放入 `config/` 子目录。
- **模块目录** 位于包根 `modules/`,与现有 `TianShuRuntimeLayoutPaths.ResolveModulePathFromHome()` 语义一致。
- **AppHost** 位于 `runtime/apphost/`,供 CLI 自动探测并启动。
- **`data/`、`logs/`** 可随包创建为空目录,也可首次运行时创建;但创建位置必须在包根下。

## 5. 配置根定位规则(核心)

### 5.1 包根 = TianShuHome

```text
可执行文件实际路径   = <pkg>/bin/tianshu(.exe)
程序目录            = AppContext.BaseDirectory          -> <pkg>/bin/
包根 / TianShuHome  = 程序目录的父目录                  -> <pkg>/
主配置              = <pkg>/tianshu.toml
模块根              = <pkg>/modules/
运行时根            = <pkg>/runtime/
数据根              = <pkg>/data/
日志根              = <pkg>/logs/   (注:logs 不由 TianShuHome 自动派生,由配置键 log_dir 相对包根重定位;见 §9.1 说明)
```

便携模式下,TianShu 自身资源只由可执行文件位置推导:

- 不读 `TIANSHU_HOME` 来决定便携包根。
- 不读 `~/.tianshu` 来决定便携包根。
- 不使用 CWD 来决定便携包根。

`TIANSHU_HOME` 与用户目录链路只作为非便携回退路径存在,不得在已识别为便携包时覆盖包根。

### 5.2 便携模式自动判定

便携模式由程序位置自动判定,建议信号为:

```text
AppContext.BaseDirectory = <candidate>/bin/
存在 <candidate>/tianshu.toml
存在 <candidate>/modules/
```

满足上述条件时,`<candidate>` 即便携包根。该判定不依赖环境变量,也不要求用户传 `--portable`。

若判定失败,保持现有非便携链路:

1. 显式命令参数中的配置文件路径优先。
2. `TIANSHU_HOME`。
3. 用户目录 `~/.tianshu`。

这保证源码树开发、测试和历史安装路径仍能运行,但公开分发包默认走便携路径。

### 5.3 工作区 = CWD / `--cwd`,与包根严格分离

| 维度 | 基准 | 用途 |
| --- | --- | --- |
| 配置 / 模块 / AppHost / 状态 | `AppContext.BaseDirectory` -> 包根 | TianShu 自身资源,跟着程序走。 |
| agent 工作区 | 当前工作目录(CWD)或显式 `--cwd` | agent 读写的目标目录,跟着用户启动位置或显式参数走。 |

示例:用户在 `D:\MyProject\` 下执行 `D:\Tools\tianshu\bin\tianshu.exe send ...`

- 配置/模块/AppHost 从 `D:\Tools\tianshu\` 加载。
- agent 操作的工作区是 `D:\MyProject\`。

**两者永不混用。** 用包根定位工作区,或用 CWD 找主配置,都是便携模式 bug。

### 5.4 运行产物落盘规则:key by CWD, store in TianShuHome

CWD 只用于**关联工作区上下文**与**生成稳定分桶键**,不得作为默认存储根。便携模式和非便携模式都应遵守同一规则:

```text
workspace key      = hash(normalized CWD)
default run root   = <TianShuHome>/runtime/runs/<workspace-key>/
host-control root  = <TianShuHome>/runtime/kernel-runtime/<workspace-key>/host-control/
checkpoint root    = <TianShuHome>/runtime/kernel-runtime/<workspace-key>/checkpoints/
send artifact root = <TianShuHome>/runtime/runs/<workspace-key>/send/
```

这保留了 `ResumeLatestMatchCwd` 这类"按 CWD 关联会话"的语义,但不会污染用户仓库。CWD 下是否存在 `.git`、`.tianshu/` 或其他项目文件,只影响工作区解析和 Project 层配置合并,不应成为默认运行产物落盘位置。

当前代码中存在需要修正的历史默认行为:

- host-control / checkpoint / pending steer 等写入 `<cwd>/.tianshu/kernel-runtime/...`。
- `send` 录制 artifact 默认写入 `<cwd>/.tianshu-cli/runs/...`。

这些路径是早期调试便利留下的实现债,不是正式设计。落地便携分发前必须迁移到 TianShuHome 下的 workspace-key 分桶。用户显式传入的 `--artifacts <path>` 是唯一例外:该参数表示用户主动要求把本次 artifact 写到指定位置,可以是 CWD 内路径。

### 5.5 不可写时 fail-closed

程序需要写包根、`runtime/`、`data/`、`logs/` 或执行 `init --force` 时,若目标不可写:

- 返回结构化 failure code,例如 `portable.package_root_not_writable`。
- 给出中文/英文引导:"无法写入 TianShu 便携包目录 `<path>`。请将 TianShu 解压到一个可写位置,例如用户目录下的独立工具目录,不要在 Program Files 等受保护位置解压后运行。"
- 不静默退回 `~/.tianshu`。
- 不静默退回 CWD。

只读位置导致的不可写是用户部署选择问题;TianShu 只负责明确失败和引导。

只读 TianShuHome 的用户可感知后果必须明确:便携模式默认运行产物写入 `<TianShuHome>/runtime/...`。如果包根不可写,需要 runtime 写入的命令必须 fail-closed,包括 `send`、`follow-up`、`resume` 等依赖 checkpoint、host-control、pending steer、turn evidence 或 run artifacts 的路径。这意味着用户把便携包解压到 `Program Files`、只读挂载目录或其他无写权限目录时,基本交互命令可能无法运行。TianShu 不自动回退到 CWD 或用户目录,因为这会破坏便携包"删除包根即可清理自身状态"的边界。用户应将便携包解压到可写目录;若确实需要外部输出,只能通过显式参数如 `--artifacts` 指定。

### 5.6 配置层隔离(便携模式必须显式处置 system / project 层)

这是便携模式最容易破功、当前实现尚未处理的一处:**配置加载不是只读单层 `tianshu.toml`,而是多层合并**。`TianShuTomlConfigurationLoader.ResolveLoadLayers()` 当前无条件加入下列层并按顺序合并(后者覆盖前者):

```text
System  ->  UserModule 默认  ->  User  ->  UserProviderInstance  ->  Project(从 project root 向下逐级到 CWD 的每个 .tianshu/)  ->  SessionFlags(--config/--profile)
```

其中 **System 层路径**在 Windows 是 `ProgramData\TianShu\tianshu.toml`、在 Linux/macOS 是 `/etc/tianshu/tianshu.toml`,**且 `TIANSHU_HOME` 不能重定向它**——唯一能改写 system 根的是环境变量 `TIANSHU_SYSTEM_CONFIG_ROOT`。

直接后果:**一台装过 TianShu system 配置的机器上运行便携包,默认仍会静默合并宿主机的 system 层**,这与 §2「分发资源与默认运行产物自包含」正面冲突。便携模式必须显式处置,不能沿用现有多层合并默认值:

| 层 | 便携模式处置 | 理由 |
| --- | --- | --- |
| System(`ProgramData\TianShu` / `/etc/tianshu`) | **隔离**:便携模式下不读宿主机 system 层(实现上可在识别为便携包时将 system 根指向包内或空目录)。 | system 层是宿主机全局状态,读取它即违反"包内分发资源自包含"。 |
| Project / CWD(`<cwd>/.tianshu/tianshu.toml`) | **保留读取**:它属于工作区语义,与 §5.3「工作区跟随 CWD」一致。 | 工作区层是用户项目配置输入,可读;但默认运行产物不得写入 CWD。 |
| User(`~/.tianshu`)/ `TIANSHU_HOME` | 不参与便携包根解析;作为非便携回退链时才生效。 | 见 §5.1。 |

> **"自包含"的准确含义(必须明确,避免误导)**:便携包的自包含指**包内分发资源与默认运行产物自包含**——二进制、默认配置、模块、AppHost、默认 run artifacts、checkpoint 和 host-control 都在包根下。它**不**等于"运行结果完全不受外部输入影响":只要用户在一个含 `<cwd>/.tianshu/tianshu.toml` 的工作区里启动,该 Project 层仍会参与本次合并。这是刻意保留的工作区配置读取语义(与 §5.3 一致),不是漏洞。需隔离的写入行为是**默认运行产物写入 CWD**;需隔离的读取行为是**宿主机全局 System 层**,因为它与用户的工作区选择无关、且无法随包删除。

> 实现提示:仅靠"主配置走包根"不足以自包含,必须同时切断 system 层,并把 runtime artifact / checkpoint / host-control 默认存储根迁移到 TianShuHome。当前 `ResolveLoadLayers` 对 system 层是硬加入(无开关),Kernel→Runtime host-control/checkpoint 与 send artifacts 也有 CWD 默认写入行为,所以这是**需要改动加载器与 CLI/Runtime 存储路径**的工作,不是纯文档约定。

## 6. 首次运行与命令语义

### 6.1 解压即带默认配置,无需先 init

包内预置:

- `<pkg>/tianshu.toml`
- `<pkg>/modules/model/provider-instances/default.toml`
- `<pkg>/modules/model/route-sets/default.toml`
- `<pkg>/modules/model/protocol-rules/default.toml`
- 其他内置模块 manifest / 默认模板

用户解压后无需运行 `init` 即可执行:

- `tianshu --help`
- `tianshu doctor --json`
- 配好 provider 环境变量后执行 `tianshu send ...`

缺少凭据时应 fail-closed,返回 `provider_api_key_missing` 或等价诊断,并提示应设置哪个环境变量,不得回显环境变量值。

### 6.2 `tianshu init`

便携模式下,`init` 是包内就地初始化/重置命令:

- 默认:若 `<pkg>/tianshu.toml` 与核心 `modules/model/**` 已存在,提示配置已存在,不覆盖。
- `init --force`:重新生成包内默认配置模板,不写入 secret。
- `init --provider <id>`:在默认模板基础上选择 active provider / route set。

`init` 不应在便携模式下默认写入用户目录。

### 6.3 `tianshu doctor`

`doctor` 默认离线、不联网、不调用模型、不产生 API 成本。检查项分**现有**与**便携模式需新增**两类(已核对当前 `CliOnboardingCommandRunner` 实现):

现有(可直接复用):
- `tianshu.toml` 是否存在且可解析(诊断码 `config_missing` / `config_load_failed`)。
- provider / model / base_url / api_key_env 配置是否完整(`provider_missing` / `model_missing` / `provider_base_url_missing` / `provider_api_key_env_missing`)。
- `api_key_env` 指向的环境变量是否实际设置(`provider_api_key_missing`,不回显值)。
- 打包程序集是否存在(`packaged_assembly_missing`)。
- **modules 投影已存在**:报告 discovered / selected / registered / rejected / unavailable 等计数。

便携模式需新增(当前实现没有,属待实现项,不得在 doctor 输出里假装已有):
- 包根是否可识别(便携布局解析成功)。
- AppHost 是否存在于 `<pkg>/runtime/apphost/`。
- 包根 / `runtime/` / `data/` 需要写入时是否可写(当前 doctor 只做 `File.Exists`,**无任何可写性探测**)。
- 平台匹配:包 RID 与当前 OS/架构是否一致(当前**无** OS/架构/RID 校验,仅有 dll 存在性检查;见 §9.3 `PlatformPackageMismatch`,需先补判定信号如 VERSION.txt 记录 RID)。

`doctor --probe` 可作为后续显式联网探测模式,但不应混入默认 smoke。

### 6.4 Secret 处理不变量

- 默认配置只保存环境变量名,不保存 secret。
- 打包产物中不得包含 API key、token、个人路径、机器用户名、私有仓库地址。
- smoke / safety scan 必须覆盖包内 `tianshu.toml`、`modules/`、README 与脚本产物。

## 7. 涉及项目与代码归属

| 项目 / 目录 | 归属内容 |
| --- | --- |
| `src/Contracts/TianShu.Contracts.Configuration` | 便携包根解析、TianShuHome 路径契约、`modules/runtime/data` 派生规则。 |
| `src/Core/TianShu.Configuration` | 配置路径包装工具、模块路径/数据路径从 TianShuHome 派生的实现保持一致。 |
| `src/Core/TianShu.RuntimeComposition` | 默认配置路径、配置层加载、provider instance / route set / protocol rules 的包内加载。 |
| `src/Presentations/TianShu.Cli` | CLI 默认配置、`init`/`doctor`/`send` 语义、CWD 与包根分离。 |
| `src/Presentations/TianShu.Cli/Runtime/Bootstrap` | 让 CLI 从包根找到 `<pkg>/runtime/apphost/TianShu.AppHost.exe`;需新增「BaseDirectory 的父目录(=包根)」作为探测根(详见 §8)。 |
| `src/Hosting/TianShu.AppHost` | 作为 CLI 完整功能运行时随包发布,读取同一份包根配置。 |
| `tools/Publish-TianShuCliRelease.ps1` | 组装完整便携包,不再只发布 CLI 平铺目录。 |
| `tools/Test-TianShuCliReleasePackage.ps1` | 解压后不设置 `TIANSHU_HOME`,直接从任意 CWD 运行包内 CLI。 |
| `tools/Test-TianShuReleaseManifest.ps1` | 校验便携资产命名、入口路径、self-contained 标志、checksum。 |
| `tools/Test-TianShuReleaseAcceptanceGate.ps1` | 静态门禁同步到便携包结构。 |
| `docs/publishing/` | 发布说明、release smoke、acceptance、release notes 同步便携包形态。 |

## 8. 与现有定位逻辑的关系

便携包不是引入全新的配置体系,而是把**包根复用为现有 TianShuHome**。

| 现有件 | 现状职责 | 便携模式处置 |
| --- | --- | --- |
| `TianShuRuntimeLayoutPaths` | 读 `TIANSHU_HOME`,否则回退 `~/.tianshu`,并派生 `tianshu.toml`、`modules/`、`runtime/`、`data/`。 | 增加便携包根自动判定;已判定为便携时返回包根。 |
| `TianShuHomePathUtilities` | 对上层暴露 TianShuHome / runtime / modules / data 路径。 | 继续作为统一入口,不新增 `config/` 根模型。 |
| `RuntimeConfigurationComposition.ResolveDefaultPath()` | 返回默认 `tianshu.toml`。 | 便携模式下返回 `<pkg>/tianshu.toml`。 |
| `ResolveModulePathFromConfig(configPath, ...)` | 从 config 文件所在目录推导 `modules/`。 | 因主配置位于包根,继续正确解析 `<pkg>/modules/...`。 |
| `CliAppHostLaunchResolver` | 探测根为 `工作目录` / `AppContext.BaseDirectory` / `~/.tianshu`,每根下查 `<root>/runtime/apphost/TianShu.AppHost.exe` 等三条相对路径。 | **需改动**:便携模式下 `BaseDirectory = <pkg>/bin/`,现有探测会查 `<pkg>/bin/runtime/apphost/`,而 AppHost 实际在包根 `<pkg>/runtime/apphost/`,差一级目录命不中。需**新增「BaseDirectory 的父目录(=包根)」作为探测根**,并置于用户目录探测之前。 |

关键约束:不要把主配置放到 `<pkg>/config/tianshu.toml`。否则 `ResolveModulePathFromConfig()`(从 config 文件所在目录推导 `modules/`)会推导到 `<pkg>/config/modules`,破坏现有 TianShuHome 模型。已核实加载器中的 user-module 默认层、provider-instance 层、known-module 路径、prompt 包路径**全部走 `ResolveModulePathFromConfig`(跟随主配置目录),而非 `ResolveModulePathFromHome`**,所以主配置位置一旦偏离包根,模块/prompt 路径会整体偏移。

> **AppHost 探测的依据**:现有 `EnumerateExecutableProbeRoots` 三个探测根中,`BaseDirectory` 在便携包里等于 `<pkg>/bin/`(CLI 入口所在),而非包根;`ProbeExecutableUnderRoot` 查的是 `<root>/runtime/apphost/...`。两者叠加恰好查到 `<pkg>/bin/runtime/apphost/`,落空。这也解释了为何现有 Install 脚本布局(CLI 在 `~/.tianshu/bin/`、AppHost 在 `~/.tianshu/runtime/apphost/`)能工作——它靠的是第三个探测根 `~/.tianshu` 命中,而便携模式没有这个 home,所以必须显式补包根探测根。

## 9. 设计接口骨架

接口命名是设计基线,实现时可按现有代码风格调整;职责、优先级与 fail-closed 行为不得弱化。

### 9.1 便携包根解析

归属:`src/Contracts/TianShu.Contracts.Configuration`

```csharp
/// <summary>
/// 便携包目录布局。包根直接等价于 TianShuHome。
/// Portable package layout. PackageRoot is TianShuHome.
/// </summary>
public sealed record PortableTianShuHomeLayout(
    string PackageRoot,
    string BinRoot,
    string ConfigFilePath,
    string ModulesRoot,
    string RuntimeRoot,
    string DataRoot);
    // 注:不含 LogsRoot。日志目录不由 TianShuHome 自动派生,而是由配置键 log_dir 决定,
    // 加载器会把 log_dir 相对配置文件所在目录(即包根)重定位。便携模式只需保证默认
    // log_dir 落在包根下(如 "logs"),不应在本布局里把 logs 当作 Home 派生项。

public static class PortableTianShuHomeResolver
{
    public static PortableTianShuHomeLayout? TryResolve()
        => TryResolveFrom(AppContext.BaseDirectory);

    internal static PortableTianShuHomeLayout? TryResolveFrom(string programDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programDirectory);

        var binRoot = Path.GetFullPath(programDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        var packageRoot = Directory.GetParent(binRoot)?.FullName;
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            return null;
        }

        var configFilePath = Path.Combine(packageRoot, "tianshu.toml");
        var modulesRoot = Path.Combine(packageRoot, "modules");
        if (!File.Exists(configFilePath) || !Directory.Exists(modulesRoot))
        {
            return null;
        }

        return new PortableTianShuHomeLayout(
            PackageRoot: packageRoot,
            BinRoot: binRoot,
            ConfigFilePath: configFilePath,
            ModulesRoot: modulesRoot,
            RuntimeRoot: Path.Combine(packageRoot, "runtime"),
            DataRoot: Path.Combine(packageRoot, "data"));
    }
}
```

### 9.2 配置层解析优先级

便携模式应是默认公开分发入口,但不能破坏源码树开发与历史安装。需要区分**两个不同的概念**,过去版本曾把它们混为一谈:

**(A) TianShuHome / 主配置根的解析优先级**(决定"自身资源"的根):

```text
1. 显式 --config-file / 命令参数中已指定的路径(仅影响本次主配置加载,见下方注)
2. 便携包根自动判定(AppContext.BaseDirectory -> ../tianshu.toml + ../modules)
3. TIANSHU_HOME
4. 用户目录 ~/.tianshu
```

**(B) 实际加载时的多层合并**(当前 `ResolveLoadLayers` 行为,便携模式必须按 §5.6 处置):

```text
System -> UserModule 默认 -> User -> UserProviderInstance -> Project(各级 .tianshu) -> SessionFlags
```

(A) 只决定 User 层那一档的根落在哪;它**不能单独决定自包含性**,因为 System 层与 Project 层是独立加入的。便携模式要兑现"自包含",必须同时按 §5.6 隔离 System 层、保留 Project 层读取,并按 §5.4 迁移默认运行产物落盘根。仅做 (A) 而不处理 (B) 和运行产物落盘是不够的。

注意:(A) 第 1 项是命令级配置文件路径,不等同于 TianShuHome。它只影响本次主配置加载;模块路径仍从该 config 所在目录推导(`ResolveModulePathFromConfig`)。

### 9.3 可写性闸门

```csharp
public enum PortableLayoutFailureCode
{
    None = 0,
    PackageRootNotWritable,
    PackageRootNotFound,
    ConfigFileMissing,
    ModulesRootMissing,
    AppHostMissing,
    PlatformPackageMismatch
}

public sealed record PortableLayoutProbeResult(
    bool Ready,
    PortableLayoutFailureCode FailureCode,
    string? GuidanceZh,
    string? GuidanceEn);

public static class PortableLayoutProbe
{
    public static PortableLayoutProbeResult Probe(PortableTianShuHomeLayout layout);

    public static PortableLayoutProbeResult ProbeWritable(string targetRoot);
}
```

写入前必须检查目标位置。不可写时返回结构化错误,不写用户目录,不创建隐藏回退状态。

当前 doctor 已输出 `portableMode`、包根配置、`modulesRoot`、`runtimeRoot`、`runtimeWritable`、`appHostPath` 和 `appHostExists`。`VERSION.txt` 已记录 `runtimeIdentifier`;更细的 OS/架构/RID mismatch 诊断仍可后续加强,不得在未实现前作为硬验收条件。

## 10. 打包脚本改造

`tools/Publish-TianShuCliRelease.ps1` 负责完整便携包组装:

1. 包名为 `tianshu-<version>-<rid>.zip` 或 `tianshu-<version>-<rid>.tar.gz`,例如 `tianshu-v0.9.1-win-x64.zip`。
2. CLI publish 输出到 `<pkg>/bin/`。
3. AppHost publish 输出到 `<pkg>/runtime/apphost/`。
4. 默认配置模块模板生成到 `<pkg>/modules/`;内置 provider/tool/memory/policy/prompt/diagnostic/artifact 能力由随包程序集与内置 descriptor 暴露,第三方模块后续可继续放入 `<pkg>/modules/`。
5. 默认主配置生成到 `<pkg>/tianshu.toml`。
6. 默认 provider instances / route sets / protocol rules 生成到 `<pkg>/modules/model/**`。
7. `README.md`、`LICENSE`、`VERSION.txt` 放在包根。
8. 包内不得包含 `AGENTS.md`、测试证据、私有仓库路径、用户本机路径、API key。
9. release manifest 记录:
   - `layout = "portable-tianshu-home"`
   - `entryPath = "bin/tianshu.exe"` 或 `bin/tianshu`
   - `configPath = "tianshu.toml"`
   - `modulesPath = "modules"`
   - `appHostPath = "runtime/apphost/TianShu.AppHost.exe"`
   - `selfContained = true`
   - sha256 / sizeBytes

便携打包脚本不得设置用户级 `TIANSHU_HOME`,不得修改 PATH,不得复制 `AGENTS.md`、测试证据、私有路径、API key 或本机 runtime state。

## 11. 测试矩阵

| 测试 | 断言 |
| --- | --- |
| 包根推导 | `TryResolveFrom("<pkg>/bin")` 返回 `PackageRoot == <pkg>`。 |
| 默认配置路径 | 便携模式下默认配置为 `<pkg>/tianshu.toml`。 |
| 模块路径推导 | 从 `<pkg>/tianshu.toml` 推导出的模块路径为 `<pkg>/modules/...`。 |
| CWD 不污染包根 | 切换多个 CWD 后,同一可执行文件解析出的包根不变。 |
| 环境变量不污染便携包根 | 设置 `TIANSHU_HOME` 后,已识别便携包仍使用 `<pkg>`。 |
| **System 层隔离** | 便携模式下,即使宿主机存在 `ProgramData\TianShu\tianshu.toml` / `/etc/tianshu/tianshu.toml`,加载结果**不含**该 system 层内容(见 §5.6)。 |
| Project 层保留 | `<cwd>/.tianshu/tianshu.toml` 仍参与合并,与工作区语义一致。 |
| 工作区跟随 CWD / `--cwd` | agent 工作区等于用户启动 CWD 或显式 `--cwd`,不等于包根。 |
| 默认运行产物不写 CWD | 未显式传入输出路径时,host-control/checkpoint/turn evidence/send artifacts 等默认落在 `<TianShuHome>/runtime/.../<workspace-key>/`,而不是 CWD 下的 `.tianshu/` 或 `.tianshu-cli/`。 |
| 显式 artifact 输出 | 用户传入 `--artifacts <path>` 时,仅本次 send artifacts 可写入该显式路径;该行为必须在结果中标明。 |
| AppHost 随包探测 | CLI 能找到 `<pkg>/runtime/apphost/TianShu.AppHost.exe`。 |
| 不可写 fail-closed | 包根 runtime 不可写时返回 `tian_shu_home_runtime_not_writable`,且未写入用户目录或 CWD。 |
| Secret 不入包 | 扫描包内文件,断言无 API key/token/个人路径/私有仓库路径。 |
| 解压结构完整性 | 解压后存在 `bin/<entry>`、`tianshu.toml`、`modules/`、`runtime/apphost/`,且不含 `AGENTS.md`。 |
| 模块随包加载 | `doctor` 能发现并注册 `<pkg>/modules/` 下的模块。 |
| 免 init 诊断 | 解压后未跑 `init`,直接 `doctor --json` 能给出离线诊断。 |
| 缺 key fail-closed | 未设置 provider key 时,`send` 返回 provider key 缺失诊断,不回显 secret。 |

源码级守护优先级最高的四条:

1. 包根定位不受 CWD 影响。
2. 包根定位不受 `TIANSHU_HOME` 覆盖。
3. 包根不可写时不退回用户目录。
4. 便携模式不读宿主机 System 层配置。
5. 默认运行产物不写入 CWD,只按 CWD hash 分桶存入 TianShuHome runtime。

## 12. 发布门禁集成

便携版验收并入现有 release smoke / acceptance 链路:

- `tools/Test-TianShuCliReleasePackage.ps1`:解压包后清空 `TIANSHU_HOME`,从非包目录 CWD 运行 `<pkg>/bin/tianshu.exe`。
- `tools/Test-TianShuReleaseManifest.ps1`:校验便携布局字段、资产名、入口路径、checksum、self-contained 取值。
- `tools/Test-TianShuReleaseAcceptanceGate.ps1`:静态断言必须锁定便携包结构,资产名为 `tianshu-<version>-<rid>`。
- `docs/publishing/tianshu-release-smoke.md`:写明 Windows 本地 smoke 是首版硬验收;Linux/macOS 可先由 CI 构建与结构检查覆盖,待有设备后补人工 smoke。
- `tools/Test-TianShuPublicReleaseSafetyScan.ps1`:负责公开发布面的 secret、私有路径和测试产物扫描;便携包 archive 内容扫描纳入 P31.10.9/P31.10.11 收口。

README 只能声明已经通过对应 smoke 的能力。Windows 未通过本地 smoke 前,不得声称 Windows 绿色包可用。

## 13. 落地顺序

P31.10 的落地顺序以 `docs/tianshu-implementation-tracker.md` 为准。本文只保留设计基线和验收口径,不再记录已失效的提案前置状态。

每步应独立可验收,并同步 `docs/tianshu-implementation-tracker.md`。

1. 修订发布/使用文档口径:包根即 TianShuHome,主配置为 `<pkg>/tianshu.toml`。
2. 增加便携包根解析与单元测试。
3. 接入默认 TianShuHome / 默认配置路径解析。
4. **隔离 System 层 + 保留 Project 层读取**(改 `ResolveLoadLayers`,见 §5.6)——这是自包含承诺的硬前提,不可后置。
5. **迁移默认运行产物落盘根**:把 host-control、checkpoint、pending steer、turn evidence、send artifacts 默认路径从 CWD `.tianshu/` / `.tianshu-cli/` 迁移到 `<TianShuHome>/runtime/.../<workspace-key>/`;保留 `--artifacts` 作为显式输出例外。
6. 修正 CLI AppHost 探测:新增"BaseDirectory 父目录(=包根)"探测根(见 §8)。
7. 调整 `init` / `doctor` 在便携模式下的语义和诊断;新增可写性探测与平台匹配检查(当前均无)。
8. 改造发布脚本,生成完整便携包(含 AppHost publish 到 `runtime/apphost/`、预置 config/modules)。
9. 改造 manifest / smoke / acceptance gate;扩 safety scan 到 archive 内容。
10. 执行 Windows 包 smoke,保留验收输出。
11. 根据 smoke 结果再同步 README、release 文档和公开发布说明。

## 14. 当前合同与后续增强边界

1. **self-contained 真值固定为 `true`。** 公开便携包中的 CLI 与 AppHost 都必须以 self-contained directory-style 发布,不得要求用户预装 .NET Runtime。若未来改为 framework-dependent,必须先修改 quickstart、release notes、manifest gate 和 package smoke。
2. **资产命名固定为 `tianshu-<version>-<rid>`。** Windows 为 `tianshu-<version>-win-x64.zip`;Linux/macOS 为 `tianshu-<version>-<rid>.tar.gz`。Manifest、CI、README 和 smoke 均以该命名为准。
3. **Config GUI 首版不进入便携包。** 当前公开便携包只承诺 CLI 与 AppHost。若未来加入 Config GUI,应放在 `bin/TianShu.ConfigGui.exe`,并复用同一包根解析与 `tianshu.toml`。
4. **Linux/macOS 验收级别。** 当前没有本机设备时,只声明 CI 构建、结构检查和 package smoke 覆盖;不得声称人工运行已验证。
5. **System 层隔离方式。** 便携模式由加载器识别包根后跳过宿主机 System 层,不依赖环境变量指路。
6. **便携判定信号。** 当前识别信号为 `<candidate>/tianshu.toml` 与 `<candidate>/modules/`。若后续发现源码树或用户目录误判风险,再增加包内 marker 文件,但不得破坏当前发布包的无环境变量启动合同。
