using TianShu.Contracts.Configuration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Tools;

namespace TianShu.Execution.Runtime;

internal sealed class ExecutionRuntimeOptions
{
    public bool UseDotNetProjectLauncher { get; set; } = true;

    public string ExecutablePath { get; set; } = "dotnet";

    public string? AppHostProjectPath { get; set; }

    public string? WorkingDirectory { get; set; }

    public string? AdditionalArguments { get; set; }

    public string? ConfigFilePath { get; set; }

    public string? ProfileName { get; set; }

    public bool ProfileNameResolvedFromConfig { get; set; }

    public IReadOnlyDictionary<string, string>? ConfigOverrides { get; set; }

    public string? Model { get; set; }

    public string? ModelProvider { get; set; }

    public AgentApprovalPolicy? ApprovalPolicy { get; set; }

    public string? SandboxMode { get; set; }

    public string? WebSearchMode { get; set; }

    public AgentServiceTierOverride ServiceTier { get; set; } = AgentServiceTierOverride.Unspecified;

    public string? ModelReasoningSummary { get; set; }

    public string? ModelVerbosity { get; set; }

    public string? CollaborationMode { get; set; }

    public ControlPlaneSessionSource? SessionSource { get; set; }

    public string? ProviderBaseUrl { get; set; }

    public string? ProviderApiKeyEnvironmentVariable { get; set; }

    public string? ProviderWireApi { get; set; }

    public long? ProviderRequestMaxRetries { get; set; }

    public long? ProviderStreamMaxRetries { get; set; }

    public long? ProviderStreamIdleTimeoutMs { get; set; }

    public long? ProviderWebsocketConnectTimeoutMs { get; set; }

    public bool? ProviderSupportsWebsockets { get; set; }

    public string ProtocolAdapter { get; set; } = "openai-responses";

    public string? ResumeThreadId { get; set; }

    public bool ResumeLatestThread { get; set; }

    public bool ResumeLatestMatchCwd { get; set; } = true;

    public int ResumeThreadListLimit { get; set; } = 20;

    public bool CreateThreadOnInitialize { get; set; } = true;

    public bool UseIsolatedSessionStorage { get; set; }

    public string? IsolatedSessionStorageRoot { get; set; }

    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(45);

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan TurnTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// runtime northbound 输入侧的动态工具定义，统一使用 conversations contracts。
    /// Dynamic tool definitions on the runtime northbound input boundary, unified on conversations contracts.
    /// </summary>
    public IReadOnlyList<ControlPlaneDynamicToolSpec>? DynamicTools { get; set; }

    /// <summary>
    /// 宿主侧动态工具调用回调，必须仅通过 Tools Contracts 交换 typed request/result。
    /// Host-side dynamic tool callback that must exchange typed request/result only through Tools Contracts.
    /// </summary>
    public Func<ToolInvocationRequest, CancellationToken, Task<ToolInvocationResult>>? DynamicToolCallHandler { get; set; }

    /// <summary>
    /// `exec --output-schema` 等 northbound 输入使用的结构化输出约束。
    /// Structured output schema used by northbound inputs such as `exec --output-schema`.
    /// </summary>
    public StructuredValue? OutputSchema { get; set; }

    /// <summary>
    /// 从 northbound 控制平面初始化命令构造 runtime 内部启动选项。
    /// Creates runtime-internal startup options from the northbound control-plane initialization command.
    /// </summary>
    public static ExecutionRuntimeOptions FromControlPlaneCommand(
        ControlPlaneInitializeRuntimeCommand command,
        Func<ToolInvocationRequest, CancellationToken, Task<ToolInvocationResult>>? dynamicToolCallHandler)
    {
        ArgumentNullException.ThrowIfNull(command);

        return new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = command.UseDotNetProjectLauncher,
            ExecutablePath = command.ExecutablePath,
            AppHostProjectPath = command.AppHostProjectPath,
            WorkingDirectory = command.WorkingDirectory,
            AdditionalArguments = command.AdditionalArguments,
            ConfigFilePath = command.ConfigFilePath,
            ProfileName = command.ProfileName,
            ProfileNameResolvedFromConfig = command.ProfileNameResolvedFromConfig,
            ConfigOverrides = command.ConfigOverrides,
            Model = command.Model,
            ModelProvider = command.ModelProvider,
            ApprovalPolicy = string.IsNullOrWhiteSpace(command.ApprovalPolicy) ? null : command.ApprovalPolicy,
            SandboxMode = command.SandboxMode,
            WebSearchMode = command.WebSearchMode,
            ServiceTier = string.IsNullOrWhiteSpace(command.ServiceTier) ? AgentServiceTierOverride.Unspecified : command.ServiceTier,
            ModelReasoningSummary = command.ModelReasoningSummary,
            ModelVerbosity = command.ModelVerbosity,
            CollaborationMode = command.CollaborationMode,
            SessionSource = command.SessionSource,
            ProviderBaseUrl = command.ProviderBaseUrl,
            ProviderApiKeyEnvironmentVariable = command.ProviderApiKeyEnvironmentVariable,
            ProviderWireApi = command.ProviderWireApi,
            ProviderRequestMaxRetries = command.ProviderRequestMaxRetries,
            ProviderStreamMaxRetries = command.ProviderStreamMaxRetries,
            ProviderStreamIdleTimeoutMs = command.ProviderStreamIdleTimeoutMs,
            ProviderWebsocketConnectTimeoutMs = command.ProviderWebsocketConnectTimeoutMs,
            ProviderSupportsWebsockets = command.ProviderSupportsWebsockets,
            ProtocolAdapter = command.ProtocolAdapter,
            ResumeThreadId = command.ResumeThreadId,
            ResumeLatestThread = command.ResumeLatestThread,
            ResumeLatestMatchCwd = command.ResumeLatestMatchCwd,
            ResumeThreadListLimit = command.ResumeThreadListLimit,
            CreateThreadOnInitialize = command.CreateThreadOnInitialize,
            UseIsolatedSessionStorage = command.UseIsolatedSessionStorage,
            IsolatedSessionStorageRoot = command.IsolatedSessionStorageRoot,
            StartupTimeout = command.StartupTimeout,
            RequestTimeout = command.RequestTimeout,
            TurnTimeout = command.TurnTimeout,
            DynamicTools = command.DynamicTools,
            DynamicToolCallHandler = dynamicToolCallHandler,
            OutputSchema = command.OutputSchema,
        };
    }

    /// <summary>
    /// 解析 runtime 默认使用的 `tianshu.toml` 路径。
    /// Resolves the default `tianshu.toml` path used by the runtime.
    /// </summary>
    public static string ResolveDefaultConfigFilePath()
        => TianShuRuntimeLayoutPaths.ResolveTianShuConfigFilePath();

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}



