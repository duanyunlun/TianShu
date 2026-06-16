using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelReviewOutputParity
{
    internal const string ReviewFallbackMessage = "Reviewer failed to output a response.";

    internal static KernelJsonSchemaPayload CreateOutputSchema()
        => KernelJsonSchemaPayload.FromElement(JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                findings = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            title = new { type = "string" },
                            body = new { type = "string" },
                            confidence_score = new { type = "number" },
                            priority = new { type = "integer" },
                            code_location = new
                            {
                                type = "object",
                                properties = new
                                {
                                    absolute_file_path = new { type = "string" },
                                    line_range = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            start = new { type = "integer" },
                                            end = new { type = "integer" },
                                        },
                                        required = new[] { "start", "end" },
                                        additionalProperties = false,
                                    },
                                },
                                required = new[] { "absolute_file_path", "line_range" },
                                additionalProperties = false,
                            },
                        },
                        required = new[] { "title", "body", "confidence_score", "priority", "code_location" },
                        additionalProperties = false,
                    },
                },
                overall_correctness = new { type = "string" },
                overall_explanation = new { type = "string" },
                overall_confidence_score = new { type = "number" },
            },
            required = new[] { "findings", "overall_correctness", "overall_explanation", "overall_confidence_score" },
            additionalProperties = false,
        }));

    internal static KernelReviewOutputEvent ParseReviewOutput(string? text)
    {
        var rawText = text ?? string.Empty;
        if (TryParseReviewOutput(rawText, out var parsed))
        {
            return parsed;
        }

        var start = rawText.IndexOf('{');
        var end = rawText.LastIndexOf('}');
        if (start >= 0
            && end > start
            && TryParseReviewOutput(rawText[start..(end + 1)], out parsed))
        {
            return parsed;
        }

        return new KernelReviewOutputEvent
        {
            OverallExplanation = rawText,
        };
    }

    internal static string RenderReviewOutputText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ReviewFallbackMessage;
        }

        return RenderReviewOutputText(ParseReviewOutput(text));
    }

    internal static string RenderReviewOutputText(KernelReviewOutputEvent output)
    {
        var sections = new List<string>();
        var explanation = output.OverallExplanation.Trim();
        if (!string.IsNullOrEmpty(explanation))
        {
            sections.Add(explanation);
        }

        if (output.Findings.Count > 0)
        {
            var findings = FormatReviewFindingsBlock(output.Findings);
            var trimmed = findings.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                sections.Add(trimmed);
            }
        }

        return sections.Count == 0
            ? ReviewFallbackMessage
            : string.Join("\n\n", sections);
    }

    private static bool TryParseReviewOutput(string text, out KernelReviewOutputEvent output)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<KernelReviewOutputEvent>(text);
            if (parsed is not null)
            {
                output = parsed;
                return true;
            }
        }
        catch (JsonException)
        {
        }

        output = default!;
        return false;
    }

    private static string FormatReviewFindingsBlock(IReadOnlyList<KernelReviewFinding> findings)
    {
        var lines = new List<string>
        {
            string.Empty,
            findings.Count > 1 ? "Full review comments:" : "Review comment:",
        };

        foreach (var finding in findings)
        {
            lines.Add(string.Empty);
            lines.Add($"- {finding.Title} — {FormatLocation(finding)}");

            foreach (var bodyLine in finding.Body.Split('\n'))
            {
                lines.Add($"  {bodyLine.TrimEnd('\r')}");
            }
        }

        return string.Join("\n", lines);
    }

    private static string FormatLocation(KernelReviewFinding finding)
        => $"{finding.CodeLocation.AbsoluteFilePath}:{finding.CodeLocation.LineRange.Start}-{finding.CodeLocation.LineRange.End}";
}

internal sealed class KernelReviewOutputEvent
{
    [JsonPropertyName("findings")]
    public List<KernelReviewFinding> Findings { get; init; } = [];

    [JsonPropertyName("overall_correctness")]
    public string OverallCorrectness { get; init; } = string.Empty;

    [JsonPropertyName("overall_explanation")]
    public string OverallExplanation { get; init; } = string.Empty;

    [JsonPropertyName("overall_confidence_score")]
    public float OverallConfidenceScore { get; init; }
}

internal sealed class KernelReviewFinding
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("confidence_score")]
    public float ConfidenceScore { get; init; }

    [JsonPropertyName("priority")]
    public int Priority { get; init; }

    [JsonPropertyName("code_location")]
    public KernelReviewCodeLocation CodeLocation { get; init; } = new();
}

internal sealed class KernelReviewCodeLocation
{
    [JsonPropertyName("absolute_file_path")]
    public string AbsoluteFilePath { get; init; } = string.Empty;

    [JsonPropertyName("line_range")]
    public KernelReviewLineRange LineRange { get; init; } = new();
}

internal sealed class KernelReviewLineRange
{
    [JsonPropertyName("start")]
    public int Start { get; init; }

    [JsonPropertyName("end")]
    public int End { get; init; }
}
