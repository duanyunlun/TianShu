using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Sessions;

/// <summary>
/// 控制平面运行时初始化命令，定义宿主向执行运行时注入的 northbound 启动参数。
/// Control-plane runtime initialization command that defines the northbound startup parameters injected by the host.
/// </summary>
public sealed record ControlPlaneInitializeRuntimeCommand
{
    /// <summary>
    /// 是否优先通过 `dotnet <project>.csproj` 启动内核。
    /// Whether the kernel should be launched via `dotnet <project>.csproj`.
    /// </summary>
    public bool UseDotNetProjectLauncher { get; set; } = true;

    /// <summary>
    /// 启动运行时所使用的可执行文件路径。
    /// Executable path used to launch the runtime.
    /// </summary>
    public string ExecutablePath { get; set; } = "dotnet";

    /// <summary>
    /// AppHost 宿主项目路径；当使用项目启动模式时应指向 `.csproj`。
    /// AppHost project path; when project-launch mode is used it should point to the `.csproj`.
    /// </summary>
    public string? AppHostProjectPath { get; set; }

    /// <summary>
    /// 运行时工作目录。
    /// Working directory used by the runtime.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// 追加到启动命令后的附加参数。
    /// Additional arguments appended to the launch command.
    /// </summary>
    public string? AdditionalArguments { get; set; }

    /// <summary>
    /// 配置文件路径。
    /// Configuration file path.
    /// </summary>
    public string? ConfigFilePath { get; set; }

    /// <summary>
    /// 期望激活的配置 profile 名称。
    /// Requested configuration profile name.
    /// </summary>
    public string? ProfileName { get; set; }

    /// <summary>
    /// profile 名称是否由配置解析而来，而不是调用方显式指定。
    /// Whether the profile name was resolved from configuration instead of being explicitly provided by the caller.
    /// </summary>
    public bool ProfileNameResolvedFromConfig { get; set; }

    /// <summary>
    /// 会话级配置覆盖项。
    /// Session-scoped configuration overrides.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ConfigOverrides { get; set; }

    /// <summary>
    /// 显式指定的模型标识。
    /// Explicit model identifier.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 显式指定的模型提供方标识。
    /// Explicit model-provider identifier.
    /// </summary>
    public string? ModelProvider { get; set; }

    /// <summary>
    /// 审批策略标识。
    /// Approval-policy identifier.
    /// </summary>
    public string? ApprovalPolicy { get; set; }

    /// <summary>
    /// 沙箱模式标识。
    /// Sandbox-mode identifier.
    /// </summary>
    public string? SandboxMode { get; set; }

    /// <summary>
    /// Web 搜索模式标识。
    /// Web-search mode identifier.
    /// </summary>
    public string? WebSearchMode { get; set; }

    /// <summary>
    /// 服务层级标识。
    /// Service-tier identifier.
    /// </summary>
    public string? ServiceTier { get; set; }

    /// <summary>
    /// 推理摘要策略。
    /// Reasoning-summary policy.
    /// </summary>
    public string? ModelReasoningSummary { get; set; }

    /// <summary>
    /// 模型输出冗长度策略。
    /// Model verbosity policy.
    /// </summary>
    public string? ModelVerbosity { get; set; }

    /// <summary>
    /// 协作模式标识。
    /// Collaboration-mode identifier.
    /// </summary>
    public string? CollaborationMode { get; set; }

    /// <summary>
    /// 会话来源标识。
    /// Session-source identifier.
    /// </summary>
    public ControlPlaneSessionSource? SessionSource { get; set; }

    /// <summary>
    /// 提供方基础地址。
    /// Provider base URL.
    /// </summary>
    public string? ProviderBaseUrl { get; set; }

    /// <summary>
    /// 提供方 API Key 对应的环境变量名。
    /// Environment-variable name that stores the provider API key.
    /// </summary>
    public string? ProviderApiKeyEnvironmentVariable { get; set; }

    /// <summary>
    /// 提供方线协议类型。
    /// Provider wire-protocol kind.
    /// </summary>
    public string? ProviderWireApi { get; set; }

    /// <summary>
    /// 提供方普通请求最大重试次数。
    /// Maximum retry count for provider request calls.
    /// </summary>
    public long? ProviderRequestMaxRetries { get; set; }

    /// <summary>
    /// 提供方流式请求最大重试次数。
    /// Maximum retry count for provider streaming calls.
    /// </summary>
    public long? ProviderStreamMaxRetries { get; set; }

    /// <summary>
    /// 提供方流式空闲超时（毫秒）。
    /// Provider stream idle timeout in milliseconds.
    /// </summary>
    public long? ProviderStreamIdleTimeoutMs { get; set; }

    /// <summary>
    /// 提供方 websocket 连接超时（毫秒）。
    /// Provider websocket connect timeout in milliseconds.
    /// </summary>
    public long? ProviderWebsocketConnectTimeoutMs { get; set; }

    /// <summary>
    /// 提供方是否支持 websocket 传输。
    /// Whether the provider supports websocket transport.
    /// </summary>
    public bool? ProviderSupportsWebsockets { get; set; }

    /// <summary>
    /// 运行时 southbound 协议适配器标识。
    /// Southbound protocol-adapter identifier used by the runtime.
    /// </summary>
    public string ProtocolAdapter { get; set; } = "openai-responses";

    /// <summary>
    /// 显式恢复的线程标识。
    /// Explicit thread identifier to resume.
    /// </summary>
    public string? ResumeThreadId { get; set; }

    /// <summary>
    /// 是否在初始化时自动恢复最近线程。
    /// Whether initialization should automatically resume the latest thread.
    /// </summary>
    public bool ResumeLatestThread { get; set; }

    /// <summary>
    /// 自动恢复最近线程时是否要求匹配当前工作目录。
    /// Whether auto-resume should be restricted to the current working directory.
    /// </summary>
    public bool ResumeLatestMatchCwd { get; set; } = true;

    /// <summary>
    /// 自动恢复线程时用于扫描候选线程的上限。
    /// Upper bound used when scanning candidate threads for auto-resume.
    /// </summary>
    public int ResumeThreadListLimit { get; set; } = 20;

    /// <summary>
    /// 初始化完成后是否自动创建线程。
    /// Whether a thread should be created automatically after initialization.
    /// </summary>
    public bool CreateThreadOnInitialize { get; set; } = true;

    /// <summary>
    /// 是否启用隔离会话存储。
    /// Whether isolated session storage should be enabled.
    /// </summary>
    public bool UseIsolatedSessionStorage { get; set; }

    /// <summary>
    /// 隔离会话存储根目录。
    /// Root directory for isolated session storage.
    /// </summary>
    public string? IsolatedSessionStorageRoot { get; set; }

    /// <summary>
    /// 启动阶段超时时间。
    /// Timeout used during startup.
    /// </summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>
    /// 普通 RPC 请求超时时间。
    /// Timeout used for regular RPC requests.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// turn 生命周期超时时间。
    /// Timeout used for an end-to-end turn lifecycle.
    /// </summary>
    public TimeSpan TurnTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 宿主注入的动态工具定义。
    /// Dynamic-tool definitions injected by the host.
    /// </summary>
    public IReadOnlyList<ControlPlaneDynamicToolSpec>? DynamicTools { get; set; }

    /// <summary>
    /// 宿主注入的结构化输出约束。
    /// Structured-output schema injected by the host.
    /// </summary>
    public StructuredValue? OutputSchema { get; set; }
}
