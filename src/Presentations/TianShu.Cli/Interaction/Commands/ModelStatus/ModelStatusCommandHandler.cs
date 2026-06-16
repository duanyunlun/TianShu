using TianShu.AppHost.Catalog;
using TianShu.AppHost.Configuration;
using TianShu.Configuration;
using TianShu.ControlPlane;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;
using TianShu.Cli.Interaction.Rendering;
using TianShu.Execution.Runtime;
using TianShu.Provider.Abstractions;
using TianShu.RuntimeComposition;

namespace TianShu.Cli.Interaction.Commands.ModelStatus;

/// <summary>
/// Handles `/model-route status` probing and protocol matrix orchestration.
/// 处理 `/model-route status` 的模型探测和协议矩阵编排。
/// </summary>
internal sealed class ModelStatusCommandHandler
{
    private const int ModelStatusMaxConcurrency = 4;
    internal const string DefaultReasoningProbePrompt =
        "事故分析：配置发布后，p95 延迟翻倍，缓存命中率从 94% 降到 61%，数据库 CPU 从 35% 升到 88%。请仔细判断最可能的根因，并用一句简洁中文回答。";

    private static readonly ProviderProbeProtocol[] ProbeProtocols =
    [
        new("openai_chat_completions", "openai_chat_completions"),
        new("openai_responses", "openai_responses"),
        new("anthropic_messages", "anthropic_messages"),
        new("google_generative", "google_generative"),
    ];

    private readonly ModelStatusTableRenderer renderer;
    private readonly Func<ChatCommandOptions, ResolvedTianShuConfig> loadResolvedConfig;
    private readonly Func<string?> getCurrentSessionThreadId;
    private readonly ModelStatusProviderProbeExecutor probeExecutor;

    public ModelStatusCommandHandler(
        ModelStatusTableRenderer renderer,
        Func<ChatCommandOptions, ResolvedTianShuConfig> loadResolvedConfig,
        Func<string?> getCurrentSessionThreadId,
        ModelStatusProviderProbeExecutor? probeExecutor = null)
    {
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        this.loadResolvedConfig = loadResolvedConfig ?? throw new ArgumentNullException(nameof(loadResolvedConfig));
        this.getCurrentSessionThreadId = getCurrentSessionThreadId ?? throw new ArgumentNullException(nameof(getCurrentSessionThreadId));
        this.probeExecutor = probeExecutor ?? (static (config, models, options, token) =>
            new ProviderModelConnectivityProbe().ProbeAsync(config, models, options, token));
    }

    public async Task HandleAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ModelStatusMode mode,
        ModelStatusCommandOutput output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        try
        {
            var commandCancellationToken = output.CancellationToken;
            var snapshot = await ResolveSnapshotAsync(runtime, options, commandCancellationToken).ConfigureAwait(false);
            var routeSet = TianShuModelRouteSetDefaults.ResolveRouteSet(snapshot.Config.RawConfig);
            var coverage = TianShuModelRouteSetDefaults.BuildRouteCoverage(routeSet, snapshot.RegisteredRouteKinds);
            var jobs = BuildProbeJobs(routeSet, snapshot, mode);
            var probeGroups = BuildProbeGroups(jobs);
            var protocols = mode == ModelStatusMode.Matrix ? ProbeProtocols : Array.Empty<ProviderProbeProtocol>();
            var styled = output.Styled;
            using var exclusiveFrameScope = output.BeginExclusiveFrameScope();
            output.WriteNoWrapLine(renderer.StyleTitle(
                mode == ModelStatusMode.Matrix ? "模型路由方案协议兼容矩阵" : "模型路由方案状态验收",
                styled));
            output.WriteNoWrapLine(renderer.StyleMeta(
                $"routeSet={routeSet.RouteSetId}  virtual={FormatBool(routeSet.IsVirtual)}  routes={routeSet.Routes.Count}  registeredRoutes={coverage.ConfiguredRegisteredRouteKinds.Count}/{coverage.RegisteredRouteKinds.Count}  stageModels={CountRouteStageProbeTargets(routeSet, snapshot.RegisteredRouteKinds)}  probeTargets={probeGroups.Length}",
                styled));
            if (coverage.MissingRegisteredRouteKinds.Count > 0 || coverage.UnknownRouteKinds.Count > 0)
            {
                output.WriteNoWrapLine(renderer.StyleMeta(
                    $"routeRegistry=missing[{string.Join(",", coverage.MissingRegisteredRouteKinds)}] unknown[{string.Join(",", coverage.UnknownRouteKinds)}]",
                    styled));
            }

            output.WriteNoWrapLine(renderer.StyleMeta(
                mode == ModelStatusMode.Matrix
                    ? "protocols=" + string.Join(", ", protocols.Select(static protocol => protocol.Id))
                    : "protocol=per-candidate resolved",
                styled));
            output.WriteNoWrapLine(renderer.BuildHeader(styled));

            if (jobs.Length == 0)
            {
                output.WriteNoWrapLine(renderer.BuildRow(
                    0,
                    "<none>",
                    "<none>",
                    ModelStatusProbeOutcome.Failed,
                    "-",
                    TimeSpan.Zero,
                    "没有可探测的模型。",
                    styled));
                return;
            }

            var total = jobs.Length;
            var succeeded = 0;
            for (var offset = 0; offset < probeGroups.Length; offset += ModelStatusMaxConcurrency)
            {
                commandCancellationToken.ThrowIfCancellationRequested();
                var batch = probeGroups
                    .Skip(offset)
                    .Take(ModelStatusMaxConcurrency)
                    .ToArray();
                succeeded += await ProbeBatchAsync(snapshot, batch, output, commandCancellationToken).ConfigureAwait(false);
            }

            output.WriteNoWrapLine(
                mode == ModelStatusMode.Matrix
                    ? $"汇总：{succeeded}/{total} 个模型协议组合连通。"
                    : $"汇总：{succeeded}/{total} 个路由阶段模型通过当前路由协议验收。");
        }
        catch (OperationCanceledException) when (output.IsUserCancellationRequested())
        {
        }
    }

    internal ModelStatusProbeJob[] BuildProbeJobs(
        TianShuModelRouteSetSnapshot routeSet,
        ModelStatusSnapshot snapshot,
        ModelStatusMode mode)
    {
        ArgumentNullException.ThrowIfNull(routeSet);
        ArgumentNullException.ThrowIfNull(snapshot);

        var protocols = mode == ModelStatusMode.Matrix ? ProbeProtocols : [];
        var candidates = ListRouteStageProbeTargets(routeSet, snapshot.RegisteredRouteKinds);
        var jobs = new List<ModelStatusProbeJob>(candidates.Length * Math.Max(protocols.Length, 1));
        var index = 0;
        foreach (var (routeKind, candidate) in candidates)
        {
            if (mode == ModelStatusMode.Development)
            {
                var resolvedProtocol = ResolveCandidateProtocol(snapshot, candidate);
                jobs.Add(new ModelStatusProbeJob(
                    ++index,
                    routeKind,
                    candidate.Provider,
                    candidate.Model,
                    new ProviderProbeProtocol(resolvedProtocol, resolvedProtocol)));
                continue;
            }

            foreach (var protocol in protocols)
            {
                jobs.Add(new ModelStatusProbeJob(++index, routeKind, candidate.Provider, candidate.Model, protocol));
            }
        }

        return jobs.ToArray();
    }

    internal static ModelStatusProbeGroup[] BuildProbeGroups(IReadOnlyList<ModelStatusProbeJob> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        return jobs
            .GroupBy(static job => ModelStatusProbeKey.FromJob(job))
            .Select(static group => new ModelStatusProbeGroup(group.Key, group.ToArray()))
            .ToArray();
    }

    private static (string RouteKind, TianShuModelRouteCandidateSnapshot Candidate)[] ListRouteStageProbeTargets(
        TianShuModelRouteSetSnapshot routeSet,
        IReadOnlyList<string> registeredRouteKinds)
        => registeredRouteKinds
            .Select(routeKind => (
                RouteKind: routeKind,
                Candidate: routeSet.Routes
                    .FirstOrDefault(route => string.Equals(route.Kind, routeKind, StringComparison.OrdinalIgnoreCase))
                    ?.Candidates.FirstOrDefault()))
            .Where(static target => target.Candidate is not null)
            .Select(static target => (target.RouteKind, target.Candidate!))
            .ToArray();

    private static int CountRouteStageProbeTargets(
        TianShuModelRouteSetSnapshot routeSet,
        IReadOnlyList<string> registeredRouteKinds)
        => ListRouteStageProbeTargets(routeSet, registeredRouteKinds).Length;

    internal bool IsReasoningProbeRequested(
        ModelStatusSnapshot snapshot,
        ProviderProbeProtocol protocol,
        string model)
    {
        if (snapshot.Config.ModelReasoningEnabled == false || IsLightweightOrNonTextModel(model))
        {
            return false;
        }

        return protocol.ConfigValue switch
        {
            ProviderWireApi.Responses => IsOpenAiReasoningModel(model),
            ProviderWireApi.OpenAiChatCompletions => IsOpenAiReasoningModel(model) || IsKnownOpenAiCompatibleReasoningModel(model),
            ProviderWireApi.AnthropicMessages => IsClaudeModel(model),
            ProviderWireApi.GoogleGenerative => IsGeminiReasoningModel(model),
            _ => false,
        };
    }

    internal static ModelStatusProbeOutcome ResolveProbeOutcome(ProviderModelConnectivityProbeItem? item)
    {
        if (item?.Succeeded == true)
        {
            return ModelStatusProbeOutcome.Succeeded;
        }

        var reason = item?.Reason ?? string.Empty;
        if (reason.Contains("尚未实现", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return ModelStatusProbeOutcome.Unavailable;
        }

        if (reason.Contains("Exception", StringComparison.OrdinalIgnoreCase)
            || item?.HttpStatusCode is null)
        {
            return ModelStatusProbeOutcome.Error;
        }

        return ModelStatusProbeOutcome.Failed;
    }

    internal static string FormatReasoningSignal(ProviderModelConnectivityProbeItem? item, bool reasoningRequested)
    {
        if (item is null || !item.Succeeded)
        {
            return "-";
        }

        if (item.HasReasoning)
        {
            return "可见";
        }

        return reasoningRequested ? "已请求" : "未观测";
    }

    internal static string FormatProbeError(ProviderModelConnectivityProbeItem? item)
    {
        if (item?.Succeeded == true)
        {
            return "-";
        }

        var httpStatus = item?.HttpStatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-";
        var path = string.IsNullOrWhiteSpace(item?.RequestPath) ? "-" : item!.RequestPath;
        var reason = Normalize(item?.Reason) ?? "无返回详情。";
        return $"http={httpStatus} path={path} {reason}";
    }

    internal async Task<ModelStatusSnapshot> ResolveSnapshotAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        CancellationToken cancellationToken)
    {
        var config = loadResolvedConfig(options);
        var threadId = getCurrentSessionThreadId();
        ControlPlaneThreadSessionConfiguration? sessionConfiguration = null;
        if (!string.IsNullOrWhiteSpace(threadId))
        {
            var read = await TianShuControlPlaneClientFactory.Create(runtime).Conversations.ReadThreadAsync(
                    new ControlPlaneReadThreadQuery
                    {
                        ThreadId = new ThreadId(threadId),
                        IncludeTurns = false,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            sessionConfiguration = read.Thread?.SessionConfiguration;
        }

        var registeredRouteKinds = ModelRouteRuntimeComposition.BuildRegisteredRouteKinds(config.RawConfig);
        var diagnostic = ModelRouteRuntimeComposition.BuildRouteDiagnostic(
            config.RawConfig,
            TianShuModelRouteSetDefaults.DefaultRouteKind,
            routeSetId: null);
        var routeCandidate = diagnostic.RouteSetIsVirtual ? null : diagnostic.PreferredCandidate;
        var model = Normalize(sessionConfiguration?.Model)
            ?? Normalize(options.RuntimeModel)
            ?? Normalize(routeCandidate?.Model)
            ?? "<config>";
        var provider = Normalize(sessionConfiguration?.ModelProvider)
            ?? Normalize(sessionConfiguration?.ModelProviderId)
            ?? Normalize(options.RuntimeModelProvider)
            ?? Normalize(routeCandidate?.Provider)
            ?? "<config>";
        var protocol = Normalize(sessionConfiguration?.ProviderWireApi)
            ?? Normalize(options.RuntimeProviderWireApi)
            ?? (routeCandidate is null ? null : ResolveCandidateProtocol(config, routeCandidate))
            ?? "<default>";
        var endpoint = Normalize(sessionConfiguration?.ProviderBaseUrl)
            ?? Normalize(config.ProviderBaseUrl)
            ?? "<default>";
        var apiKeyEnv = Normalize(sessionConfiguration?.ProviderApiKeyEnvironmentVariable)
            ?? Normalize(config.ProviderEnvKey)
            ?? "<unset>";

        return new ModelStatusSnapshot(
            model,
            provider,
            protocol,
            endpoint,
            apiKeyEnv,
            threadId,
            config,
            registeredRouteKinds);
    }

    private async Task<int> ProbeBatchAsync(
        ModelStatusSnapshot snapshot,
        IReadOnlyList<ModelStatusProbeGroup> batch,
        ModelStatusCommandOutput output,
        CancellationToken cancellationToken)
    {
        var probes = batch
            .SelectMany(group =>
            {
                var primaryJob = group.Jobs[0];
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var task = ProbeItemAsync(snapshot, primaryJob, cancellationToken);
                return group.Jobs.Select(job => new ModelStatusRunningProbe(
                    job,
                    stopwatch,
                    IsReasoningProbeRequested(snapshot, job.Protocol, job.Model),
                    task));
            })
            .ToArray();

        if (output.Styled)
        {
            using (output.HideCursorForTerminalRefresh())
            {
                output.WriteLiveRows(BuildProbeRows(probes, output.Styled), false);
                while (probes.Any(static probe => !probe.Task.IsCompleted))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                    output.WriteLiveRows(BuildProbeRows(probes, output.Styled), true);
                }

                output.WriteLiveRows(BuildProbeRows(probes, output.Styled), true);
            }
        }
        else
        {
            await Task.WhenAll(probes.Select(static probe => probe.Task)).ConfigureAwait(false);
            foreach (var probe in probes)
            {
                output.WriteFinalRow(BuildProbeRow(probe, output.Styled));
            }
        }

        var results = await Task.WhenAll(probes.Select(static probe => probe.Task)).ConfigureAwait(false);
        return results.Count(static result => result.Item?.Succeeded == true);
    }

    internal async Task<(ProviderModelConnectivityProbeItem? Item, TimeSpan Elapsed)> ProbeItemAsync(
        ModelStatusSnapshot snapshot,
        ModelStatusProbeJob job,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var promptConfiguration = TianShuPromptConfigUtilities.FromConfig(snapshot.Config.RawConfig);
        var probeConfig = BuildProbeConfig(snapshot, job, job.Protocol.ConfigValue);
        var probeOptions = new ProviderModelConnectivityProbeOptions
        {
            Prompt = promptConfiguration.ModelStatusReasoningProbePrompt ?? DefaultReasoningProbePrompt,
            Timeout = TimeSpan.FromSeconds(30),
        };
        var maxRetries = ResolveStatusProbeMaxRetries(snapshot.Config);
        for (var retryIndex = 0; ; retryIndex++)
        {
            ProviderModelConnectivityProbeItem? item;
            try
            {
                var result = await probeExecutor(probeConfig, [job.Model], probeOptions, cancellationToken).ConfigureAwait(false);
                item = result.Items.FirstOrDefault();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                item = ProviderModelConnectivityProbeItem.Failed(job.Model, null, null, $"{ex.GetType().Name}: {ex.Message}");
            }

            if (item?.Succeeded == true || !ShouldRetryStatusProbe(item) || retryIndex >= maxRetries)
            {
                stopwatch.Stop();
                if (retryIndex > 0 && item is { Succeeded: false })
                {
                    item = item with { Reason = $"重试 {retryIndex} 次后仍失败：{item.Reason}" };
                }

                return (item, stopwatch.Elapsed);
            }

            await Task.Delay(ComputeStatusProbeRetryDelay(retryIndex), cancellationToken).ConfigureAwait(false);
        }
    }

    internal static bool ShouldRetryStatusProbe(ProviderModelConnectivityProbeItem? item)
    {
        if (item is null)
        {
            return true;
        }

        if (item.Succeeded)
        {
            return false;
        }

        if (item.HttpStatusCode is >= 500 or 408 or 409 or 425 or 429)
        {
            return true;
        }

        if (item.HttpStatusCode is not null)
        {
            return false;
        }

        var reason = item.Reason ?? string.Empty;
        return reason.Contains("HttpRequestException", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("TaskCanceledException", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("IOException", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("timeout", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("temporar", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("连接", StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveStatusProbeMaxRetries(ResolvedTianShuConfig config)
    {
        var configured = config.ProviderStreamMaxRetries ?? config.ProviderRequestMaxRetries;
        return configured is null ? 0 : Math.Clamp((int)configured.Value, 0, 10);
    }

    private static TimeSpan ComputeStatusProbeRetryDelay(int retryIndex)
        => TimeSpan.FromMilliseconds(Math.Min(1000, 250 * (retryIndex + 1)));

    private string[] BuildProbeRows(IReadOnlyList<ModelStatusRunningProbe> probes, bool styled)
        => probes.Select(probe => BuildProbeRow(probe, styled)).ToArray();

    private string BuildProbeRow(ModelStatusRunningProbe probe, bool styled)
    {
        if (!probe.Task.IsCompletedSuccessfully)
        {
            return renderer.BuildRow(
                probe.Job.Index,
                FormatProbeModel(probe.Job),
                probe.Job.Protocol.Id,
                ModelStatusProbeOutcome.Running,
                "检测中",
                probe.Stopwatch.Elapsed,
                string.Empty,
                styled);
        }

        var (item, elapsed) = probe.Task.Result;
        return renderer.BuildRow(
            probe.Job.Index,
            FormatProbeModel(probe.Job),
            probe.Job.Protocol.Id,
            ResolveProbeOutcome(item),
            FormatReasoningSignal(item, probe.ReasoningRequested),
            elapsed,
            FormatProbeError(item),
            styled);
    }

    private static ResolvedTianShuConfig BuildProbeConfig(ModelStatusSnapshot snapshot, ModelStatusProbeJob job, string protocol)
        => new()
        {
            ConfigFilePath = snapshot.Config.ConfigFilePath,
            UserConfigPath = snapshot.Config.UserConfigPath,
            ActiveProfile = snapshot.Config.ActiveProfile,
            Model = job.Model,
            ModelProvider = job.Provider,
            ApprovalPolicy = snapshot.Config.ApprovalPolicy,
            SandboxMode = snapshot.Config.SandboxMode,
            WebSearchMode = snapshot.Config.WebSearchMode,
            ServiceTier = snapshot.Config.ServiceTier,
            ModelReasoningEnabled = snapshot.Config.ModelReasoningEnabled,
            ModelReasoningEffort = snapshot.Config.ModelReasoningEffort,
            ModelReasoningSummary = snapshot.Config.ModelReasoningSummary,
            ModelVerbosity = snapshot.Config.ModelVerbosity,
            ModelReasoningBudgetTokens = snapshot.Config.ModelReasoningBudgetTokens,
            ProviderBaseUrl = TianShuModelProviderConfigReader.ReadProviderBaseUrl(snapshot.Config.RawConfig, job.Provider)
                ?? (string.Equals(job.Provider, snapshot.Provider, StringComparison.OrdinalIgnoreCase) && snapshot.Endpoint != "<default>" ? snapshot.Endpoint : null)
                ?? snapshot.Config.ProviderBaseUrl,
            ProviderEnvKey = TianShuModelProviderConfigReader.ReadProviderApiKeyEnvironmentVariable(snapshot.Config.RawConfig, job.Provider)
                ?? (string.Equals(job.Provider, snapshot.Provider, StringComparison.OrdinalIgnoreCase) && snapshot.ApiKeyEnv != "<unset>" ? snapshot.ApiKeyEnv : null)
                ?? snapshot.Config.ProviderEnvKey,
            ProviderWireApi = protocol,
            ProviderRequestMaxRetries = snapshot.Config.ProviderRequestMaxRetries,
            ProviderStreamMaxRetries = snapshot.Config.ProviderStreamMaxRetries,
            ProviderStreamIdleTimeoutMs = snapshot.Config.ProviderStreamIdleTimeoutMs,
            ProviderWebsocketConnectTimeoutMs = snapshot.Config.ProviderWebsocketConnectTimeoutMs,
            ProviderSupportsWebsockets = snapshot.Config.ProviderSupportsWebsockets,
            ProtocolAdapter = snapshot.Config.ProtocolAdapter,
            RawConfig = snapshot.Config.RawConfig,
            Layers = snapshot.Config.Layers,
        };

    private static string ResolveCandidateProtocol(ModelStatusSnapshot snapshot, TianShuModelRouteCandidateSnapshot candidate)
        => ResolveCandidateProtocol(snapshot.Config, candidate);

    private static string ResolveCandidateProtocol(ResolvedTianShuConfig config, TianShuModelRouteCandidateSnapshot candidate)
    {
        var explicitProtocol = Normalize(candidate.Protocol);
        if (explicitProtocol is not null && !string.Equals(explicitProtocol, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return explicitProtocol;
        }

        return KernelModelProtocolResolver.ResolveModelProtocol(config.RawConfig, candidate.Provider, candidate.Model);
    }

    private static string FormatProbeModel(ModelStatusProbeJob job)
        => $"{job.RouteKind} {job.Provider}/{job.Model}";

    private static string FormatBool(bool value)
        => value ? "true" : "false";

    private static bool IsLightweightOrNonTextModel(string model)
        => model.Contains("flash", StringComparison.OrdinalIgnoreCase)
           || model.Contains("lite", StringComparison.OrdinalIgnoreCase)
           || model.Contains("image", StringComparison.OrdinalIgnoreCase)
           || model.Contains("embedding", StringComparison.OrdinalIgnoreCase)
           || model.Contains("audio", StringComparison.OrdinalIgnoreCase);

    private static bool IsOpenAiReasoningModel(string model)
        => model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase)
           || model.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
           || model.StartsWith("o3", StringComparison.OrdinalIgnoreCase)
           || model.StartsWith("o4", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownOpenAiCompatibleReasoningModel(string model)
        => model.Contains("qwen", StringComparison.OrdinalIgnoreCase)
           || model.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
           || model.Contains("kimi", StringComparison.OrdinalIgnoreCase)
           || model.Contains("claude", StringComparison.OrdinalIgnoreCase)
           || model.Contains("anthropic", StringComparison.OrdinalIgnoreCase)
           || model.Contains("minimax", StringComparison.OrdinalIgnoreCase)
           || model.Contains("mimo", StringComparison.OrdinalIgnoreCase)
           || model.Contains("glm", StringComparison.OrdinalIgnoreCase)
           || model.Contains("grok", StringComparison.OrdinalIgnoreCase);

    private static bool IsClaudeModel(string model)
        => model.Contains("claude", StringComparison.OrdinalIgnoreCase)
           || model.Contains("anthropic", StringComparison.OrdinalIgnoreCase);

    private static bool IsGeminiReasoningModel(string model)
        => model.Contains("gemini", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
