using System.Text.Json;
using TianShu.ControlPlane;
using TianShu.AppHost.Configuration;
using TianShu.Configuration;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime;
using TianShu.Cli.Interaction.Commands.ModelStatus;
using TianShu.Cli.Terminal;
using TianShu.RuntimeComposition;

namespace TianShu.Cli.Interaction.Commands.Model;

/// <summary>
/// Handles interactive model route set commands while delegating status probes to the dedicated handler.
/// 处理交互式模型路由方案命令，并把状态验收委托给专用 handler。
/// </summary>
internal sealed class InteractiveModelCommandHandler
{
    public async Task HandleModelCommandAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        string rest,
        InteractiveModelCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);

        var requestedRouteSet = Normalize(rest);
        if (TryParseModelStatusCommand(requestedRouteSet, out var statusMode))
        {
            await context.HandleModelStatusAsync(statusMode, cancellationToken).ConfigureAwait(false);
            return;
        }

        var config = LoadResolvedConfig(options, context);
        if (config is null)
        {
            return;
        }

        if (requestedRouteSet is not null)
        {
            await SelectModelRouteSetAsync(runtime, options, config, requestedRouteSet, context, cancellationToken).ConfigureAwait(false);
            return;
        }

        var registeredRouteKinds = ResolveRegisteredRouteKinds(config);
        var routeSets = ListModelRouteSets(config, registeredRouteKinds);

        if (context.ShouldUseInteractivePicker() && routeSets.Length > 0)
        {
            var selectedRouteSet = await TrySelectRouteSetWithTianShuTerminalAsync(
                    routeSets,
                    cancellationToken,
                    context.BeginExclusiveFrameScope)
                .ConfigureAwait(false);
            if (selectedRouteSet is null)
            {
                return;
            }

            await SelectModelRouteSetAsync(runtime, options, config, selectedRouteSet.RouteSetId, context, cancellationToken).ConfigureAwait(false);
            return;
        }

        var activeRouteSetId = TianShuModelRouteSetDefaults.ResolveActiveRouteSetId(config.RawConfig);
        context.WriteControlPlaneLine($"当前模型路由方案：{activeRouteSetId}", false);
        context.WriteControlPlaneLine("用法：/model-route <route-set>", false);
        context.WriteControlPlaneLine("诊断：/model-route status [--matrix]", false);
        if (routeSets.Length == 0)
        {
            context.WriteControlPlaneLine("当前没有可展示的模型路由方案。请先在 ConfigGUI 中创建或选择模型路由方案。", false);
            return;
        }

        context.WriteControlPlaneLine("可用模型路由方案：", false);
        foreach (var routeSet in routeSets)
        {
            var marker = routeSet.IsActive ? "*" : "-";
            var registryStatus = routeSet.MissingRegisteredRouteCount == 0 && routeSet.UnknownRouteCount == 0
                ? "ok"
                : $"missing={routeSet.MissingRegisteredRouteCount} unknown={routeSet.UnknownRouteCount}";
            context.WriteControlPlaneLine($"  {marker} {routeSet.RouteSetId}  {routeSet.DisplayName}  routes={routeSet.RouteCount} registeredRoutes={routeSet.ConfiguredRegisteredRouteCount}/{routeSet.RegisteredRouteCount} candidates={routeSet.CandidateCount} registry={registryStatus}", false);
        }
    }

    private static ResolvedTianShuConfig? LoadResolvedConfig(
        ChatCommandOptions options,
        InteractiveModelCommandContext context)
    {
        try
        {
            return context.LoadResolvedConfig(options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            context.WriteControlPlaneLine($"读取模型路由方案失败：{ex.Message}", true);
            return null;
        }
    }

    private static async Task SelectModelRouteSetAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ResolvedTianShuConfig config,
        string routeSetId,
        InteractiveModelCommandContext context,
        CancellationToken cancellationToken)
    {
        if (!TryResolveConfiguredRouteSet(config, routeSetId, out var routeSet))
        {
            context.WriteControlPlaneLine($"未找到模型路由方案：{routeSetId}", true);
            return;
        }

        var currentThreadId = context.GetCurrentThreadId();
        if (currentThreadId is not null)
        {
            if (context.HasRunningConversation())
            {
                context.WriteControlPlaneLine("当前回合仍在运行，请先 /wait-complete 或 /interrupt 后再切换模型路由方案。", true);
                return;
            }

            var persistence = await PersistSelectedRouteSetAsync(runtime, options, routeSet.RouteSetId, cancellationToken).ConfigureAwait(false);
            var resumed = await TianShuControlPlaneClientFactory.Create(runtime).Conversations.ResumeThreadAsync(
                    new ControlPlaneResumeThreadCommand
                    {
                        ThreadId = new ThreadId(currentThreadId),
                        WorkingDirectory = options.WorkingDirectory,
                        Configuration = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                        {
                            ["model_route_set"] = StructuredValue.FromString(routeSet.RouteSetId),
                        },
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (resumed is null)
            {
                context.WriteControlPlaneLine($"切换模型路由方案失败：{routeSet.RouteSetId}", true);
                return;
            }

            options.RuntimeModel = null;
            options.RuntimeModelProvider = Normalize(resumed.Thread.SessionConfiguration?.ModelProvider)
                ?? Normalize(resumed.Thread.SessionConfiguration?.ModelProviderId)
                ?? Normalize(resumed.Thread.ModelProvider)
                ?? options.RuntimeModelProvider;
            context.SetSessionActiveThreadId(resumed.Thread.ThreadId.Value);
            context.SetCurrentDisplayModel(null);
            context.MarkTerminalTurn();
            var resumedRouteSetId = Normalize(resumed.Thread.SessionConfiguration?.ModelRouteSetId) ?? routeSet.RouteSetId;
            context.WriteControlPlaneLine($"已切换模型路由方案：{resumedRouteSetId}{persistence.FormatSuffix()}", false);
            return;
        }

        options.RuntimeModel = null;
        var persisted = await PersistSelectedRouteSetAsync(runtime, options, routeSet.RouteSetId, cancellationToken).ConfigureAwait(false);
        context.SetCurrentDisplayModel(null);
        context.WriteControlPlaneLine($"已选择模型路由方案：{routeSet.RouteSetId}{persisted.FormatSuffix()}。下一条消息会用该路由方案创建线程。", false);
    }

    private static async Task<ModelSelectionPersistenceResult> PersistSelectedRouteSetAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        string routeSetId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await TianShuControlPlaneClientFactory.Create(runtime).Catalog.WriteConfigBatchAsync(
                    new ControlPlaneConfigBatchWriteCommand
                    {
                        Items =
                        [
                            new ControlPlaneConfigWriteItem
                            {
                                KeyPath = ResolvePersistedRouteSetKeyPath(options),
                                Value = StructuredValue.FromString(routeSetId),
                            },
                        ],
                        WorkingDirectory = options.WorkingDirectory,
                        ReloadUserConfig = true,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return ModelSelectionPersistenceResult.From(result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException)
        {
            return ModelSelectionPersistenceResult.Failed(ex.Message);
        }
    }

    private static string ResolvePersistedRouteSetKeyPath(ChatCommandOptions options)
    {
        var activeProfile = Normalize(options.ProfileName);
        try
        {
            activeProfile = Normalize(new RuntimeConfigurationComposition()
                .Load(options.ConfigFilePath, options.ProfileName, options.ConfigOverrides, options.WorkingDirectory)
                .ActiveProfile) ?? activeProfile;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            // 配置读取失败时退回根 model_route_set；真实写入仍会经过 Config Plane 校验。
        }

        return string.IsNullOrWhiteSpace(activeProfile) ? "model_route_set" : $"profiles.{activeProfile}.model_route_set";
    }

    private static bool TryParseModelStatusCommand(string? value, out ModelStatusMode mode)
    {
        mode = ModelStatusMode.Development;
        var tokens = SplitCommandTokens(value);
        if (tokens.Length == 0
            || !string.Equals(tokens[0], "status", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (tokens.Length == 1)
        {
            return true;
        }

        if (tokens.Length == 2 && string.Equals(tokens[1], "--matrix", StringComparison.OrdinalIgnoreCase))
        {
            mode = ModelStatusMode.Matrix;
            return true;
        }

        return false;
    }

    private static string[] SplitCommandTokens(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static RouteSetChoice[] ListModelRouteSets(
        ResolvedTianShuConfig config,
        IReadOnlyList<string> registeredRouteKinds)
    {
        var activeRouteSetId = TianShuModelRouteSetDefaults.ResolveActiveRouteSetId(config.RawConfig);
        var routeSetIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (TryReadObjectExact(config.RawConfig, "model_route_sets", out var routeSets))
        {
            foreach (var routeSetId in routeSets.Keys)
            {
                if (!string.IsNullOrWhiteSpace(routeSetId))
                {
                    routeSetIds.Add(routeSetId);
                }
            }
        }

        foreach (var key in config.RawConfig.Keys)
        {
            var parts = key.Split('.');
            if (parts.Length >= 3
                && string.Equals(parts[0], "model_route_sets", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(parts[1]))
            {
                routeSetIds.Add(parts[1]);
            }
        }

        if (!string.IsNullOrWhiteSpace(activeRouteSetId))
        {
            routeSetIds.Add(activeRouteSetId);
        }

        return routeSetIds
            .Select(routeSetId => ToRouteSetChoice(config, routeSetId, activeRouteSetId, registeredRouteKinds))
            .Where(static routeSet => routeSet.RouteCount > 0)
            .OrderByDescending(static routeSet => routeSet.IsActive)
            .ThenBy(static routeSet => routeSet.RouteSetId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryReadObjectExact(
        Dictionary<string, object?> config,
        string propertyName,
        out Dictionary<string, object?> value)
    {
        if (config.TryGetValue(propertyName, out var rawValue)
            && TryAsDictionary(rawValue, out value))
        {
            return true;
        }

        value = null!;
        return false;
    }

    private static bool TryAsDictionary(object? value, out Dictionary<string, object?> dictionary)
    {
        switch (value)
        {
            case Dictionary<string, object?> concrete:
                dictionary = concrete;
                return true;
            case IReadOnlyDictionary<string, object?> readOnly:
                dictionary = readOnly.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case IDictionary<string, object?> mutable:
                dictionary = mutable.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Object:
                dictionary = element.EnumerateObject().ToDictionary(
                    static property => property.Name,
                    static property => JsonSerializer.Deserialize<object?>(property.Value.GetRawText()),
                    StringComparer.Ordinal);
                return true;
            default:
                dictionary = null!;
                return false;
        }
    }

    private static RouteSetChoice ToRouteSetChoice(
        ResolvedTianShuConfig config,
        string routeSetId,
        string activeRouteSetId,
        IReadOnlyList<string> registeredRouteKinds)
    {
        var routeSet = TianShuModelRouteSetDefaults.ResolveRouteSet(config.RawConfig, routeSetId);
        var coverage = TianShuModelRouteSetDefaults.BuildRouteCoverage(routeSet, registeredRouteKinds);
        return new RouteSetChoice(
            routeSet.RouteSetId,
            routeSet.DisplayName,
            routeSet.Routes.Count,
            routeSet.Routes.Sum(static route => route.Candidates.Count),
            string.Equals(routeSet.RouteSetId, activeRouteSetId, StringComparison.OrdinalIgnoreCase),
            coverage.RegisteredRouteKinds.Count,
            coverage.ConfiguredRegisteredRouteKinds.Count,
            coverage.MissingRegisteredRouteKinds.Count,
            coverage.UnknownRouteKinds.Count);
    }

    private static IReadOnlyList<string> ResolveRegisteredRouteKinds(ResolvedTianShuConfig config)
        => ModelRouteRuntimeComposition.BuildRegisteredRouteKinds(config.RawConfig);

    private static bool TryResolveConfiguredRouteSet(
        ResolvedTianShuConfig config,
        string routeSetId,
        out TianShuModelRouteSetSnapshot routeSet)
    {
        routeSet = TianShuModelRouteSetDefaults.ResolveRouteSet(config.RawConfig, routeSetId);
        return routeSet.Routes.Count > 0 && !routeSet.IsVirtual;
    }

    private static async Task<RouteSetChoice?> TrySelectRouteSetWithTianShuTerminalAsync(
        IReadOnlyList<RouteSetChoice> routeSets,
        CancellationToken cancellationToken,
        Func<IDisposable>? beginExclusiveFrameScope)
    {
        if (routeSets.Count == 0)
        {
            return null;
        }

        var rows = routeSets
            .Select(static routeSet => $"{routeSet.RouteSetId}  {routeSet.DisplayName}  routes={routeSet.RouteCount} registeredRoutes={routeSet.ConfiguredRegisteredRouteCount}/{routeSet.RegisteredRouteCount} candidates={routeSet.CandidateCount}")
            .ToArray();
        int? selectedIndex;
        try
        {
            selectedIndex = await new TerminalSelectionPicker(beginExclusiveFrameScope)
                .SelectAsync(rows, "选择模型路由方案", cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            selectedIndex = null;
        }

        if (selectedIndex is null || selectedIndex < 0 || selectedIndex >= routeSets.Count)
        {
            return null;
        }

        return routeSets[selectedIndex.Value];
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record RouteSetChoice(
        string RouteSetId,
        string DisplayName,
        int RouteCount,
        int CandidateCount,
        bool IsActive,
        int RegisteredRouteCount,
        int ConfiguredRegisteredRouteCount,
        int MissingRegisteredRouteCount,
        int UnknownRouteCount);

    private sealed record ModelSelectionPersistenceResult(bool Saved, bool Overridden, string? Error)
    {
        public static ModelSelectionPersistenceResult From(ControlPlaneConfigWriteResult result)
            => new(true, result.IsOverridden, null);

        public static ModelSelectionPersistenceResult Failed(string error)
            => new(false, false, error);

        public string FormatSuffix()
        {
            if (!Saved)
            {
                return $"，但保存默认配置失败：{Error}";
            }

            return Overridden
                ? "，已写入用户级默认配置；注意：更高优先级配置层可能仍会覆盖重启默认模型"
                : "，已写入用户级默认配置";
        }
    }
}

internal sealed record InteractiveModelCommandContext(
    Func<ModelStatusMode, CancellationToken, Task> HandleModelStatusAsync,
    Func<ChatCommandOptions, ResolvedTianShuConfig> LoadResolvedConfig,
    Func<bool> ShouldUseInteractivePicker,
    Func<bool> HasRunningConversation,
    Func<string?> GetCurrentThreadId,
    Action<string?> SetSessionActiveThreadId,
    Action<string?> SetCurrentDisplayModel,
    Action MarkTerminalTurn,
    Action<string, bool> WriteControlPlaneLine,
    Func<IDisposable>? BeginExclusiveFrameScope);
