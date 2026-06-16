using TianShu.Configuration;
using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private IEnumerable<string> ExistingConfigObjectIds(string prefix)
        => allFields
            .Where(static field => field.IsConfigured)
            .Select(field => TryExtractConfigObjectId(field.Key, prefix))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase);

    private string CreateUniqueConfigObjectId(string prefix, string baseId)
    {
        var existingIds = ExistingConfigObjectIds(prefix).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? baseId : $"{baseId}-{index + 1}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private string? ResolveSelectedConfigObjectId(string prefix)
        => selectedField is null ? null : TryExtractConfigObjectId(selectedField.Key, prefix);

    private List<ConfigurationChange> CloneConfigObjectChanges(string prefix, string sourceId, string targetId)
    {
        var sourcePrefix = $"{prefix}{sourceId}.";
        var targetPrefix = $"{prefix}{targetId}.";
        return allFields
            .Where(field => field.IsConfigured && field.Key.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
            .Select(field => new ConfigurationChange
            {
                Operation = ConfigurationChangeOperation.Set,
                Key = targetPrefix + field.Key[sourcePrefix.Length..],
                Value = ParseClonedFieldValue(field),
            })
            .ToList();
    }

    private static StructuredValue ParseClonedFieldValue(ConfigFieldRow field)
    {
        try
        {
            return TianShuConfigurationTomlChangeApplier.ParseUserInput(field.CurrentValue, field.ValueKind);
        }
        catch (Exception) when (field.ValueKind == ConfigurationValueKind.Array)
        {
            return ParseDisplayedArrayValue(field.CurrentValue);
        }
    }

    private static StructuredValue ParseDisplayedArrayValue(string value)
    {
        var text = value.Trim();
        if (text.Length >= 2 && text[0] == '[' && text[^1] == ']')
        {
            text = text[1..^1];
        }

        return ArrayValue(
            text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(static item => item.Trim().Trim('"'))
                .ToArray());
    }

    private static string? TryExtractConfigObjectId(string key, string prefix)
    {
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = key[prefix.Length..];
        var dotIndex = rest.IndexOf('.', StringComparison.Ordinal);
        return dotIndex <= 0 ? null : rest[..dotIndex];
    }
}
