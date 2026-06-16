using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Windows.Agent.Contracts;

public static class UiContractLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IDeserializer YamlDeserializer =
        new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    public static async Task<UiContract> LoadFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Contract path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Contract file not found.", path);
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var text = await File.ReadAllTextAsync(path, cancellationToken);

        UiContract? contract = ext switch
        {
            ".json" => LoadJson(text),
            ".yml" or ".yaml" => LoadYaml(text),
            _ => TryLoadAuto(text)
        };

        if (contract == null)
        {
            throw new InvalidOperationException("Failed to parse UI contract.");
        }

        return contract;
    }

    private static UiContract? TryLoadAuto(string text)
    {
        // 先尝试 JSON，再尝试 YAML（便于用户不写扩展名）。
        return LoadJson(text) ?? LoadYaml(text);
    }

    private static UiContract? LoadJson(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<UiContract>(text, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static UiContract? LoadYaml(string text)
    {
        try
        {
            return YamlDeserializer.Deserialize<UiContract>(text);
        }
        catch
        {
            return null;
        }
    }
}

