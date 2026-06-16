using Tomlyn;
using Tomlyn.Model;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// requirements.toml 解析与约束装配辅助件。
/// Helpers for loading requirements.toml and building requirement-driven validation payloads.
/// </summary>
internal static class KernelConfigRequirementsUtilities
{
    private const string CloudRequirementsTomlEnvironmentVariable = "TIANSHU_CLOUD_REQUIREMENTS_TOML";
    private const string LegacyCloudRequirementsTomlEnvironmentVariable = "TIANSHU_CLOUD_REQUIREMENTS_TOML";
    private const string AdminRequirementsTomlEnvironmentVariable = "TIANSHU_ADMIN_REQUIREMENTS_TOML";
    private const string LegacyAdminRequirementsTomlEnvironmentVariable = "TIANSHU_ADMIN_REQUIREMENTS_TOML";

    public static (HashSet<string> AllowedApprovalPolicies, HashSet<string> AllowedSandboxModes) LoadConfigValidationRules(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return BuildConfigValidationRules(LoadMergedConfigRequirements());
    }

    public static Dictionary<string, bool> LoadRequiredFeatureFlags(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return BuildRequiredFeatureFlags(LoadMergedConfigRequirements());
    }

    public static KernelMergedConfigRequirements LoadMergedConfigRequirements()
    {
        var merged = new KernelRequirementsMergeState();
        merged.Merge(ReadRequirementsFromEnvironment(
            CloudRequirementsTomlEnvironmentVariable,
            LegacyCloudRequirementsTomlEnvironmentVariable,
            "cloud requirements"));
        merged.Merge(ReadRequirementsFromEnvironment(
            AdminRequirementsTomlEnvironmentVariable,
            LegacyAdminRequirementsTomlEnvironmentVariable,
            "admin requirements"));
        merged.Merge(ReadRequirementsFromFile(
            TianShuConfigTomlPathResolver.ResolveSystemRequirementsTomlPath(),
            "system requirements"));
        merged.Merge(ReadRequirementsFromFile(
            TianShuConfigTomlPathResolver.ResolveUserRequirementsTomlPath(),
            "compatibility user requirements"));
        return merged.Build();
    }

    public static object? BuildConfigRequirementsPayload(KernelMergedConfigRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        var allowedWebSearchModes = requirements.AllowedWebSearchModes?.ToArray();
        if (allowedWebSearchModes is not null
            && !allowedWebSearchModes.Contains("disabled", StringComparer.OrdinalIgnoreCase))
        {
            allowedWebSearchModes = [.. allowedWebSearchModes, "disabled"];
        }

        var featureRequirements = requirements.FeatureRequirements is { Count: > 0 }
            ? new Dictionary<string, bool>(requirements.FeatureRequirements, StringComparer.OrdinalIgnoreCase)
            : null;
        var network = requirements.Network is null
            ? null
            : new
            {
                enabled = requirements.Network.Enabled,
                httpPort = requirements.Network.HttpPort,
                socksPort = requirements.Network.SocksPort,
                allowUpstreamProxy = requirements.Network.AllowUpstreamProxy,
                dangerouslyAllowNonLoopbackProxy = requirements.Network.DangerouslyAllowNonLoopbackProxy,
                dangerouslyAllowNonLoopbackAdmin = requirements.Network.DangerouslyAllowNonLoopbackAdmin,
                dangerouslyAllowAllUnixSockets = requirements.Network.DangerouslyAllowAllUnixSockets,
                allowedDomains = requirements.Network.AllowedDomains,
                deniedDomains = requirements.Network.DeniedDomains,
                allowUnixSockets = requirements.Network.AllowUnixSockets,
                allowLocalBinding = requirements.Network.AllowLocalBinding,
            };

        if (requirements.AllowedApprovalPolicies is null
            && requirements.AllowedSandboxModes is null
            && allowedWebSearchModes is null
            && featureRequirements is null
            && string.IsNullOrWhiteSpace(requirements.EnforceResidency)
            && network is null)
        {
            return null;
        }

        return new
        {
            allowedApprovalPolicies = requirements.AllowedApprovalPolicies,
            allowedSandboxModes = requirements.AllowedSandboxModes,
            allowedWebSearchModes,
            featureRequirements,
            enforceResidency = string.IsNullOrWhiteSpace(requirements.EnforceResidency) ? null : requirements.EnforceResidency,
            network,
        };
    }

    public static (HashSet<string> AllowedApprovalPolicies, HashSet<string> AllowedSandboxModes) BuildConfigValidationRules(
        KernelMergedConfigRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        var allowedApprovalPolicies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "untrusted",
            "on-failure",
            "on-request",
            "never",
            "always",
        };
        var allowedSandboxModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "readonly",
            "workspacewrite",
            "dangerfullaccess",
            "danger-full-access",
        };

        if (requirements.AllowedApprovalPolicies is { } configApprovalPolicies)
        {
            allowedApprovalPolicies.Clear();
            foreach (var policy in configApprovalPolicies)
            {
                var normalized = Normalize(policy);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    allowedApprovalPolicies.Add(normalized!);
                }
            }
        }

        if (requirements.AllowedSandboxModes is { } configSandboxModes)
        {
            allowedSandboxModes.Clear();
            foreach (var mode in configSandboxModes)
            {
                var normalized = Normalize(mode);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    allowedSandboxModes.Add(NormalizeModeToken(normalized!));
                }
            }
        }

        return (allowedApprovalPolicies, allowedSandboxModes);
    }

    public static Dictionary<string, bool> BuildRequiredFeatureFlags(KernelMergedConfigRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        return requirements.FeatureRequirements is { } featureRequirements
            ? new Dictionary<string, bool>(featureRequirements, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }

    public static KernelParsedRequirements ParseRequirementsToml(string text, string sourceDescription)
    {
        var root = ParseTomlRoot(text, sourceDescription);
        var featureRequirements = ReadOptionalBooleanSection(root, "features", sourceDescription);
        if (featureRequirements is null || featureRequirements.Count == 0)
        {
            featureRequirements = ReadOptionalBooleanSection(root, "feature_requirements", sourceDescription);
        }

        return new KernelParsedRequirements(
            ReadOptionalStringArray(root, "allowed_approval_policies", sourceDescription),
            ReadOptionalStringArray(root, "allowed_sandbox_modes", sourceDescription),
            ReadOptionalStringArray(root, "allowed_web_search_modes", sourceDescription),
            featureRequirements,
            TryReadOptionalString(root, "enforce_residency", sourceDescription),
            ReadOptionalManagedNetworkRequirements(root, sourceDescription));
    }

    private static KernelParsedRequirements? ReadRequirementsFromEnvironment(
        string environmentVariableName,
        string legacyEnvironmentVariableName,
        string sourceDescription)
    {
        var text = Environment.GetEnvironmentVariable(environmentVariableName);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = Environment.GetEnvironmentVariable(legacyEnvironmentVariableName);
        }

        return string.IsNullOrWhiteSpace(text)
            ? null
            : ParseRequirementsToml(text, sourceDescription);
    }

    private static KernelParsedRequirements? ReadRequirementsFromFile(
        string path,
        string sourceDescription)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var text = File.ReadAllText(path);
        return ParseRequirementsToml(text, $"{sourceDescription} `{path}`");
    }

    private static Dictionary<string, object?> ParseTomlRoot(string text, string sourceDescription)
    {
        try
        {
            if (Toml.ToModel(text) is not TomlTable table)
            {
                throw new FormatException($"{sourceDescription} must be a TOML table.");
            }

            return KernelConfigObjectUtilities.ConvertTomlTableToDictionary(table);
        }
        catch (Exception ex) when (ex is not FormatException)
        {
            throw new FormatException($"failed to parse {sourceDescription}: {ex.Message}", ex);
        }
    }

    private static List<string>? ReadOptionalStringArray(
        IReadOnlyDictionary<string, object?> root,
        string key,
        string sourceDescription)
    {
        if (!root.TryGetValue(key, out var rawValue))
        {
            return null;
        }

        if (rawValue is not List<object?> list)
        {
            throw new FormatException($"{sourceDescription} field `{key}` must be an array of strings.");
        }

        var values = new List<string>(list.Count);
        foreach (var entry in list)
        {
            if (entry is not string text)
            {
                throw new FormatException($"{sourceDescription} field `{key}` must be an array of strings.");
            }

            var normalized = Normalize(text);
            if (!string.IsNullOrWhiteSpace(normalized)
                && !values.Contains(normalized!, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(normalized!);
            }
        }

        return values;
    }

    private static string? TryReadOptionalString(
        IReadOnlyDictionary<string, object?> root,
        string key,
        string sourceDescription)
    {
        if (!root.TryGetValue(key, out var rawValue))
        {
            return null;
        }

        if (rawValue is not string text)
        {
            throw new FormatException($"{sourceDescription} field `{key}` must be a string.");
        }

        return Normalize(text);
    }

    private static Dictionary<string, bool>? ReadOptionalBooleanSection(
        IReadOnlyDictionary<string, object?> root,
        string sectionName,
        string sourceDescription)
    {
        if (!root.TryGetValue(sectionName, out var rawValue))
        {
            return null;
        }

        if (rawValue is not Dictionary<string, object?> section)
        {
            throw new FormatException($"{sourceDescription} section `{sectionName}` must be a table.");
        }

        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in section)
        {
            if (pair.Value is not bool value)
            {
                throw new FormatException($"{sourceDescription} field `{sectionName}.{pair.Key}` must be a boolean.");
            }

            result[pair.Key] = value;
        }

        return result;
    }

    private static KernelManagedNetworkRequirements? ReadOptionalManagedNetworkRequirements(
        IReadOnlyDictionary<string, object?> root,
        string sourceDescription)
    {
        if (!root.TryGetValue("experimental_network", out var rawValue))
        {
            return null;
        }

        if (rawValue is not Dictionary<string, object?> section)
        {
            throw new FormatException($"{sourceDescription} section `experimental_network` must be a table.");
        }

        if (section.Count == 0)
        {
            return null;
        }

        return new KernelManagedNetworkRequirements(
            Enabled: ReadOptionalBoolean(section, "enabled", sourceDescription, "experimental_network"),
            HttpPort: ReadOptionalInteger(section, "http_port", sourceDescription, "experimental_network"),
            SocksPort: ReadOptionalInteger(section, "socks_port", sourceDescription, "experimental_network"),
            AllowUpstreamProxy: ReadOptionalBoolean(section, "allow_upstream_proxy", sourceDescription, "experimental_network"),
            DangerouslyAllowNonLoopbackProxy: ReadOptionalBoolean(section, "dangerously_allow_non_loopback_proxy", sourceDescription, "experimental_network"),
            DangerouslyAllowNonLoopbackAdmin: ReadOptionalBoolean(section, "dangerously_allow_non_loopback_admin", sourceDescription, "experimental_network"),
            DangerouslyAllowAllUnixSockets: ReadOptionalBoolean(section, "dangerously_allow_all_unix_sockets", sourceDescription, "experimental_network"),
            AllowedDomains: ReadOptionalStringArray(section, "allowed_domains", $"{sourceDescription} section `experimental_network`")?.ToArray(),
            DeniedDomains: ReadOptionalStringArray(section, "denied_domains", $"{sourceDescription} section `experimental_network`")?.ToArray(),
            AllowUnixSockets: ReadOptionalStringArray(section, "allow_unix_sockets", $"{sourceDescription} section `experimental_network`")?.ToArray(),
            AllowLocalBinding: ReadOptionalBoolean(section, "allow_local_binding", sourceDescription, "experimental_network"));
    }

    private static bool? ReadOptionalBoolean(
        IReadOnlyDictionary<string, object?> section,
        string key,
        string sourceDescription,
        string sectionName)
    {
        if (!section.TryGetValue(key, out var rawValue))
        {
            return null;
        }

        if (rawValue is not bool value)
        {
            throw new FormatException($"{sourceDescription} field `{sectionName}.{key}` must be a boolean.");
        }

        return value;
    }

    private static int? ReadOptionalInteger(
        IReadOnlyDictionary<string, object?> section,
        string key,
        string sourceDescription,
        string sectionName)
    {
        if (!section.TryGetValue(key, out var rawValue))
        {
            return null;
        }

        return rawValue switch
        {
            byte value => value,
            sbyte value => value,
            short value => value,
            ushort value => value,
            int value => value,
            uint value when value <= int.MaxValue => (int)value,
            long value when value is >= int.MinValue and <= int.MaxValue => (int)value,
            ulong value when value <= int.MaxValue => (int)value,
            _ => throw new FormatException($"{sourceDescription} field `{sectionName}.{key}` must be an integer."),
        };
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeModeToken(string value)
        => value
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
}
