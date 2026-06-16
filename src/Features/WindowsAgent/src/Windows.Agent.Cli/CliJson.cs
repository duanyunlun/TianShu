using System.Text.Json;

namespace Windows.Agent.Cli;

internal static class CliJson
{
    public static string Serialize(object payload, bool pretty)
    {
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = pretty
        });
    }

    public static bool TryParse(string text, out JsonElement parsed)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            parsed = doc.RootElement.Clone();
            return true;
        }
        catch
        {
            parsed = default;
            return false;
        }
    }
}
