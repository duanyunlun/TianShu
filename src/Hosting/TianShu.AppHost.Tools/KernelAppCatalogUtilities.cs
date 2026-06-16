using TianShu.Contracts.Catalog;
using TianShu.Contracts.Primitives;

namespace TianShu.AppHost.Tools;

/// <summary>
/// app/list surface 与 connector 目录合并辅助件。
/// Helpers for app-list surface shaping and connector catalog merging.
/// </summary>
internal static class KernelAppCatalogUtilities
{
    public static KernelAppConfigState BuildAppConfigState(IReadOnlyDictionary<string, string> values)
    {
        var enabledStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var accessibleStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            if (!pair.Key.StartsWith("apps.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var segments = pair.Key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length < 3 || !string.Equals(segments[2], "enabled", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(segments[2], "isAccessible", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            var appId = segments[1];
            var enabled = bool.TryParse(pair.Value.Trim((char)34), out var parsed) && parsed;
            if (string.Equals(segments[2], "enabled", StringComparison.OrdinalIgnoreCase))
            {
                enabledStates[appId] = enabled;
            }
            else
            {
                accessibleStates[appId] = enabled;
            }
        }

        return new KernelAppConfigState(enabledStates, accessibleStates);
    }

    public static List<ControlPlaneAppDescriptor> BuildAppsFromConfig(
        KernelAppConfigState configState,
        IReadOnlyDictionary<string, IReadOnlyList<string>> pluginDisplayNames,
        IReadOnlyList<string> pluginAppIds)
    {
        var appIds = new HashSet<string>(configState.EnabledStates.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var appId in configState.AccessibleStates.Keys)
        {
            appIds.Add(appId);
        }

        foreach (var appId in pluginAppIds)
        {
            appIds.Add(appId);
        }

        return appIds
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Select(appId => CreateAppDescriptor(
                id: appId,
                name: appId,
                description: null,
                logoUrl: null,
                logoUrlDark: null,
                distributionChannel: null,
                branding: null,
                metadata: null,
                labels: null,
                installUrl: null,
                isAccessible: configState.AccessibleStates.TryGetValue(appId, out var isAccessible) && isAccessible || pluginAppIds.Contains(appId, StringComparer.OrdinalIgnoreCase),
                isEnabled: !configState.EnabledStates.TryGetValue(appId, out var isEnabled) || isEnabled,
                pluginDisplayNames: ResolvePluginDisplayNames(appId, pluginDisplayNames, Array.Empty<string>())))
            .ToList();
    }

    public static List<ControlPlaneAppDescriptor> BuildAppsFromConnectors(
        IReadOnlyList<KernelPluginConnectorInfo>? directoryConnectors,
        IReadOnlyList<KernelPluginConnectorInfo>? accessibleConnectors,
        KernelAppConfigState configState,
        IReadOnlyDictionary<string, IReadOnlyList<string>> pluginDisplayNames,
        IReadOnlyList<string> pluginAppIds)
    {
        var directoryMap = new Dictionary<string, KernelPluginConnectorInfo>(StringComparer.OrdinalIgnoreCase);
        if (directoryConnectors is not null)
        {
            foreach (var connector in directoryConnectors)
            {
                directoryMap[connector.Id] = connector;
            }
        }

        var accessibleMap = new Dictionary<string, KernelPluginConnectorInfo>(StringComparer.OrdinalIgnoreCase);
        if (accessibleConnectors is not null)
        {
            foreach (var connector in accessibleConnectors)
            {
                accessibleMap[connector.Id] = connector;
            }
        }

        var merged = new Dictionary<string, ControlPlaneAppDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in directoryMap.OrderBy(static item => item.Value.Name, StringComparer.OrdinalIgnoreCase).ThenBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var connector = accessibleMap.TryGetValue(pair.Key, out var accessibleConnector)
                ? MergeConnector(pair.Value, accessibleConnector)
                : pair.Value;
            merged[pair.Key] = CreateAppDescriptor(
                id: connector.Id,
                name: connector.Name,
                description: connector.Description,
                logoUrl: connector.LogoUrl,
                logoUrlDark: connector.LogoUrlDark,
                distributionChannel: connector.DistributionChannel,
                branding: connector.Branding,
                metadata: connector.AppMetadata,
                labels: connector.Labels,
                installUrl: connector.InstallUrl,
                isAccessible: accessibleMap.ContainsKey(pair.Key),
                isEnabled: !configState.EnabledStates.TryGetValue(pair.Key, out var isEnabled) || isEnabled,
                pluginDisplayNames: ResolvePluginDisplayNames(pair.Key, pluginDisplayNames, connector.PluginDisplayNames));
        }

        if (directoryConnectors is null)
        {
            foreach (var pair in accessibleMap.OrderBy(static item => item.Value.Name, StringComparer.OrdinalIgnoreCase).ThenBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (merged.ContainsKey(pair.Key))
                {
                    continue;
                }

                var connector = pair.Value;
                merged[pair.Key] = CreateAppDescriptor(
                    id: connector.Id,
                    name: connector.Name,
                    description: connector.Description,
                    logoUrl: connector.LogoUrl,
                    logoUrlDark: connector.LogoUrlDark,
                    distributionChannel: connector.DistributionChannel,
                    branding: connector.Branding,
                    metadata: connector.AppMetadata,
                    labels: connector.Labels,
                    installUrl: connector.InstallUrl,
                    isAccessible: true,
                    isEnabled: !configState.EnabledStates.TryGetValue(pair.Key, out var isEnabled) || isEnabled,
                    pluginDisplayNames: ResolvePluginDisplayNames(pair.Key, pluginDisplayNames, connector.PluginDisplayNames));
            }
        }

        return merged.Values
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static KernelPluginConnectorInfo MergeConnector(KernelPluginConnectorInfo existing, KernelPluginConnectorInfo incoming)
    {
        var name = existing.Name;
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, existing.Id, StringComparison.OrdinalIgnoreCase))
        {
            name = incoming.Name;
        }

        var description = !string.IsNullOrWhiteSpace(existing.Description)
            ? existing.Description
            : incoming.Description;
        var installUrl = !string.IsNullOrWhiteSpace(existing.InstallUrl)
            ? existing.InstallUrl
            : incoming.InstallUrl;
        var logoUrl = !string.IsNullOrWhiteSpace(existing.LogoUrl)
            ? existing.LogoUrl
            : incoming.LogoUrl;
        var logoUrlDark = !string.IsNullOrWhiteSpace(existing.LogoUrlDark)
            ? existing.LogoUrlDark
            : incoming.LogoUrlDark;
        var distributionChannel = !string.IsNullOrWhiteSpace(existing.DistributionChannel)
            ? existing.DistributionChannel
            : incoming.DistributionChannel;
        var branding = existing.Branding ?? incoming.Branding;
        var appMetadata = existing.AppMetadata ?? incoming.AppMetadata;
        IReadOnlyDictionary<string, string>? labels = null;
        if ((existing.Labels?.Count ?? 0) > 0 || (incoming.Labels?.Count ?? 0) > 0)
        {
            var mergedLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (incoming.Labels is not null)
            {
                foreach (var pair in incoming.Labels)
                {
                    mergedLabels[pair.Key] = pair.Value;
                }
            }

            if (existing.Labels is not null)
            {
                foreach (var pair in existing.Labels)
                {
                    mergedLabels[pair.Key] = pair.Value;
                }
            }

            labels = mergedLabels;
        }

        var pluginDisplayNames = existing.PluginDisplayNames
            .Concat(incoming.PluginDisplayNames)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new KernelPluginConnectorInfo(
            existing.Id,
            name,
            description,
            installUrl,
            pluginDisplayNames,
            logoUrl,
            logoUrlDark,
            distributionChannel,
            branding,
            appMetadata,
            labels);
    }

    public static bool ShouldSendAppListUpdatedNotification(IReadOnlyList<ControlPlaneAppDescriptor> items, bool accessibleLoaded, bool directoryLoaded)
        => items.Any(static item => item.IsAccessible) || (accessibleLoaded && directoryLoaded);

    public static bool AppListsEqual(IReadOnlyList<ControlPlaneAppDescriptor>? left, IReadOnlyList<ControlPlaneAppDescriptor>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private static ControlPlaneAppDescriptor CreateAppDescriptor(
        string id,
        string name,
        string? description,
        string? logoUrl,
        string? logoUrlDark,
        string? distributionChannel,
        object? branding,
        object? metadata,
        IReadOnlyDictionary<string, string>? labels,
        string? installUrl,
        bool isAccessible,
        bool isEnabled,
        IReadOnlyList<string> pluginDisplayNames)
        => new()
        {
            Id = id,
            Name = name,
            Description = description,
            LogoUrl = logoUrl,
            LogoUrlDark = logoUrlDark,
            DistributionChannel = distributionChannel,
            Branding = StructuredValue.FromPlainObject(branding),
            Metadata = StructuredValue.FromPlainObject(metadata),
            Labels = labels ?? new Dictionary<string, string>(StringComparer.Ordinal),
            InstallUrl = installUrl,
            IsAccessible = isAccessible,
            IsEnabled = isEnabled,
            PluginDisplayNames = pluginDisplayNames,
        };

    private static IReadOnlyList<string> ResolvePluginDisplayNames(
        string appId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> configuredNames,
        IReadOnlyList<string> connectorNames)
        => configuredNames.TryGetValue(appId, out var names)
            ? names
                .Concat(connectorNames)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : connectorNames;
}

/// <summary>
/// app/list 配置态宿主载体。
/// Host-side configuration carrier for the app-list surface.
/// </summary>
internal sealed record KernelAppConfigState(
    IReadOnlyDictionary<string, bool> EnabledStates,
    IReadOnlyDictionary<string, bool> AccessibleStates);

/// <summary>
/// connector 目录项宿主载体。
/// Host-side connector descriptor shared by plugin discovery and app-list projection.
/// </summary>
internal sealed record KernelPluginConnectorInfo(
    string Id,
    string Name,
    string? Description,
    string? InstallUrl,
    IReadOnlyList<string> PluginDisplayNames,
    string? LogoUrl,
    string? LogoUrlDark,
    string? DistributionChannel,
    object? Branding,
    object? AppMetadata,
    IReadOnlyDictionary<string, string>? Labels);
