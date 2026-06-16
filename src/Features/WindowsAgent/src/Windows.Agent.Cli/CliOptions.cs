using System.Globalization;

namespace Windows.Agent.Cli;

internal sealed class CliOptions
{
    private readonly Dictionary<string, string?> _values;

    private CliOptions(Dictionary<string, string?> values)
    {
        _values = values;
    }

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = token[2..];
            string? value = null;

            var equalsIndex = key.IndexOf('=', StringComparison.Ordinal);
            if (equalsIndex >= 0)
            {
                value = key[(equalsIndex + 1)..];
                key = key[..equalsIndex];
            }
            else
            {
                var hasNext = i + 1 < args.Count;
                if (hasNext && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[i + 1];
                    i++;
                }
                else
                {
                    value = null; // flag
                }
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            values[key] = value;
        }

        return new CliOptions(values);
    }

    public bool Has(string key) => _values.ContainsKey(key);

    public IReadOnlyDictionary<string, string?> ToDictionary()
        => new Dictionary<string, string?>(_values, StringComparer.OrdinalIgnoreCase);

    public string? GetString(string key, string? defaultValue = null)
    {
        if (!_values.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        return value ?? defaultValue ?? "true";
    }

    public string RequireString(string key)
    {
        var value = GetString(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing required option: --{key}");
        }
        return value;
    }

    public int GetInt(string key, int defaultValue)
    {
        var value = GetString(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"Invalid integer for --{key}: '{value}'");
        }

        return parsed;
    }

    public int RequireInt(string key)
    {
        var value = RequireString(key);
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"Invalid integer for --{key}: '{value}'");
        }
        return parsed;
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        if (!_values.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        if (value == null)
        {
            return true; // flag present
        }

        if (bool.TryParse(value, out var b))
        {
            return b;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("y", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
