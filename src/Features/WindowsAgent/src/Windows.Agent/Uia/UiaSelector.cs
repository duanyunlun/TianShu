namespace Windows.Agent.Uia;

public sealed record UiaSelector(
    string? AutomationId = null,
    string? Name = null,
    string? ClassName = null,
    string? ControlType = null)
{
    public static bool TryParse(string? selector, out UiaSelector? parsed, out string? error)
    {
        parsed = null;
        error = null;

        if (string.IsNullOrWhiteSpace(selector))
        {
            error = "selector is required";
            return false;
        }

        var parts = selector.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "selector is required";
            return false;
        }

        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts)
        {
            var idx = part.IndexOf('=', StringComparison.Ordinal);
            if (idx <= 0 || idx >= part.Length - 1)
            {
                error = $"Invalid selector segment: '{part}'. Expected 'key=value'.";
                return false;
            }

            var key = part[..idx].Trim();
            var value = Unquote(part[(idx + 1)..].Trim());

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                error = $"Invalid selector segment: '{part}'. Expected 'key=value'.";
                return false;
            }

            kv[key] = value;
        }

        var automationId = GetFirst(kv, "automationId", "aid");
        var name = GetFirst(kv, "name", "text");
        var className = GetFirst(kv, "className", "class");
        var controlType = GetFirst(kv, "controlType", "type");

        if (automationId == null && name == null && className == null && controlType == null)
        {
            error = "selector must include at least one of automationId/name/className/controlType";
            return false;
        }

        parsed = new UiaSelector(automationId, name, className, controlType);
        return true;
    }

    private static string? GetFirst(IReadOnlyDictionary<string, string> kv, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (kv.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
        {
            return value[1..^1];
        }

        return value;
    }
}

