using System.Text;
using System.Text.Json;
using TianShu.AppHost.Configuration;
using TianShu.AppHost.State;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelSpawnAgentsOnCsvRuntimeHelpers
{
    private const int DefaultSpawnAgentsOnCsvConcurrency = 16;
    private const int MaxSpawnAgentsOnCsvConcurrency = 64;
    private const int DefaultAgentJobRuntimeTimeoutSeconds = 60 * 30;

    public static KernelSpawnAgentsOnCsvResponse BuildSpawnAgentsOnCsvResponse(
        KernelAgentJobRecord job,
        IReadOnlyList<KernelAgentJobItemRecord> items)
    {
        var completedItems = items.Count(item => string.Equals(item.Status, "completed", StringComparison.OrdinalIgnoreCase));
        var failedItems = items.Count(item => string.Equals(item.Status, "failed", StringComparison.OrdinalIgnoreCase));
        var failedSummaries = items
            .Where(item => string.Equals(item.Status, "failed", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.LastError))
            .Take(5)
            .Select(item => new KernelSpawnAgentsOnCsvFailureSummary(item.ItemId, item.SourceId, item.LastError!))
            .ToArray();
        return new KernelSpawnAgentsOnCsvResponse(
            JobId: job.Id,
            Status: job.Status,
            OutputCsvPath: job.OutputCsvPath,
            TotalItems: items.Count,
            CompletedItems: completedItems,
            FailedItems: failedItems,
            JobError: string.IsNullOrWhiteSpace(job.LastError) ? null : job.LastError,
            FailedItemErrors: failedSummaries.Length == 0 ? null : failedSummaries);
    }

    public static IReadOnlyList<string> ParseAgentJobHeaders(string inputHeadersJson)
    {
        if (string.IsNullOrWhiteSpace(inputHeadersJson))
        {
            return Array.Empty<string>();
        }

        using var document = JsonDocument.Parse(inputHeadersJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var headers = new List<string>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var value = element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
            if (value is not null)
            {
                headers.Add(value);
            }
        }

        return headers;
    }

    public static List<(string? ItemId, string? SourceId, string RowJson)> BuildAgentJobItems(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        int? idColumnIndex)
    {
        var items = new List<(string? ItemId, string? SourceId, string RowJson)>(rows.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count != headers.Count)
            {
                var rowIndex = i + 2;
                throw new InvalidOperationException($"csv row {rowIndex} has {row.Count} fields but header has {headers.Count}");
            }

            var sourceId = idColumnIndex.HasValue ? row[idColumnIndex.Value] : null;
            sourceId = string.IsNullOrWhiteSpace(sourceId) ? null : sourceId;
            var baseItemId = sourceId ?? $"row-{i + 1}";
            var itemId = baseItemId;
            var suffix = 2;
            while (!seenIds.Add(itemId))
            {
                itemId = $"{baseItemId}-{suffix}";
                suffix++;
            }

            var rowObject = new Dictionary<string, string>(headers.Count, StringComparer.Ordinal);
            for (var columnIndex = 0; columnIndex < headers.Count; columnIndex++)
            {
                rowObject[headers[columnIndex]] = row[columnIndex];
            }

            items.Add((itemId, sourceId, JsonSerializer.Serialize(rowObject)));
        }

        return items;
    }

    public static string BuildAgentJobWorkerPrompt(KernelAgentJobRecord job, KernelAgentJobItemRecord item)
    {
        using var rowDocument = JsonDocument.Parse(item.RowJson);
        var instruction = RenderAgentJobInstructionTemplate(job.Instruction, rowDocument.RootElement);
        var rowJson = JsonSerializer.Serialize(rowDocument.RootElement, new JsonSerializerOptions { WriteIndented = true });
        var outputSchema = string.IsNullOrWhiteSpace(job.OutputSchemaJson)
            ? "{}"
            : FormatAgentJobJson(job.OutputSchemaJson!);
        return $"You are processing one item for a generic agent job.\n" +
               $"Job ID: {job.Id}\n" +
               $"Item ID: {item.ItemId}\n\n" +
               "Task instruction:\n" +
               $"{instruction}\n\n" +
               "Input row (JSON):\n" +
               $"{rowJson}\n\n" +
               "Expected result schema (JSON Schema or {}):\n" +
               $"{outputSchema}\n\n" +
               "You MUST call the `report_agent_job_result` tool exactly once with:\n" +
               $"1. `job_id` = \"{job.Id}\"\n" +
               $"2. `item_id` = \"{item.ItemId}\"\n" +
               "3. `result` = a JSON object that contains your analysis result for this row.\n\n" +
               "If you need to stop the job early, include `stop` = true in the tool call.\n\n" +
               "After the tool call succeeds, stop.";
    }

    public static string RenderAgentJobInstructionTemplate(string instruction, JsonElement rowJson)
    {
        const string openBraceSentinel = "__TIANSHU_OPEN_BRACE__";
        const string closeBraceSentinel = "__TIANSHU_CLOSE_BRACE__";

        var rendered = instruction
            .Replace("{{", openBraceSentinel, StringComparison.Ordinal)
            .Replace("}}", closeBraceSentinel, StringComparison.Ordinal);
        if (rowJson.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in rowJson.EnumerateObject())
            {
                var replacement = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.GetRawText();
                rendered = rendered.Replace($"{{{property.Name}}}", replacement, StringComparison.Ordinal);
            }
        }

        return rendered
            .Replace(openBraceSentinel, "{", StringComparison.Ordinal)
            .Replace(closeBraceSentinel, "}", StringComparison.Ordinal);
    }

    public static string FormatAgentJobJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }

    public static int NormalizeSpawnAgentsOnCsvConcurrency(int? requested, int maxThreads)
    {
        var value = requested ?? DefaultSpawnAgentsOnCsvConcurrency;
        if (value < 1)
        {
            value = 1;
        }

        value = Math.Min(value, MaxSpawnAgentsOnCsvConcurrency);
        return Math.Min(value, Math.Max(maxThreads, 1));
    }

    public static bool IsSpawnAgentThreadLimitError(Exception ex)
        => ex is InvalidOperationException
           && ex.Message.StartsWith("agent thread limit reached (max ", StringComparison.Ordinal);

    public static int ResolveConfiguredAgentJobMaxRuntimeSeconds(Dictionary<string, object?> config)
    {
        if (!TryReadNestedValueExact(config, ["agents", "job_max_runtime_seconds"], out var rawValue))
        {
            return DefaultAgentJobRuntimeTimeoutSeconds;
        }

        if (rawValue is null)
        {
            return DefaultAgentJobRuntimeTimeoutSeconds;
        }

        if (!TryReadInt(rawValue, out var configuredValue) || configuredValue < 1)
        {
            throw new InvalidOperationException("agents.job_max_runtime_seconds must be at least 1");
        }

        return configuredValue;
    }

    public static int NormalizeSpawnAgentsOnCsvMaxRuntimeSeconds(int? requested)
    {
        if (!requested.HasValue)
        {
            return DefaultAgentJobRuntimeTimeoutSeconds;
        }

        if (requested.Value < 1)
        {
            throw new InvalidOperationException("max_runtime_seconds must be >= 1");
        }

        return requested.Value;
    }

    private static bool TryReadNestedValueExact(
        Dictionary<string, object?> config,
        IReadOnlyList<string> propertyPath,
        out object? value)
    {
        var current = config;
        for (var index = 0; index < propertyPath.Count; index++)
        {
            if (!current.TryGetValue(propertyPath[index], out value))
            {
                return false;
            }

            if (index == propertyPath.Count - 1)
            {
                return true;
            }

            if (!TryAsDictionary(value, out current))
            {
                value = null;
                return false;
            }
        }

        value = null;
        return false;
    }

    private static bool TryAsDictionary(object? value, out Dictionary<string, object?> dictionary)
    {
        switch (value)
        {
            case Dictionary<string, object?> concrete:
                dictionary = concrete;
                return true;
            case IReadOnlyDictionary<string, object?> readOnly:
                dictionary = readOnly.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case IDictionary<string, object?> mutable:
                dictionary = mutable.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                dictionary = pairs.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Object:
                dictionary = ConvertJsonObject(element);
                return true;
            default:
                dictionary = null!;
                return false;
        }
    }

    private static bool TryReadInt(object? value, out int intValue)
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

    private static Dictionary<string, object?> ConvertJsonObject(JsonElement element)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            dictionary[property.Name] = ConvertJsonValue(property.Value);
        }

        return dictionary;
    }

    private static object? ConvertJsonValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var intValue)
                ? intValue
                : element.TryGetDouble(out var doubleValue)
                    ? doubleValue
                    : element.GetRawText(),
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };

    public static TimeSpan ResolveAgentJobRuntimeTimeout(KernelAgentJobRecord job)
    {
        var seconds = job.MaxRuntimeSeconds.GetValueOrDefault(DefaultAgentJobRuntimeTimeoutSeconds);
        if (seconds < 1)
        {
            seconds = DefaultAgentJobRuntimeTimeoutSeconds;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    public static bool IsAgentJobItemStale(KernelAgentJobItemRecord item, TimeSpan runtimeTimeout)
    {
        var age = DateTimeOffset.UtcNow - item.UpdatedAt;
        return age >= runtimeTimeout;
    }

    public static string FormatAgentJobRuntimeTimeout(TimeSpan timeout)
        => $"{Math.Max(1, (int)Math.Round(timeout.TotalSeconds))}s";

    public static string ResolveAgentJobPath(string? cwd, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(string.IsNullOrWhiteSpace(cwd) ? Environment.CurrentDirectory : cwd!, path));
    }

    public static string BuildDefaultAgentJobOutputPath(string inputPath, string jobSuffix)
    {
        var directory = Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "agent_job_output";
        }

        return Path.Combine(directory, $"{stem}.agent-job-{jobSuffix}.csv");
    }

    public static void EnsureUniqueAgentJobHeaders(IReadOnlyList<string> headers)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var header in headers)
        {
            if (!seen.Add(header))
            {
                throw new InvalidOperationException($"csv header {header} is duplicated");
            }
        }
    }

    public static (List<string> Headers, List<IReadOnlyList<string>> Rows) ParseAgentJobCsv(string content)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var currentField = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    currentField.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    currentRow.Add(currentField.ToString());
                    currentField.Clear();
                    break;
                case '\r':
                    currentRow.Add(currentField.ToString());
                    currentField.Clear();
                    AppendAgentJobCsvRow(rows, currentRow);
                    currentRow = new List<string>();
                    if (i + 1 < content.Length && content[i + 1] == '\n')
                    {
                        i++;
                    }
                    break;
                case '\n':
                    currentRow.Add(currentField.ToString());
                    currentField.Clear();
                    AppendAgentJobCsvRow(rows, currentRow);
                    currentRow = new List<string>();
                    break;
                default:
                    currentField.Append(ch);
                    break;
            }
        }

        if (inQuotes)
        {
            throw new InvalidOperationException("failed to parse csv input: unterminated quoted field");
        }

        if (currentField.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentField.ToString());
            AppendAgentJobCsvRow(rows, currentRow);
        }

        if (rows.Count == 0)
        {
            return (new List<string>(), new List<IReadOnlyList<string>>());
        }

        var headers = rows[0];
        if (headers.Count > 0)
        {
            headers[0] = headers[0].TrimStart('\uFEFF');
        }

        return (headers, rows.Skip(1).Cast<IReadOnlyList<string>>().ToList());
    }

    public static string RenderAgentJobCsv(IReadOnlyList<string> headers, IReadOnlyList<KernelAgentJobItemRecord> items)
    {
        var outputHeaders = headers
            .Concat(
            [
                "job_id",
                "item_id",
                "row_index",
                "source_id",
                "status",
                "attempt_count",
                "last_error",
                "result_json",
                "reported_at",
                "completed_at",
            ])
            .ToArray();
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', outputHeaders.Select(EscapeAgentJobCsvValue)));
        foreach (var item in items.OrderBy(static item => item.RowIndex))
        {
            using var rowDocument = JsonDocument.Parse(item.RowJson);
            var rowObject = rowDocument.RootElement.ValueKind == JsonValueKind.Object
                ? rowDocument.RootElement
                : throw new InvalidOperationException($"row_json for item {item.ItemId} is not a JSON object");
            var values = new List<string>(headers.Count + 10);
            foreach (var header in headers)
            {
                values.Add(EscapeAgentJobCsvValue(ReadAgentJobCsvValue(rowObject, header)));
            }

            values.Add(EscapeAgentJobCsvValue(item.JobId));
            values.Add(EscapeAgentJobCsvValue(item.ItemId));
            values.Add(EscapeAgentJobCsvValue(item.RowIndex.ToString()));
            values.Add(EscapeAgentJobCsvValue(item.SourceId ?? string.Empty));
            values.Add(EscapeAgentJobCsvValue(item.Status));
            values.Add(EscapeAgentJobCsvValue(item.AttemptCount.ToString()));
            values.Add(EscapeAgentJobCsvValue(item.LastError ?? string.Empty));
            values.Add(EscapeAgentJobCsvValue(item.ResultJson ?? string.Empty));
            values.Add(EscapeAgentJobCsvValue(item.ReportedAt?.ToString("O") ?? string.Empty));
            values.Add(EscapeAgentJobCsvValue(item.CompletedAt?.ToString("O") ?? string.Empty));
            builder.AppendLine(string.Join(',', values));
        }

        return builder.ToString();
    }

    public static bool IsTerminalAgentJobItemStatus(string status)
        => string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase);

    private static void AppendAgentJobCsvRow(List<List<string>> rows, List<string> row)
    {
        if (row.Count == 0)
        {
            return;
        }

        if (row.All(static value => string.IsNullOrEmpty(value)))
        {
            return;
        }

        rows.Add(row);
    }

    private static string ReadAgentJobCsvValue(JsonElement rowObject, string header)
    {
        if (!rowObject.TryGetProperty(header, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.String => value.GetString() ?? string.Empty,
            _ => value.GetRawText(),
        };
    }

    private static string EscapeAgentJobCsvValue(string value)
    {
        if (value.Contains(',')
            || value.Contains('"')
            || value.Contains('\r')
            || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }
}
