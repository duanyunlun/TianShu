using System.Security.Cryptography;
using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// 配置写入、写后校验与覆盖态分析辅助件。
/// Helpers for config writes, post-write validation, and override-state analysis.
/// </summary>
internal static class KernelConfigWriteUtilities
{
    private static readonly StringComparer ConfigPathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static string ResolveCliConfigOverrideBaseDirectory(string? cwd, string? tianShuHome = null)
    {
        var normalizedCwd = NormalizeText(cwd);
        if (!string.IsNullOrWhiteSpace(normalizedCwd))
        {
            return Path.GetFullPath(normalizedCwd!);
        }

        return tianShuHome ?? TianShuHomePathUtilities.ResolveTianShuHomePath();
    }

    public static string ResolveConfigWritePath(string? filePath, string? cwd, string key)
        => ResolveConfigWritePath(filePath, cwd, new[] { key });

    public static string ResolveConfigWritePath(string? filePath, string? cwd, IEnumerable<string> keys)
    {
        _ = cwd;
        _ = keys;

        var userConfigPath = Path.GetFullPath(TianShuConfigTomlPathResolver.ResolveUserConfigTomlPath());
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return userConfigPath;
        }

        var providedPath = Path.GetFullPath(filePath);
        if (!AreEquivalentForComparison(userConfigPath, providedPath))
        {
            throw new InvalidOperationException("Only writes to the user config are allowed");
        }

        return userConfigPath;
    }

    public static string ResolveConfigWriteTargetPath(string resolvedPath)
    {
        var writePaths = ResolveSymlinkWritePaths(resolvedPath);
        return writePaths.WritePath;
    }

    public static bool TryValidateConfigEdit(
        string key,
        string valueJson,
        (HashSet<string> AllowedApprovalPolicies, HashSet<string> AllowedSandboxModes) rules,
        out string? error)
    {
        error = null;
        if (!TryParseConfigJsonValue(valueJson, out var value))
        {
            error = $"配置项 `{key}` 不是合法 JSON。";
            return true;
        }

        var normalizedKey = NormalizeConfigKeyPathForValidation(key);
        if (IsApprovalPolicyKey(normalizedKey))
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                error = $"配置项 `{key}` 必须是字符串。";
                return true;
            }

            var approvalPolicy = NormalizeText(value.GetString());
            if (string.IsNullOrWhiteSpace(approvalPolicy))
            {
                error = $"配置项 `{key}` 不能为空。";
                return true;
            }

            if (!rules.AllowedApprovalPolicies.Contains(approvalPolicy!))
            {
                error = $"配置项 `{key}` 不在允许范围内：{string.Join(", ", rules.AllowedApprovalPolicies.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase))}";
                return true;
            }

            return false;
        }

        if (IsSandboxModeKey(normalizedKey))
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                error = $"配置项 `{key}` 必须是字符串。";
                return true;
            }

            var sandboxMode = NormalizeText(value.GetString());
            if (string.IsNullOrWhiteSpace(sandboxMode))
            {
                error = $"配置项 `{key}` 不能为空。";
                return true;
            }

            var normalizedMode = NormalizeModeToken(sandboxMode!);
            if (!rules.AllowedSandboxModes.Contains(normalizedMode))
            {
                error = $"配置项 `{key}` 不在允许范围内：{string.Join(", ", rules.AllowedSandboxModes.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase))}";
                return true;
            }

            return false;
        }

        if (IsModelKey(normalizedKey))
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(NormalizeText(value.GetString())))
            {
                error = $"配置项 `{key}` 必须是非空字符串。";
                return true;
            }

            return false;
        }

        if (normalizedKey.EndsWith(".enabled", StringComparison.OrdinalIgnoreCase)
            || normalizedKey.EndsWith(".isAccessible", StringComparison.OrdinalIgnoreCase))
        {
            if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                error = $"配置项 `{key}` 必须是布尔值。";
                return true;
            }

            return false;
        }

        if (normalizedKey.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedKey, "providerBaseUrl", StringComparison.OrdinalIgnoreCase))
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                error = $"配置项 `{key}` 必须是 URL 字符串。";
                return true;
            }

            var url = NormalizeText(value.GetString());
            if (string.IsNullOrWhiteSpace(url)
                || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                error = $"配置项 `{key}` 必须是 http/https 绝对地址。";
                return true;
            }

            return false;
        }

        return false;
    }

    public static void ValidateMutatedConfigTable(
        TomlTable root,
        (HashSet<string> AllowedApprovalPolicies, HashSet<string> AllowedSandboxModes) rules,
        IReadOnlyDictionary<string, bool> requiredFeatureFlags)
    {
        var config = KernelConfigObjectUtilities.ConvertTomlTableToDictionary(root);
        foreach (var (keyPath, valueJson) in EnumerateConfigValidationEntries(config))
        {
            if (!TryValidateConfigEdit(keyPath, valueJson, rules, out var validationError))
            {
                continue;
            }

            throw KernelConfigWriteException.Validation(validationError!);
        }

        if (requiredFeatureFlags.Count == 0)
        {
            return;
        }

        foreach (var pair in requiredFeatureFlags)
        {
            if (!TryGetConfigValue(config, ["features", pair.Key], out var actualValue))
            {
                continue;
            }

            if (actualValue is not bool actualFlag)
            {
                throw KernelConfigWriteException.Validation(
                    $"配置项 `features.{pair.Key}` 必须是布尔值，以满足 requirements.toml 的约束。");
            }

            if (actualFlag == pair.Value)
            {
                continue;
            }

            throw KernelConfigWriteException.Validation(
                $"配置项 `features.{pair.Key}` 与 requirements.toml 的约束冲突，要求值为 {pair.Value.ToString().ToLowerInvariant()}。");
        }
    }

    public static IEnumerable<(string KeyPath, string ValueJson)> EnumerateConfigValidationEntries(Dictionary<string, object?> config)
    {
        foreach (var pair in config)
        {
            foreach (var entry in EnumerateConfigValidationEntries(pair.Value, pair.Key))
            {
                yield return entry;
            }
        }
    }

    public static bool TryParseConfigJsonValue(string valueJson, out JsonElement value)
    {
        value = default;
        try
        {
            using var document = JsonDocument.Parse(valueJson);
            value = document.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string NormalizeConfigKeyPathForValidation(string key)
    {
        return key
            .Replace("\"", string.Empty, StringComparison.Ordinal)
            .Replace("'", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    public static string NormalizeModeToken(string value)
    {
        return value
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
    }

    public static bool IsApprovalPolicyKey(string key)
    {
        return string.Equals(key, "approvalPolicy", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "approval_policy", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "permissions.approvalPolicy", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "permissions.approval_policy", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSandboxModeKey(string key)
    {
        return string.Equals(key, "sandbox.type", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "sandbox_mode", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "permissions.sandbox_mode", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsModelKey(string key)
    {
        return string.Equals(key, "model", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "review_model", StringComparison.OrdinalIgnoreCase)
               || key.EndsWith(".model", StringComparison.OrdinalIgnoreCase);
    }

    public static string ComputeConfigVersion(Dictionary<string, string> values)
    {
        var ordered = values.OrderBy(static x => x.Key, StringComparer.Ordinal).ToArray();
        var serialized = JsonSerializer.Serialize(ordered);
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(serialized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ComputeConfigVersion(TomlTable values)
        => KernelConfigObjectUtilities.ComputeConfigObjectVersion(KernelConfigObjectUtilities.ConvertTomlTableToDictionary(values));

    public static Dictionary<string, object?> BuildConfigObject(Dictionary<string, string> values)
    {
        var root = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in values.OrderBy(static x => x.Key, StringComparer.Ordinal))
        {
            SetNestedConfigValue(root, pair.Key, TryParseJsonValue(pair.Value));
        }

        return root;
    }

    public static void SetNestedConfigValue(
        Dictionary<string, object?> root,
        string keyPath,
        object? value)
    {
        var segments = keyPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return;
        }

        Dictionary<string, object?> current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            if (!current.TryGetValue(segment, out var next) || next is not Dictionary<string, object?> typed)
            {
                typed = new Dictionary<string, object?>(StringComparer.Ordinal);
                current[segment] = typed;
            }

            current = typed;
        }

        current[segments[^1]] = value;
    }

    public static object? TryParseJsonValue(string rawJson)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(rawJson);
        }
        catch
        {
            return rawJson;
        }
    }

    public static string? TryGetRawProperty(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.GetRawText();
    }

    public static List<KernelConfigWriteItem> ExtractBatchConfigItems(JsonElement @params)
    {
        var results = new List<KernelConfigWriteItem>();
        if (@params.ValueKind != JsonValueKind.Object)
        {
            return results;
        }

        var defaultMergeStrategy = NormalizeConfigMergeStrategy(ReadStringProperty(@params, "mergeStrategy"));

        if (@params.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var key = ReadStringProperty(item, "keyPath")
                    ?? ReadStringProperty(item, "key")
                    ?? ReadStringProperty(item, "path");
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var valueJson = TryGetRawProperty(item, "value") ?? "null";
                results.Add(new KernelConfigWriteItem(
                    key,
                    valueJson,
                    NormalizeConfigMergeStrategy(ReadStringProperty(item, "mergeStrategy")) ?? defaultMergeStrategy));
            }
        }

        if (@params.TryGetProperty("edits", out var edits) && edits.ValueKind == JsonValueKind.Array)
        {
            foreach (var edit in edits.EnumerateArray())
            {
                var key = ReadStringProperty(edit, "keyPath")
                    ?? ReadStringProperty(edit, "key")
                    ?? ReadStringProperty(edit, "path");
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var valueJson = TryGetRawProperty(edit, "value") ?? "null";
                results.Add(new KernelConfigWriteItem(
                    key,
                    valueJson,
                    NormalizeConfigMergeStrategy(ReadStringProperty(edit, "mergeStrategy")) ?? defaultMergeStrategy));
            }
        }

        return results;
    }

    public static async Task<KernelConfigWriteMutationResult> MutatePersistedConfigTableAsync(
        SemaphoreSlim configGate,
        string resolvedPath,
        string? expectedVersion,
        Action<TomlTable> mutate,
        (HashSet<string> AllowedApprovalPolicies, HashSet<string> AllowedSandboxModes) validationRules,
        IReadOnlyDictionary<string, bool> requiredFeatureFlags,
        CancellationToken cancellationToken)
    {
        var writePath = ResolveConfigWriteTargetPath(resolvedPath);
        await configGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = KernelConfigPersistenceUtilities.ReadPersistedConfigTable(writePath);
            var currentVersion = ComputeConfigVersion(root);
            if (!string.IsNullOrWhiteSpace(expectedVersion)
                && !string.Equals(expectedVersion, currentVersion, StringComparison.Ordinal))
            {
                throw KernelConfigWriteException.VersionConflict();
            }

            mutate(root);
            ValidateMutatedConfigTable(root, validationRules, requiredFeatureFlags);
            var updatedVersion = ComputeConfigVersion(root);
            if (string.Equals(updatedVersion, currentVersion, StringComparison.Ordinal))
            {
                return new KernelConfigWriteMutationResult(root, resolvedPath, currentVersion);
            }

            var serialized = Toml.FromModel(root).TrimEnd() + Environment.NewLine;
            await WriteTextAtomicallyAsync(writePath, serialized, cancellationToken).ConfigureAwait(false);
            return new KernelConfigWriteMutationResult(root, resolvedPath, updatedVersion);
        }
        finally
        {
            configGate.Release();
        }
    }

    public static async Task WriteTextAtomicallyAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"无法确定配置文件目录：{path}");
        }

        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".tianshu-config-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // 临时文件清理失败不覆盖主链错误。
                }
            }
        }
    }

    public static bool TryApplyConfigWriteValue(
        TomlTable root,
        IReadOnlyList<string> segments,
        JsonElement value,
        string? mergeStrategy,
        out bool pathNotFound)
    {
        pathNotFound = false;
        if (segments.Count == 0)
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            if (!TryRemoveTomlPathValue(root, segments))
            {
                pathNotFound = true;
                return false;
            }

            return true;
        }

        var normalizedMergeStrategy = NormalizeConfigMergeStrategy(mergeStrategy);
        if (string.Equals(normalizedMergeStrategy, "upsert", StringComparison.OrdinalIgnoreCase)
            && TryUpsertTomlPathValue(root, segments, KernelConfigPersistenceUtilities.ConvertJsonElementToTomlValue(value)))
        {
            return true;
        }

        KernelConfigPersistenceUtilities.SetTomlPathValue(
            root,
            segments,
            KernelConfigPersistenceUtilities.ConvertJsonElementToTomlValue(value));
        return true;
    }

    public static bool TryUpsertTomlPathValue(TomlTable root, IReadOnlyList<string> segments, object value)
    {
        TomlTable current = root;
        for (var index = 0; index < segments.Count - 1; index++)
        {
            current = KernelConfigPersistenceUtilities.GetOrCreateTomlTable(current, segments[index]);
        }

        if (current.TryGetValue(segments[^1], out var existing)
            && existing is TomlTable existingTable
            && value is TomlTable incomingTable)
        {
            KernelConfigPersistenceUtilities.MergePersistedConfigTable(existingTable, incomingTable);
            return true;
        }

        return false;
    }

    public static bool TryRemoveTomlPathValue(TomlTable root, IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
        {
            return false;
        }

        var stack = new Stack<(TomlTable Table, string Key)>();
        TomlTable current = root;
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (!current.TryGetValue(segments[index], out var next)
                || next is not TomlTable nextTable)
            {
                return false;
            }

            stack.Push((current, segments[index]));
            current = nextTable;
        }

        if (!current.Remove(segments[^1]))
        {
            return false;
        }

        while (stack.Count > 0 && current.Count == 0)
        {
            var (parent, key) = stack.Pop();
            parent.Remove(key);
            current = parent;
        }

        return true;
    }

    public static string? NormalizeConfigMergeStrategy(string? value)
    {
        var normalized = NormalizeText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "replace";
        }

        return string.Equals(normalized, "upsert", StringComparison.OrdinalIgnoreCase)
            ? "upsert"
            : "replace";
    }

    public static object? ComputeConfigWriteOverriddenMetadata(
        KernelConfigReadSnapshot snapshot,
        Dictionary<string, object?> userConfig,
        IReadOnlyList<string> segments)
    {
        var userValue = TryGetConfigValue(userConfig, segments, out var explicitUserValue)
            ? explicitUserValue
            : null;
        var hasEffectiveValue = TryGetConfigValue(snapshot.Config, segments, out var effectiveValue);
        if (!hasEffectiveValue && userValue is null)
        {
            return null;
        }

        if (ConfigValuesEqual(userValue, effectiveValue))
        {
            return null;
        }

        var overridingLayer = FindConfigWriteOverridingLayer(snapshot, effectiveValue, segments);
        if (overridingLayer is null)
        {
            return null;
        }

        return new
        {
            message = BuildConfigOverrideMessage(overridingLayer),
            overridingLayer = new
            {
                name = overridingLayer.Name,
                version = overridingLayer.Version,
            },
            effectiveValue,
        };
    }

    public static KernelConfigReadLayer? FindConfigWriteOverridingLayer(
        KernelConfigReadSnapshot snapshot,
        object? effectiveValue,
        IReadOnlyList<string> segments)
    {
        for (var index = snapshot.OrderedLayers.Count - 1; index >= 0; index--)
        {
            var layer = snapshot.OrderedLayers[index];
            if (!TryGetConfigValue(layer.Config, segments, out var layerValue))
            {
                continue;
            }

            if (ConfigValuesEqual(layerValue, effectiveValue))
            {
                return layer;
            }
        }

        return null;
    }

    public static bool TryGetConfigValue(
        Dictionary<string, object?> root,
        IReadOnlyList<string> segments,
        out object? value)
    {
        value = root;
        foreach (var segment in segments)
        {
            if (value is Dictionary<string, object?> dictionary)
            {
                if (!dictionary.TryGetValue(segment, out value))
                {
                    value = null;
                    return false;
                }

                continue;
            }

            if (value is List<object?> list
                && int.TryParse(segment, out var index)
                && index >= 0
                && index < list.Count)
            {
                value = list[index];
                continue;
            }

            value = null;
            return false;
        }

        return true;
    }

    public static bool ConfigValuesEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left is Dictionary<string, object?> leftDictionary && right is Dictionary<string, object?> rightDictionary)
        {
            if (leftDictionary.Count != rightDictionary.Count)
            {
                return false;
            }

            foreach (var pair in leftDictionary)
            {
                if (!rightDictionary.TryGetValue(pair.Key, out var rightValue)
                    || !ConfigValuesEqual(pair.Value, rightValue))
                {
                    return false;
                }
            }

            return true;
        }

        if (left is List<object?> leftList && right is List<object?> rightList)
        {
            if (leftList.Count != rightList.Count)
            {
                return false;
            }

            for (var index = 0; index < leftList.Count; index++)
            {
                if (!ConfigValuesEqual(leftList[index], rightList[index]))
                {
                    return false;
                }
            }

            return true;
        }

        return Equals(left, right);
    }

    private static IEnumerable<(string KeyPath, string ValueJson)> EnumerateConfigValidationEntries(object? value, string keyPath)
    {
        switch (value)
        {
            case Dictionary<string, object?> dictionary:
                foreach (var pair in dictionary)
                {
                    var nestedKeyPath = $"{keyPath}.{pair.Key}";
                    foreach (var entry in EnumerateConfigValidationEntries(pair.Value, nestedKeyPath))
                    {
                        yield return entry;
                    }
                }

                yield break;

            case List<object?> list:
                for (var index = 0; index < list.Count; index++)
                {
                    var nestedKeyPath = $"{keyPath}.{index}";
                    foreach (var entry in EnumerateConfigValidationEntries(list[index], nestedKeyPath))
                    {
                        yield return entry;
                    }
                }

                yield break;

            default:
                yield return (keyPath, JsonSerializer.Serialize(value));
                yield break;
        }
    }

    private static string BuildConfigOverrideMessage(KernelConfigReadLayer layer)
    {
        var layerName = JsonSerializer.SerializeToElement(layer.Name);
        var type = NormalizeText(ReadStringProperty(layerName, "type")) ?? string.Empty;
        return type.ToLowerInvariant() switch
        {
            "project" => $"Overridden by project config: {ReadStringProperty(layerName, "dotTianShuFolder")}/tianshu.toml",
            "user" => $"Overridden by user config: {ReadStringProperty(layerName, "file")}",
            "sessionflags" => "Overridden by session flags",
            _ => "Overridden by a higher-precedence config layer",
        };
    }

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool AreEquivalentForComparison(string? left, string? right)
    {
        var normalizedLeft = TryNormalizeForComparison(left);
        var normalizedRight = TryNormalizeForComparison(right);
        return !string.IsNullOrWhiteSpace(normalizedLeft)
               && !string.IsNullOrWhiteSpace(normalizedRight)
               && ConfigPathComparer.Equals(normalizedLeft, normalizedRight);
    }

    private static string? TryNormalizeForComparison(string? path)
    {
        var normalizedPath = NormalizePathForComparison(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return null;
        }

        var resolved = ResolveSymlinkWritePaths(normalizedPath!);
        return NormalizeComparablePath(resolved.ReadPath ?? resolved.WritePath);
    }

    private static KernelConfigWritePathResolution ResolveSymlinkWritePaths(string path)
    {
        var root = NormalizeComparablePath(Path.GetFullPath(path));
        var current = root;
        var visited = new HashSet<string>(ConfigPathComparer);

        while (true)
        {
            if (!TryGetLinkTarget(current, out var exists, out var linkTarget))
            {
                return new KernelConfigWritePathResolution(null, root);
            }

            if (!exists && string.IsNullOrWhiteSpace(linkTarget))
            {
                return new KernelConfigWritePathResolution(current, current);
            }

            if (string.IsNullOrWhiteSpace(linkTarget))
            {
                return new KernelConfigWritePathResolution(current, current);
            }

            if (!visited.Add(current))
            {
                return new KernelConfigWritePathResolution(null, root);
            }

            current = ResolveLinkTarget(current, linkTarget!);
        }
    }

    private static bool TryGetLinkTarget(string path, out bool exists, out string? linkTarget)
    {
        exists = false;
        linkTarget = null;

        try
        {
            FileSystemInfo info;
            if (Directory.Exists(path))
            {
                info = new DirectoryInfo(path);
            }
            else
            {
                info = new FileInfo(path);
            }

            info.Refresh();
            exists = info.Exists;
            linkTarget = NormalizeText(info.LinkTarget);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static string ResolveLinkTarget(string path, string linkTarget)
    {
        if (Path.IsPathRooted(linkTarget))
        {
            return NormalizeComparablePath(linkTarget);
        }

        var parentDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
        return NormalizeComparablePath(Path.Combine(parentDirectory, linkTarget));
    }

    private static string? NormalizePathForComparison(string? value)
    {
        var normalized = NormalizeText(value);
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : NormalizeComparablePath(normalized!);
    }

    private static string NormalizeComparablePath(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string? ReadStringProperty(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }
}

internal sealed record KernelConfigWritePathResolution(string? ReadPath, string WritePath);

/// <summary>
/// 批量配置写入项载体。
/// Carrier for one config-write batch item.
/// </summary>
internal sealed record KernelConfigWriteItem(string Key, string ValueJson, string? MergeStrategy);

/// <summary>
/// 配置文件变更后的结果载体。
/// Result snapshot after a persisted config mutation.
/// </summary>
internal sealed record KernelConfigWriteMutationResult(
    TomlTable Root,
    string FilePath,
    string Version);

/// <summary>
/// 配置写入阶段的结构化异常。
/// Structured exception for config-write failures.
/// </summary>
internal sealed class KernelConfigWriteException : Exception
{
    public KernelConfigWriteException(int code, string message, object? dataPayload = null)
        : base(message)
    {
        Code = code;
        DataPayload = dataPayload;
    }

    public int Code { get; }

    public object? DataPayload { get; }

    public static KernelConfigWriteException Validation(string message)
        => new(
            -32602,
            message,
            new
            {
                errorCode = "configValidationError",
            });

    public static KernelConfigWriteException VersionConflict()
        => new(
            -32600,
            "Configuration was modified since last read. Fetch latest version and retry.",
            new
            {
                errorCode = "configVersionConflict",
            });
}
