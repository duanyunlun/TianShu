using System.Text.Json;

namespace TianShu.AppHost.Tools.Runtime;

internal static class ProviderOutputItemPresentationPolicy
{
    private static readonly IReadOnlyDictionary<string, ProviderOutputItemPresentationDescriptor> Descriptors =
        new Dictionary<string, ProviderOutputItemPresentationDescriptor>(StringComparer.Ordinal)
        {
            ["filechange"] = new(static _ => true),
            ["localshellcall"] = new(static _ => true),
            ["toolsearchcall"] = new(IsServerExecutedToolSearchCall),
        };

    public static bool ShouldEmitLifecycleNotification(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var itemType = Normalize(KernelToolJsonHelpers.ReadString(item, "type"));
        return itemType is not null
               && Descriptors.TryGetValue(itemType, out var descriptor)
               && descriptor.ShouldEmit(item);
    }

    private static bool IsServerExecutedToolSearchCall(JsonElement item)
    {
        var execution = Normalize(KernelToolJsonHelpers.ReadString(item, "execution"));
        return string.Equals(execution, "server", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private sealed record ProviderOutputItemPresentationDescriptor(Func<JsonElement, bool> ShouldEmit);
}
