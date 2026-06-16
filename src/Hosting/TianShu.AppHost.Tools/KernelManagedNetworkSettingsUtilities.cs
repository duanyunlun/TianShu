using System.Net;
using System.Text.Json;
using TianShu.AppHost.Configuration;
using static TianShu.AppHost.Configuration.KernelConfigRequirementsUtilities;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 托管网络 settings 的配置读取、requirements 合并与约束校验辅助方法。
/// Helpers for managed-network settings resolution, requirements merging, and constraint validation.
/// </summary>
internal static class KernelManagedNetworkSettingsUtilities
{
    public static KernelManagedNetworkSettings ResolveManagedNetworkSettings(KernelConfigReadSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var requirements = ReadManagedNetworkRequirements();
        if (requirements is null)
        {
            return KernelManagedNetworkSettings.Disabled(requirementsPresent: false);
        }

        var syntax = KernelPermissionProfileResolver.ResolvePermissionConfigSyntax(snapshot.OrderedLayers, snapshot.Config);
        if (syntax != KernelPermissionConfigSyntax.Profiles)
        {
            return KernelManagedNetworkSettings.Disabled(requirementsPresent: true);
        }

        var profileName = ReadStringExact(snapshot.Config, "default_permissions");
        if (string.IsNullOrWhiteSpace(profileName)
            || !TryReadObjectExact(snapshot.Config, "permissions", out var permissionsRoot)
            || !TryReadObjectExact(permissionsRoot, profileName!, out var profile)
            || !TryReadObjectExact(profile, "network", out var network))
        {
            return KernelManagedNetworkSettings.Disabled(requirementsPresent: true);
        }

        var settings = CreateDefaultManagedNetworkSettings(requirementsPresent: true);
        settings = ApplyConfiguredManagedNetworkSettings(settings, network);
        settings = ApplyManagedNetworkRequirements(settings, requirements);
        ValidateManagedNetworkSettings(settings, requirements);
        return settings;
    }

    public static KernelManagedNetworkSettings ResolveManagedNetworkSettingsWithSkillOverride(
        KernelConfigReadSnapshot snapshot,
        IReadOnlyList<string>? skillAllowedDomains,
        IReadOnlyList<string>? skillDeniedDomains)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var settings = ResolveManagedNetworkSettings(snapshot);
        if (skillAllowedDomains is not null)
        {
            settings = settings with { AllowedDomains = NormalizeManagedNetworkList(skillAllowedDomains) };
        }

        if (skillDeniedDomains is not null)
        {
            settings = settings with { DeniedDomains = NormalizeManagedNetworkList(skillDeniedDomains) };
        }

        ValidateManagedNetworkDomainPatterns("skill.permissions.network.allowed_domains", settings.AllowedDomains);
        ValidateManagedNetworkDomainPatterns("skill.permissions.network.denied_domains", settings.DeniedDomains);
        return settings;
    }

    private static KernelManagedNetworkSettings CreateDefaultManagedNetworkSettings(bool requirementsPresent)
    {
        return new KernelManagedNetworkSettings(
            RequirementsPresent: requirementsPresent,
            Enabled: false,
            HttpHost: "127.0.0.1",
            HttpPort: 3128,
            SocksHost: "127.0.0.1",
            SocksPort: 8081,
            EnableSocks5: true,
            EnableSocks5Udp: true,
            AllowUpstreamProxy: true,
            DangerouslyAllowNonLoopbackProxy: false,
            DangerouslyAllowAllUnixSockets: false,
            Mode: "full",
            AllowedDomains: Array.Empty<string>(),
            DeniedDomains: Array.Empty<string>(),
            AllowUnixSockets: Array.Empty<string>(),
            AllowLocalBinding: true);
    }

    private static KernelManagedNetworkSettings ApplyConfiguredManagedNetworkSettings(
        KernelManagedNetworkSettings settings,
        Dictionary<string, object?> network)
    {
        if (ReadBooleanExact(network, "enabled") is bool enabled)
        {
            settings = settings with { Enabled = enabled };
        }

        var httpUrl = ReadStringExact(network, "proxy_url");
        if (!string.IsNullOrWhiteSpace(httpUrl))
        {
            var binding = ResolveProxyBinding(httpUrl!, 3128, "network.proxy_url");
            settings = settings with { HttpHost = binding.Host, HttpPort = binding.Port };
        }

        if (ReadBooleanExact(network, "enable_socks5") is bool enableSocks5)
        {
            settings = settings with { EnableSocks5 = enableSocks5 };
        }

        var socksUrl = ReadStringExact(network, "socks_url");
        if (!string.IsNullOrWhiteSpace(socksUrl))
        {
            var binding = ResolveProxyBinding(socksUrl!, 8081, "network.socks_url");
            settings = settings with { SocksHost = binding.Host, SocksPort = binding.Port };
        }

        if (ReadBooleanExact(network, "enable_socks5_udp") is bool enableSocks5Udp)
        {
            settings = settings with { EnableSocks5Udp = enableSocks5Udp };
        }

        if (ReadBooleanExact(network, "allow_upstream_proxy") is bool allowUpstreamProxy)
        {
            settings = settings with { AllowUpstreamProxy = allowUpstreamProxy };
        }

        if (ReadBooleanExact(network, "dangerously_allow_non_loopback_proxy") is bool allowNonLoopbackProxy)
        {
            settings = settings with { DangerouslyAllowNonLoopbackProxy = allowNonLoopbackProxy };
        }

        if (ReadBooleanExact(network, "dangerously_allow_all_unix_sockets") is bool allowAllUnixSockets)
        {
            settings = settings with { DangerouslyAllowAllUnixSockets = allowAllUnixSockets };
        }

        var mode = ReadStringExact(network, "mode");
        if (!string.IsNullOrWhiteSpace(mode))
        {
            settings = settings with { Mode = NormalizeManagedNetworkMode(mode!) };
        }

        if (TryReadConfiguredStringArrayValue(network, out var allowedDomains, ["allowed_domains"]))
        {
            settings = settings with { AllowedDomains = NormalizeManagedNetworkList(allowedDomains) };
        }

        if (TryReadConfiguredStringArrayValue(network, out var deniedDomains, ["denied_domains"]))
        {
            settings = settings with { DeniedDomains = NormalizeManagedNetworkList(deniedDomains) };
        }

        if (TryReadConfiguredStringArrayValue(network, out var allowUnixSockets, ["allow_unix_sockets"]))
        {
            settings = settings with { AllowUnixSockets = NormalizeManagedNetworkList(allowUnixSockets) };
        }

        if (ReadBooleanExact(network, "allow_local_binding") is bool allowLocalBinding)
        {
            settings = settings with { AllowLocalBinding = allowLocalBinding };
        }

        return settings;
    }

    private static KernelManagedNetworkSettings ApplyManagedNetworkRequirements(
        KernelManagedNetworkSettings settings,
        KernelManagedNetworkRequirements requirements)
    {
        if (requirements.Enabled is bool enabled)
        {
            settings = settings with { Enabled = enabled };
        }

        if (requirements.HttpPort is int httpPort)
        {
            settings = settings with { HttpHost = "127.0.0.1", HttpPort = httpPort };
        }

        if (requirements.SocksPort is int socksPort)
        {
            settings = settings with { SocksHost = "127.0.0.1", SocksPort = socksPort };
        }

        if (requirements.AllowUpstreamProxy is bool allowUpstreamProxy)
        {
            settings = settings with { AllowUpstreamProxy = allowUpstreamProxy };
        }

        if (requirements.DangerouslyAllowNonLoopbackProxy is bool allowNonLoopbackProxy)
        {
            settings = settings with { DangerouslyAllowNonLoopbackProxy = allowNonLoopbackProxy };
        }

        if (requirements.DangerouslyAllowAllUnixSockets is bool allowAllUnixSockets)
        {
            settings = settings with { DangerouslyAllowAllUnixSockets = allowAllUnixSockets };
        }

        if (requirements.AllowedDomains is not null)
        {
            settings = settings with { AllowedDomains = NormalizeManagedNetworkList(requirements.AllowedDomains) };
        }

        if (requirements.DeniedDomains is not null)
        {
            settings = settings with { DeniedDomains = NormalizeManagedNetworkList(requirements.DeniedDomains) };
        }

        if (requirements.AllowUnixSockets is not null)
        {
            settings = settings with { AllowUnixSockets = NormalizeManagedNetworkList(requirements.AllowUnixSockets) };
        }

        if (requirements.AllowLocalBinding is bool allowLocalBinding)
        {
            settings = settings with { AllowLocalBinding = allowLocalBinding };
        }

        return settings;
    }

    private static void ValidateManagedNetworkSettings(
        KernelManagedNetworkSettings settings,
        KernelManagedNetworkRequirements requirements)
    {
        ValidateManagedNetworkDomainPatterns("network.allowed_domains", settings.AllowedDomains);
        ValidateManagedNetworkDomainPatterns("network.denied_domains", settings.DeniedDomains);
        ValidateManagedNetworkUnixSocketAllowlist(settings.AllowUnixSockets);

        var allowAllUnixSockets = requirements.DangerouslyAllowAllUnixSockets
            ?? requirements.AllowUnixSockets is null;
        if (settings.DangerouslyAllowAllUnixSockets && !allowAllUnixSockets)
        {
            throw CreateManagedNetworkConstraintError(
                "network.dangerously_allow_all_unix_sockets",
                "true",
                "false (disabled by managed config)");
        }
    }

    private static void ValidateManagedNetworkDomainPatterns(string fieldName, IReadOnlyList<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (string.Equals(KernelManagedNetworkHelpers.Normalize(pattern), "*", StringComparison.Ordinal))
            {
                throw CreateManagedNetworkConstraintError(
                    fieldName,
                    pattern,
                    "exact hosts or scoped wildcards like *.example.com or **.example.com");
            }
        }
    }

    private static void ValidateManagedNetworkUnixSocketAllowlist(IReadOnlyList<string> socketPaths)
    {
        for (var index = 0; index < socketPaths.Count; index++)
        {
            var socketPath = socketPaths[index];
            if (IsAbsoluteManagedNetworkSocketPath(socketPath))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"invalid network.allow_unix_sockets[{index}]: expected an absolute path, got \"{socketPath}\"");
        }
    }

    private static bool IsAbsoluteManagedNetworkSocketPath(string socketPath)
    {
        var normalized = KernelManagedNetworkHelpers.Normalize(socketPath);
        return !string.IsNullOrWhiteSpace(normalized)
            && (Path.IsPathFullyQualified(normalized) || normalized.StartsWith("/", StringComparison.Ordinal));
    }

    private static bool TryReadConfiguredStringArrayValue(
        Dictionary<string, object?> config,
        out string[] values,
        params string[][] propertyPaths)
    {
        foreach (var propertyPath in propertyPaths)
        {
            if (TryReadNestedValueExact(config, propertyPath, out var rawValue)
                && TryReadStringArray(rawValue, out values))
            {
                return true;
            }
        }

        values = Array.Empty<string>();
        return false;
    }

    private static string? ReadStringExact(Dictionary<string, object?> config, string propertyName)
        => TryReadValueExact(config, propertyName, out var rawValue)
           && TryReadString(rawValue, out var value)
            ? value
            : null;

    private static bool? ReadBooleanExact(Dictionary<string, object?> config, string propertyName)
        => TryReadValueExact(config, propertyName, out var rawValue)
           && TryReadBoolean(rawValue, out var value)
            ? value
            : null;

    private static bool TryReadObjectExact(
        Dictionary<string, object?> config,
        string propertyName,
        out Dictionary<string, object?> value)
    {
        if (TryReadValueExact(config, propertyName, out var rawValue)
            && TryAsDictionary(rawValue, out value))
        {
            return true;
        }

        value = null!;
        return false;
    }

    private static bool TryReadValueExact(Dictionary<string, object?> config, string propertyName, out object? value)
        => config.TryGetValue(propertyName, out value);

    private static bool TryReadNestedValueExact(
        Dictionary<string, object?> config,
        IReadOnlyList<string> propertyPath,
        out object? value)
    {
        var current = config;
        for (var index = 0; index < propertyPath.Count; index++)
        {
            if (!TryReadValueExact(current, propertyPath[index], out value))
            {
                return false;
            }

            if (index == propertyPath.Count - 1)
            {
                return true;
            }

            if (!TryAsDictionary(value, out current))
            {
                value = null;
                return false;
            }
        }

        value = null;
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
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                dictionary = pairs.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Object:
                dictionary = ConvertJsonObject(element);
                return true;
            default:
                dictionary = null!;
                return false;
        }
    }

    private static bool TryReadString(object? value, out string text)
    {
        switch (value)
        {
            case string stringValue:
                text = stringValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                text = element.GetString() ?? string.Empty;
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }

    private static bool TryReadBoolean(object? value, out bool booleanValue)
    {
        switch (value)
        {
            case bool native:
                booleanValue = native;
                return true;
            case JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                booleanValue = element.GetBoolean();
                return true;
            case string text when bool.TryParse(text, out var parsed):
                booleanValue = parsed;
                return true;
            default:
                booleanValue = default;
                return false;
        }
    }

    private static bool TryReadStringArray(object? value, out string[] values)
    {
        if (value is string)
        {
            values = Array.Empty<string>();
            return false;
        }

        if (value is IEnumerable<object?> items)
        {
            values = items
                .Select(static item => TryReadString(item, out var text) ? KernelManagedNetworkHelpers.Normalize(text) : null)
                .Where(static item => item is not null)
                .Cast<string>()
                .ToArray();
            return true;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            values = element
                .EnumerateArray()
                .Select(static item => item.ValueKind == JsonValueKind.String ? KernelManagedNetworkHelpers.Normalize(item.GetString()) : null)
                .Where(static item => item is not null)
                .Cast<string>()
                .ToArray();
            return true;
        }

        values = Array.Empty<string>();
        return false;
    }

    private static Dictionary<string, object?> ConvertJsonObject(JsonElement element)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            dictionary[property.Name] = ConvertJsonValue(property.Value);
        }

        return dictionary;
    }

    private static object? ConvertJsonValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var intValue)
                ? intValue
                : element.TryGetDouble(out var doubleValue)
                    ? doubleValue
                    : element.GetRawText(),
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };

    private static string NormalizeManagedNetworkMode(string mode)
    {
        var normalized = KernelManagedNetworkHelpers.Normalize(mode);
        return normalized switch
        {
            "limited" => "limited",
            "full" => "full",
            _ => throw new InvalidOperationException($"invalid network.mode: {mode}"),
        };
    }

    private static string[] NormalizeManagedNetworkList(IEnumerable<string> values)
    {
        return values
            .Select(KernelManagedNetworkHelpers.Normalize)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static InvalidOperationException CreateManagedNetworkConstraintError(
        string fieldName,
        string candidate,
        string allowed)
    {
        return new InvalidOperationException(
            $"network proxy constraints are invalid: invalid value for {fieldName}: {candidate} (allowed {allowed})");
    }

    private static (string Host, int Port) ResolveProxyBinding(string rawUrl, int defaultPort, string fieldName)
    {
        var normalized = KernelManagedNetworkHelpers.Normalize(rawUrl);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return ("127.0.0.1", defaultPort);
        }

        try
        {
            return ParseManagedNetworkHostAndPort(normalized!, defaultPort);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException($"invalid {fieldName}: {normalized}");
        }
    }

    private static (string Host, int Port) ParseManagedNetworkHostAndPort(string rawUrl, int defaultPort)
    {
        var trimmed = rawUrl.Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException($"missing host in network proxy address: {rawUrl}");
        }

        if (IPAddress.TryParse(trimmed, out var ipAddress)
            && ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            && !trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return (trimmed, defaultPort);
        }

        var candidate = trimmed.Contains("://", StringComparison.Ordinal)
            ? trimmed
            : $"http://{trimmed}";
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return (
                KernelManagedNetworkHelpers.Normalize(uri.Host.Trim('[', ']'))
                    ?? throw new InvalidOperationException($"missing host in network proxy address: {rawUrl}"),
                uri.IsDefaultPort ? defaultPort : uri.Port);
        }

        return ParseManagedNetworkHostAndPortFallback(trimmed, defaultPort);
    }

    private static (string Host, int Port) ParseManagedNetworkHostAndPortFallback(string input, int defaultPort)
    {
        var withoutScheme = input.Split(new[] { "://" }, StringSplitOptions.None).Last();
        var hostPort = withoutScheme.Split('/').FirstOrDefault() ?? withoutScheme;
        hostPort = hostPort.Split('@').Last();

        if (hostPort.StartsWith("[", StringComparison.Ordinal))
        {
            var end = hostPort.IndexOf(']');
            if (end <= 1)
            {
                throw new InvalidOperationException($"missing host in network proxy address: {input}");
            }

            var host = hostPort[1..end];
            var portText = hostPort[(end + 1)..].TrimStart(':');
            return (host, ushort.TryParse(portText, out var parsedPort) ? parsedPort : defaultPort);
        }

        var colonCount = hostPort.Count(static ch => ch == ':');
        if (colonCount == 1)
        {
            var separator = hostPort.LastIndexOf(':');
            var host = hostPort[..separator];
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new InvalidOperationException($"missing host in network proxy address: {input}");
            }

            var portText = hostPort[(separator + 1)..];
            return (host, ushort.TryParse(portText, out var parsedPort) ? parsedPort : defaultPort);
        }

        if (string.IsNullOrWhiteSpace(hostPort))
        {
            throw new InvalidOperationException($"missing host in network proxy address: {input}");
        }

        return (hostPort, defaultPort);
    }

    private static KernelManagedNetworkRequirements? ReadManagedNetworkRequirements()
        => LoadMergedConfigRequirements().Network;
}
