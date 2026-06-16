using System.Text.Json;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli;

internal sealed class ProbeUserInputScript
{
    private readonly Dictionary<string, ControlPlaneUserInputSubmission> requestAnswers;
    private readonly ControlPlaneUserInputSubmission? defaultAnswers;

    private ProbeUserInputScript(
        string sourcePath,
        Dictionary<string, ControlPlaneUserInputSubmission> requestAnswers,
        ControlPlaneUserInputSubmission? defaultAnswers)
    {
        SourcePath = sourcePath;
        this.requestAnswers = requestAnswers;
        this.defaultAnswers = defaultAnswers;
    }

    public string SourcePath { get; }

    public static ProbeUserInputScript? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"用户补录 JSON 不存在：{fullPath}", fullPath);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("用户补录 JSON 的根节点必须是对象。支持直接答案对象，或使用 requests/defaultAnswers 包装。");
        }

        var root = document.RootElement;
        var requestAnswers = new Dictionary<string, ControlPlaneUserInputSubmission>(StringComparer.Ordinal);
        ControlPlaneUserInputSubmission? defaultAnswers = null;

        root.TryGetProperty("requests", out var requestsElement);
        root.TryGetProperty("defaultAnswers", out var defaultAnswersElement);
        root.TryGetProperty("answers", out var answersElement);
        var hasEnvelope = requestsElement.ValueKind != JsonValueKind.Undefined
            || defaultAnswersElement.ValueKind != JsonValueKind.Undefined
            || answersElement.ValueKind != JsonValueKind.Undefined;

        if (requestsElement.ValueKind != JsonValueKind.Undefined)
        {
            if (requestsElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("user-input-json 中的 requests 必须是对象，其键为 callId，值为答案对象。");
            }

            foreach (var property in requestsElement.EnumerateObject())
            {
                requestAnswers[property.Name] = ConvertAnswersObject(property.Value, $"requests.{property.Name}");
            }
        }

        if (defaultAnswersElement.ValueKind != JsonValueKind.Undefined)
        {
            defaultAnswers = ConvertAnswersObject(defaultAnswersElement, "defaultAnswers");
        }
        else if (answersElement.ValueKind != JsonValueKind.Undefined)
        {
            defaultAnswers = ConvertAnswersObject(answersElement, "answers");
        }
        else if (!hasEnvelope)
        {
            defaultAnswers = ConvertAnswersObject(root, "root");
        }

        if (requestAnswers.Count == 0 && defaultAnswers is null)
        {
            throw new FormatException("用户补录 JSON 中未找到可用答案。请提供答案对象，或使用 requests/defaultAnswers 包装。");
        }

        return new ProbeUserInputScript(fullPath, requestAnswers, defaultAnswers);
    }

    public bool TryResolveAnswers(string callId, out ControlPlaneUserInputSubmission answers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);

        if (requestAnswers.TryGetValue(callId, out var requestSpecificAnswers))
        {
            answers = CliGovernanceEnvelopeFactory.Normalize(CloneAnswers(callId, requestSpecificAnswers));
            return true;
        }

        if (defaultAnswers is not null)
        {
            answers = CliGovernanceEnvelopeFactory.Normalize(CloneAnswers(callId, defaultAnswers));
            return true;
        }

        answers = CliGovernanceEnvelopeFactory.Normalize(new ControlPlaneUserInputSubmission
        {
            CallId = new CallId(callId),
            Answers = new Dictionary<string, StructuredValue>(StringComparer.Ordinal),
        });
        return false;
    }

    public static ControlPlaneUserInputSubmission ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ConvertAnswersObject(document.RootElement, "inline");
    }

    private static ControlPlaneUserInputSubmission ConvertAnswersObject(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"{context} 必须是对象。");
        }

        var dictionary = new Dictionary<string, StructuredValue>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            dictionary[property.Name] = ConvertStructuredValue(property.Value);
        }

        return new ControlPlaneUserInputSubmission
        {
            Answers = dictionary,
        };
    }

    private static ControlPlaneUserInputSubmission CloneAnswers(string callId, ControlPlaneUserInputSubmission source)
        => source with
        {
            CallId = new CallId(callId),
            Answers = source.Answers.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal),
        };

    private static StructuredValue ConvertStructuredValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => StructuredValue.FromObject(
                element.EnumerateObject().ToDictionary(
                    static property => property.Name,
                    static property => ConvertStructuredValue(property.Value),
                    StringComparer.Ordinal)),
            JsonValueKind.Array => StructuredValue.FromArray(
                element.EnumerateArray().Select(ConvertStructuredValue).ToArray()),
            JsonValueKind.String => StructuredValue.FromString(element.GetString() ?? string.Empty),
            JsonValueKind.Number => StructuredValue.FromNumber(element.GetRawText()),
            JsonValueKind.True => StructuredValue.FromBoolean(true),
            JsonValueKind.False => StructuredValue.FromBoolean(false),
            JsonValueKind.Null or JsonValueKind.Undefined => StructuredValue.Null,
            _ => StructuredValue.FromString(element.GetRawText()),
        };
}
