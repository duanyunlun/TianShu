using System.Text;
using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// spawn-agent 角色定义、配置解析与描述格式化辅助件。
/// Helpers for spawn-agent role definitions, config parsing, and description formatting.
/// </summary>
internal static class KernelSpawnAgentRoleConfigurationUtilities
{
    private const string AgentTypeUnavailableError = "agent type is currently not available";
    private const string DefaultSpawnAgentRoleName = "default";
    private const string ExplorerSpawnAgentRoleName = "explorer";
    private const string WorkerSpawnAgentRoleName = "worker";
    private const string ExplorerBuiltInRoleConfigText = "";
    private const string DefaultSpawnAgentRoleDescription = "Default agent.";
    private const string ExplorerSpawnAgentRoleDescription =
        """
        Use `explorer` for specific codebase questions.
        Explorers are fast and authoritative.
        They must be used to ask specific, well-scoped questions on the codebase.
        Rules:
        - In order to avoid redundant work, you should avoid exploring the same problem that explorers have already covered. Typically, you should trust the explorer results without additional verification. You are still allowed to inspect the code yourself to gain the needed context!
        - You are encouraged to spawn up multiple explorers in parallel when you have multiple distinct questions to ask about the codebase that can be answered independently. This allows you to get more information faster without waiting for one question to finish before asking the next. While waiting for the explorer results, you can continue working on other local tasks that do not depend on those results. This parallelism is a key advantage of delegation, so use it whenever you have multiple questions to ask.
        - Reuse existing explorers for related questions.
        """;
    private const string WorkerSpawnAgentRoleDescription =
        """
        Use for execution and production work.
        Typical tasks:
        - Implement part of a feature
        - Fix tests or bugs
        - Split large refactors into independent chunks
        Rules:
        - Explicitly assign **ownership** of the task (files / responsibility). When the subtask involves code changes, you should clearly specify which files or modules the worker is responsible for. This helps avoid merge conflicts and ensures accountability. For example, you can say "Worker 1 is responsible for updating the authentication module, while Worker 2 will handle the database layer." By defining clear ownership, you can delegate more effectively and reduce coordination overhead.
        - Always tell workers they are **not alone in the codebase**, and they should not revert the edits made by others, and they should adjust their implementation to accommodate the changes made by others. This is important because there may be multiple workers making changes in parallel, and they need to be aware of each other's work to avoid conflicts and ensure a cohesive final product.
        """;

    public static IReadOnlyDictionary<string, KernelSpawnAgentRoleDefinition> ResolveSpawnAgentRoleDefinitions(
        KernelConfigReadSnapshot snapshot,
        string cwd)
    {
        var roles = new Dictionary<string, KernelSpawnAgentRoleDefinition>(GetBuiltInSpawnAgentRoleDefinitions(), StringComparer.Ordinal);
        foreach (var pair in ResolveUserDefinedSpawnAgentRoles(snapshot, cwd))
        {
            roles[pair.Key] = pair.Value;
        }

        return roles;
    }

    public static async Task<KernelSpawnAgentRoleOverrides> LoadSpawnAgentRoleOverridesAsync(
        KernelSpawnAgentRoleDefinition role,
        CancellationToken cancellationToken,
        bool throwOnFailure = true)
    {
        var config = await LoadSpawnAgentRoleConfigAsync(role, cancellationToken, throwOnFailure).ConfigureAwait(false);
        return ReadSpawnAgentRoleOverrides(config);
    }

    public static async Task<string> BuildSpawnAgentTypeDescriptionAsync(
        KernelConfigReadSnapshot snapshot,
        string cwd,
        CancellationToken cancellationToken)
    {
        var userDefinedRoles = ResolveUserDefinedSpawnAgentRoles(snapshot, cwd);
        var builtInRoles = GetBuiltInSpawnAgentRoleDefinitions();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var formattedRoles = new List<string>();

        foreach (var role in userDefinedRoles.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (seen.Add(role.Key))
            {
                formattedRoles.Add(await FormatSpawnAgentRoleDescriptionAsync(role.Value, cancellationToken).ConfigureAwait(false));
            }
        }

        foreach (var role in builtInRoles.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (seen.Add(role.Key))
            {
                formattedRoles.Add(await FormatSpawnAgentRoleDescriptionAsync(role.Value, cancellationToken).ConfigureAwait(false));
            }
        }

        return $"Optional type name for the new agent. If omitted, `{DefaultSpawnAgentRoleName}` is used.\nAvailable roles:\n{string.Join("\n", formattedRoles)}";
    }

    private static Dictionary<string, KernelSpawnAgentRoleDefinition> ResolveUserDefinedSpawnAgentRoles(
        KernelConfigReadSnapshot snapshot,
        string cwd)
    {
        var roles = new Dictionary<string, KernelSpawnAgentRoleDefinition>(StringComparer.Ordinal);
        if (!TryReadObjectExact(snapshot.Config, "agents", out var agents))
        {
            return roles;
        }

        foreach (var pair in agents)
        {
            if (string.IsNullOrWhiteSpace(pair.Key)
                || !TryAsDictionary(pair.Value, out var roleConfig))
            {
                continue;
            }

            var roleName = Normalize(pair.Key);
            if (string.IsNullOrWhiteSpace(roleName))
            {
                continue;
            }

            var resolvedConfigFilePath = ResolveSpawnAgentRoleConfigFilePath(
                snapshot,
                roleName!,
                ReadStringExact(roleConfig, "config_file"),
                cwd);
            var nicknameCandidates = ReadStringArrayExact(roleConfig, "nickname_candidates");
            roles[roleName!] = new KernelSpawnAgentRoleDefinition(
                roleName!,
                Normalize(ReadStringExact(roleConfig, "description")),
                resolvedConfigFilePath,
                EmbeddedConfigText: null,
                NicknameCandidates: nicknameCandidates.Length == 0 ? null : nicknameCandidates);
        }

        return roles;
    }

    private static Dictionary<string, KernelSpawnAgentRoleDefinition> GetBuiltInSpawnAgentRoleDefinitions()
    {
        return new Dictionary<string, KernelSpawnAgentRoleDefinition>(StringComparer.Ordinal)
        {
            [DefaultSpawnAgentRoleName] = new(
                DefaultSpawnAgentRoleName,
                DefaultSpawnAgentRoleDescription,
                ResolvedConfigFilePath: null,
                EmbeddedConfigText: null,
                NicknameCandidates: null),
            [ExplorerSpawnAgentRoleName] = new(
                ExplorerSpawnAgentRoleName,
                ExplorerSpawnAgentRoleDescription,
                ResolvedConfigFilePath: null,
                EmbeddedConfigText: ExplorerBuiltInRoleConfigText,
                NicknameCandidates: null),
            [WorkerSpawnAgentRoleName] = new(
                WorkerSpawnAgentRoleName,
                WorkerSpawnAgentRoleDescription,
                ResolvedConfigFilePath: null,
                EmbeddedConfigText: null,
                NicknameCandidates: null),
        };
    }

    private static string? ResolveSpawnAgentRoleConfigFilePath(
        KernelConfigReadSnapshot snapshot,
        string roleName,
        string? configuredPath,
        string cwd)
    {
        var normalizedPath = Normalize(configuredPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return null;
        }

        return KernelInstructionConfigUtilities.ResolveConfiguredInstructionFilePath(
            snapshot,
            $"agents.{roleName}.config_file",
            normalizedPath!,
            cwd);
    }

    private static async Task<string> FormatSpawnAgentRoleDescriptionAsync(
        KernelSpawnAgentRoleDefinition role,
        CancellationToken cancellationToken)
    {
        var description = Normalize(role.Description);
        if (string.IsNullOrWhiteSpace(description))
        {
            return $"{role.Name}: no description";
        }

        var overrides = await LoadSpawnAgentRoleOverridesAsync(role, cancellationToken, throwOnFailure: false).ConfigureAwait(false);
        var builder = new StringBuilder();
        builder.Append(role.Name).Append(": {\n").Append(description);
        AppendSpawnAgentLockedSettingsNote(builder, overrides);
        builder.Append("\n}");
        return builder.ToString();
    }

    private static async Task<Dictionary<string, object?>> LoadSpawnAgentRoleConfigAsync(
        KernelSpawnAgentRoleDefinition role,
        CancellationToken cancellationToken,
        bool throwOnFailure)
    {
        if (!string.IsNullOrWhiteSpace(role.EmbeddedConfigText))
        {
            return ParseSpawnAgentRoleConfigText(role.EmbeddedConfigText!, throwOnFailure);
        }

        if (string.IsNullOrWhiteSpace(role.ResolvedConfigFilePath))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        try
        {
            var content = await File.ReadAllTextAsync(role.ResolvedConfigFilePath!, cancellationToken).ConfigureAwait(false);
            return ParseSpawnAgentRoleConfigText(content, throwOnFailure);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or DirectoryNotFoundException or FileNotFoundException or OperationCanceledException)
        {
            if (!throwOnFailure || ex is OperationCanceledException)
            {
                if (ex is OperationCanceledException)
                {
                    throw;
                }

                return new Dictionary<string, object?>(StringComparer.Ordinal);
            }

            throw new InvalidOperationException(AgentTypeUnavailableError, ex);
        }
    }

    private static Dictionary<string, object?> ParseSpawnAgentRoleConfigText(string content, bool throwOnFailure)
    {
        try
        {
            if (Toml.ToModel(content) is TomlTable table)
            {
                return KernelConfigObjectUtilities.ConvertTomlTableToDictionary(table);
            }
        }
        catch (Exception ex)
        {
            if (throwOnFailure)
            {
                throw new InvalidOperationException(AgentTypeUnavailableError, ex);
            }

            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    private static KernelSpawnAgentRoleOverrides ReadSpawnAgentRoleOverrides(Dictionary<string, object?> config)
    {
        return new KernelSpawnAgentRoleOverrides(
            Normalize(ReadStringExact(config, "model")),
            Normalize(ReadStringExact(config, "model_reasoning_effort")),
            Normalize(ReadStringExact(config, "developer_instructions")));
    }

    private static string? ReadStringExact(Dictionary<string, object?> config, string propertyName)
        => TryReadValueExact(config, propertyName, out var rawValue)
           && TryReadString(rawValue, out var value)
            ? value
            : null;

    private static string[] ReadStringArrayExact(Dictionary<string, object?> config, string propertyName)
        => TryReadValueExact(config, propertyName, out var rawValue)
           && TryReadStringArray(rawValue, out var values)
            ? values
            : Array.Empty<string>();

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
                .Select(static item => TryReadString(item, out var text) ? Normalize(text) : null)
                .Where(static item => item is not null)
                .Cast<string>()
                .ToArray();
            return true;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            values = element
                .EnumerateArray()
                .Select(static item => item.ValueKind == JsonValueKind.String ? Normalize(item.GetString()) : null)
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

    private static void AppendSpawnAgentLockedSettingsNote(StringBuilder builder, KernelSpawnAgentRoleOverrides overrides)
    {
        var model = Normalize(overrides.Model);
        var reasoningEffort = Normalize(overrides.ReasoningEffort);
        switch (model, reasoningEffort)
        {
            case ({ } lockedModel, { } lockedReasoningEffort):
                builder.Append(
                    $"\n- This role's model is set to `{lockedModel}` and its reasoning effort is set to `{lockedReasoningEffort}`. These settings cannot be changed.");
                break;
            case ({ } lockedModel, null):
                builder.Append($"\n- This role's model is set to `{lockedModel}` and cannot be changed.");
                break;
            case (null, { } lockedReasoningEffort):
                builder.Append($"\n- This role's reasoning effort is set to `{lockedReasoningEffort}` and cannot be changed.");
                break;
        }
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
