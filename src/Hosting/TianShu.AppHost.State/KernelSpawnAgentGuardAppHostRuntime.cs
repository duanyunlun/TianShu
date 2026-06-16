using System.Text.Json;
using TianShu.Execution.Runtime;

namespace TianShu.AppHost.State;

internal sealed class KernelSpawnAgentGuardAppHostRuntime
{
    private const int DefaultSpawnAgentMaxThreads = 6;
    private const int DefaultSpawnAgentMaxDepth = 1;

    private readonly KernelSpawnAgentGuardState spawnAgentGuardState;
    private readonly Func<string?, CancellationToken, Task<Dictionary<string, object?>>> loadConfigAsync;
    private readonly Action<string> releaseSpawnAgentNicknameReservation;

    public KernelSpawnAgentGuardAppHostRuntime(
        KernelSpawnAgentGuardState spawnAgentGuardState,
        Func<string?, CancellationToken, Task<Dictionary<string, object?>>> loadConfigAsync,
        Action<string> releaseSpawnAgentNicknameReservation)
    {
        this.spawnAgentGuardState = spawnAgentGuardState;
        this.loadConfigAsync = loadConfigAsync;
        this.releaseSpawnAgentNicknameReservation = releaseSpawnAgentNicknameReservation;
    }

    public async Task<KernelSpawnAgentGuardConfiguration> ResolveSpawnAgentGuardConfigurationAsync(
        string? cwd,
        CancellationToken cancellationToken)
    {
        var effectiveCwd = Normalize(cwd) ?? Environment.CurrentDirectory;
        var config = await loadConfigAsync(effectiveCwd, cancellationToken).ConfigureAwait(false);

        return new KernelSpawnAgentGuardConfiguration(
            ResolveConfiguredSpawnAgentPositiveInt(
                config,
                ["agents", "max_threads"],
                DefaultSpawnAgentMaxThreads,
                "agents.max_threads"),
            ResolveConfiguredSpawnAgentPositiveInt(
                config,
                ["agents", "max_depth"],
                DefaultSpawnAgentMaxDepth,
                "agents.max_depth"));
    }

    public KernelSpawnSlotReservation ReserveSpawnAgentSlot(int maxThreads)
    {
        spawnAgentGuardState.Reserve(maxThreads);
        return new KernelSpawnSlotReservation(spawnAgentGuardState);
    }

    public bool IsTrackedSpawnAgentThread(string threadId)
        => spawnAgentGuardState.IsTracked(threadId);

    public void ReleaseSpawnedAgentThread(string threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        spawnAgentGuardState.ReleaseTracked(normalizedThreadId);
        releaseSpawnAgentNicknameReservation(normalizedThreadId);
    }

    public static int GetNextThreadSpawnDepth(KernelSessionSource? sessionSource)
        => Math.Max(sessionSource?.GetThreadSpawnDepth() ?? 0, 0) + 1;

    public static int ResolveConfiguredSpawnAgentPositiveInt(
        Dictionary<string, object?> config,
        string[] propertyPath,
        int defaultValue,
        string configKey)
    {
        if (!TryReadConfiguredNestedValue(config, propertyPath, out var rawValue) || rawValue is null)
        {
            return defaultValue;
        }

        if (!TryReadConfiguredInt(rawValue, out var configuredValue) || configuredValue < 1)
        {
            throw new InvalidOperationException($"{configKey} must be at least 1");
        }

        return configuredValue;
    }

    private static bool TryReadConfiguredNestedValue(
        Dictionary<string, object?> config,
        IReadOnlyList<string> propertyPath,
        out object? value)
    {
        object? current = config;
        for (var index = 0; index < propertyPath.Count; index++)
        {
            if (!TryAsDictionary(current, out var currentDictionary))
            {
                value = null;
                return false;
            }

            if (!TryGetCompatibleValue(currentDictionary, propertyPath[index], out current))
            {
                value = null;
                return false;
            }
        }

        value = current;
        return true;
    }

    private static bool TryGetCompatibleValue(
        Dictionary<string, object?> dictionary,
        string propertyName,
        out object? value)
    {
        foreach (var candidateName in EnumerateCompatiblePropertyNames(propertyName))
        {
            if (dictionary.TryGetValue(candidateName, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    private static IEnumerable<string> EnumerateCompatiblePropertyNames(string propertyName)
    {
        yield return propertyName;

        if (propertyName.Contains('_', StringComparison.Ordinal))
        {
            var camelCase = ToCamelCase(propertyName);
            if (!string.Equals(camelCase, propertyName, StringComparison.Ordinal))
            {
                yield return camelCase;
            }
        }
    }

    private static bool TryAsDictionary(object? value, out Dictionary<string, object?> dictionary)
    {
        if (value is Dictionary<string, object?> typedDictionary)
        {
            dictionary = typedDictionary;
            return true;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            dictionary = JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText())
                ?? new Dictionary<string, object?>(StringComparer.Ordinal);
            return true;
        }

        dictionary = null!;
        return false;
    }

    private static bool TryReadConfiguredInt(object? value, out int intValue)
    {
        switch (value)
        {
            case int typedInt:
                intValue = typedInt;
                return true;
            case long typedLong when typedLong is >= int.MinValue and <= int.MaxValue:
                intValue = (int)typedLong;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var parsedInt):
                intValue = parsedInt;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsedIntFromString):
                intValue = parsedIntFromString;
                return true;
            case string text when int.TryParse(text, out var parsedIntFromText):
                intValue = parsedIntFromText;
                return true;
            default:
                intValue = default;
                return false;
        }
    }

    private static string ToCamelCase(string value)
    {
        var buffer = new List<char>(value.Length);
        var uppercaseNext = false;
        foreach (var ch in value)
        {
            if (ch == '_')
            {
                uppercaseNext = true;
                continue;
            }

            buffer.Add(uppercaseNext ? char.ToUpperInvariant(ch) : ch);
            uppercaseNext = false;
        }

        return new string(buffer.ToArray());
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
