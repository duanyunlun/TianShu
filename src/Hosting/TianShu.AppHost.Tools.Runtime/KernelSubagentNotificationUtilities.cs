using System.Text.Json.Nodes;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelSubagentNotificationUtilities
{
    private const string OpenTag = "<subagent_notification>";
    private const string CloseTag = "</subagent_notification>";

    public static string Format(string agentId, JsonNode? status)
    {
        var payload = new JsonObject
        {
            ["agent_id"] = agentId,
            ["status"] = status?.DeepClone(),
        };

        return $"{OpenTag}\n{payload.ToJsonString()}\n{CloseTag}";
    }

    public static bool IsNotificationHistoryItem(KernelConversationHistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!string.Equals(KernelToolJsonHelpers.Normalize(item.Role), "user", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsNotificationText(item.Content);
    }

    public static bool IsNotificationText(string? text)
    {
        var normalized = KernelToolJsonHelpers.Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var trimmed = normalized!.Trim();
        return trimmed.StartsWith(OpenTag, StringComparison.OrdinalIgnoreCase)
            && trimmed.EndsWith(CloseTag, StringComparison.OrdinalIgnoreCase);
    }
}
