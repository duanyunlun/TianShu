using TianShu.AppHost.Catalog;
using TianShu.Configuration;
using TianShu.Provider.Abstractions;

namespace TianShu.Cli.Interaction.Commands.ModelStatus;

/// <summary>
/// `/model-route status` 的运行模式。
/// Execution mode for `/model-route status`.
/// </summary>
internal enum ModelStatusMode
{
    Development = 0,
    Matrix = 1,
}

/// <summary>
/// 一次模型探测使用的展示协议和配置协议。
/// Display and configuration protocol for one model probe.
/// </summary>
internal sealed record ProviderProbeProtocol(string Id, string ConfigValue);

/// <summary>
/// 一个待执行的 route stage 首选模型 / 协议探测任务。
/// A scheduled route stage preferred model/protocol probe job.
/// </summary>
internal sealed record ModelStatusProbeJob(
    int Index,
    string RouteKind,
    string Provider,
    string Model,
    ProviderProbeProtocol Protocol);

/// <summary>
/// 一个实际需要访问 provider 的唯一探测目标。
/// Unique provider probe target shared by one or more route stage rows.
/// </summary>
internal sealed record ModelStatusProbeGroup(
    ModelStatusProbeKey Key,
    IReadOnlyList<ModelStatusProbeJob> Jobs);

/// <summary>
/// Provider 探测去重键：同一 provider / model / protocol 只需要真实探测一次。
/// Provider probe de-duplication key: the same provider/model/protocol only needs one real request.
/// </summary>
internal readonly record struct ModelStatusProbeKey(
    string Provider,
    string Model,
    string ProtocolId,
    string ProtocolConfigValue)
{
    public static ModelStatusProbeKey FromJob(ModelStatusProbeJob job)
        => new(
            Normalize(job.Provider),
            Normalize(job.Model),
            Normalize(job.Protocol.Id),
            Normalize(job.Protocol.ConfigValue));

    private static string Normalize(string value)
        => value.Trim().ToLowerInvariant();
}

/// <summary>
/// 一个正在执行的模型探测任务及其实时状态。
/// A running model probe and its live state.
/// </summary>
internal sealed record ModelStatusRunningProbe(
    ModelStatusProbeJob Job,
    System.Diagnostics.Stopwatch Stopwatch,
    bool ReasoningRequested,
    Task<(ProviderModelConnectivityProbeItem? Item, TimeSpan Elapsed)> Task);

/// <summary>
/// Provider 连通性探测执行端口，便于命令层测试去重与重试，不改变真实执行默认路径。
/// Provider connectivity probe execution port used to test command-level de-duplication and retry without changing the real default path.
/// </summary>
internal delegate Task<ProviderModelConnectivityProbeResult> ModelStatusProviderProbeExecutor(
    ResolvedTianShuConfig config,
    IReadOnlyList<string> models,
    ProviderModelConnectivityProbeOptions options,
    CancellationToken cancellationToken);

/// <summary>
/// `/model-route status` 执行时解析出的模型、provider 和配置快照。
/// Resolved model, provider, and configuration snapshot for `/model-route status`.
/// </summary>
internal sealed record ModelStatusSnapshot(
    string Model,
    string Provider,
    string Protocol,
    string Endpoint,
    string ApiKeyEnv,
    string? ThreadId,
    ResolvedTianShuConfig Config,
    IReadOnlyList<string> RegisteredRouteKinds);

/// <summary>
/// 命令处理器写入终端输出的端口，避免 handler 直接依赖 Console。
/// Output port used by the command handler so it does not depend directly on Console.
/// </summary>
internal sealed record ModelStatusCommandOutput(
    bool Styled,
    Action<string> WriteNoWrapLine,
    Func<IDisposable> HideCursorForTerminalRefresh,
    Action<IReadOnlyList<string>, bool> WriteLiveRows,
    Action<string> WriteFinalRow,
    Func<IDisposable> BeginExclusiveFrameScope,
    CancellationToken CancellationToken,
    Func<bool> IsUserCancellationRequested);
