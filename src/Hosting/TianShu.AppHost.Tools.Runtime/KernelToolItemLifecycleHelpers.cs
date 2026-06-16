using System.Text;
using System.Text.Json;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelMcpToolLifecycleDescriptor(string Server, string Tool);

internal sealed record WebSearchLifecycleObservation(
    string CallId,
    string Query,
    JsonElement? Action);

internal sealed record ImageGenerationLifecycleObservation(
    string CallId,
    string Status,
    string? RevisedPrompt,
    string Result,
    string? SavedPath,
    JsonElement RawItem);

internal static class KernelToolItemLifecycleHelpers
{
    public static object[]? BuildDynamicToolContentItems(KernelToolResult result)
    {
        if (result.OutputContentItems is null || result.OutputContentItems.Count == 0)
        {
            return null;
        }

        return result.OutputContentItems.Select(static item =>
        {
            var normalizedType = KernelToolJsonHelpers.Normalize(item.Type) ?? "input_text";
            return normalizedType switch
            {
                "input_image" => (object)new
                {
                    type = "inputImage",
                    imageUrl = item.ImageUrl ?? string.Empty,
                },
                _ => new
                {
                    type = "inputText",
                    text = item.Text ?? string.Empty,
                },
            };
        }).ToArray();
    }

    public static object[] BuildFileChangeChanges(string toolName, JsonElement arguments, string cwd)
    {
        if (string.Equals(toolName, "apply_patch", StringComparison.Ordinal))
        {
            var patch = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "input"));
            if (string.IsNullOrWhiteSpace(patch))
            {
                return Array.Empty<object>();
            }

            try
            {
                return KernelApplyPatch
                    .DescribeChanges(patch!, cwd)
                    .Select(static change => (object)new
                    {
                        path = change.Path,
                        kind = change.Kind,
                        diff = change.Diff,
                    })
                    .ToArray();
            }
            catch
            {
                return Array.Empty<object>();
            }
        }

        if (string.Equals(toolName, "write", StringComparison.Ordinal))
        {
            var rawPath = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "path"));
            var content = KernelToolJsonHelpers.ReadString(arguments, "content") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return Array.Empty<object>();
            }

            var fullPath = Path.IsPathRooted(rawPath)
                ? rawPath!
                : Path.GetFullPath(Path.Combine(cwd, rawPath!));
            var append = KernelToolJsonHelpers.ReadBool(arguments, "append") ?? false;
            var kind = !File.Exists(fullPath) ? "add" : "update";
            if (append)
            {
                kind = "update";
            }

            return
            [
                new
                {
                    path = fullPath,
                    kind,
                    diff = content,
                },
            ];
        }

        return Array.Empty<object>();
    }

    public static string? ResolveImageViewPath(JsonElement arguments, string cwd)
    {
        var rawPath = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "path"));
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(Path.IsPathRooted(rawPath)
                ? rawPath
                : Path.Combine(cwd, rawPath));
        }
        catch
        {
            return rawPath;
        }
    }

    public static string TryGetCommandExecutionStatusFromExitCode(int exitCode)
        => exitCode == 0 ? "completed" : "failed";

    public static string? BuildCommandExecutionAggregatedOutput(string? stdout, string? stderr)
    {
        var hasStdout = !string.IsNullOrWhiteSpace(stdout);
        var hasStderr = !string.IsNullOrWhiteSpace(stderr);
        if (!hasStdout && !hasStderr)
        {
            return null;
        }

        if (!hasStdout)
        {
            return stderr;
        }

        if (!hasStderr)
        {
            return stdout;
        }

        if (stdout!.EndsWith("\n", StringComparison.Ordinal) || stdout.EndsWith("\r", StringComparison.Ordinal))
        {
            return stdout + stderr;
        }

        return stdout + Environment.NewLine + stderr;
    }

    public static object BuildCommandExecutionItemPayload(
        string itemId,
        string command,
        string cwd,
        string? processId,
        string status,
        string? aggregatedOutput,
        int? exitCode,
        long? durationMs)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = itemId,
            ["type"] = "commandExecution",
            ["command"] = command,
            ["cwd"] = cwd,
            ["processId"] = processId,
            ["status"] = status,
            ["commandActions"] = Array.Empty<object>(),
            ["aggregatedOutput"] = aggregatedOutput,
            ["exitCode"] = exitCode,
            ["durationMs"] = durationMs,
        };
    }

    public static bool TryCreateMcpToolLifecycleDescriptor(
        IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools,
        string toolName,
        out KernelMcpToolLifecycleDescriptor descriptor)
    {
        descriptor = null!;
        if (!KernelDynamicToolResolver.TryResolveDescriptor(dynamicTools, toolName, out var dynamicToolDescriptor))
        {
            return false;
        }

        var server = KernelToolJsonHelpers.Normalize(dynamicToolDescriptor.ApprovalServerName);
        if (string.IsNullOrWhiteSpace(server)
            || string.Equals(server, "dynamic", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        descriptor = new KernelMcpToolLifecycleDescriptor(server!, toolName);
        return true;
    }

    public static object CreateMcpToolCallItem(
        string itemId,
        string server,
        string tool,
        string status,
        JsonElement arguments,
        object? resultPayload,
        object? errorPayload,
        long? durationMs)
    {
        return new
        {
            id = itemId,
            type = "mcpToolCall",
            server,
            tool,
            status,
            arguments,
            result = resultPayload,
            error = errorPayload,
            durationMs,
        };
    }

    public static object CreateMcpToolCallResultPayload(KernelToolResult result)
    {
        var payload = new Dictionary<string, object?>
        {
            ["content"] = BuildMcpToolResultContent(result),
            ["isError"] = !result.Success,
        };
        if (result.StructuredOutput is not null)
        {
            payload["structuredContent"] = ConvertJsonElementToObject(result.StructuredOutput);
        }

        if (result.Metadata is not null)
        {
            payload["_meta"] = ConvertJsonElementToObject(result.Metadata);
        }

        return payload;
    }

    public static object[] BuildMcpToolResultContent(KernelToolResult result)
    {
        if (result.RawOutputContentItems is { Count: > 0 } rawItems)
        {
            return rawItems
                .Select(static item => ConvertJsonElementToObject(item) ?? new { })
                .ToArray();
        }

        if (string.IsNullOrWhiteSpace(result.OutputText))
        {
            return [];
        }

        return
        [
            new
            {
                type = "text",
                text = result.OutputText,
            },
        ];
    }

    public static IReadOnlyList<WebSearchLifecycleObservation> CaptureWebSearchOutputItems(IEnumerable<JsonElement> outputItems)
    {
        var observations = new List<WebSearchLifecycleObservation>();
        foreach (var item in outputItems)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "type"));
            if (!string.Equals(type, "web_search_call", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var callId = KernelToolJsonHelpers.Normalize(
                KernelToolJsonHelpers.ReadString(item, "id")
                ?? KernelToolJsonHelpers.ReadString(item, "call_id"));
            if (string.IsNullOrWhiteSpace(callId))
            {
                continue;
            }

            JsonElement? action = null;
            if (item.TryGetProperty("action", out var actionValue) && actionValue.ValueKind == JsonValueKind.Object)
            {
                action = actionValue.Clone();
            }

            var query = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "query"))
                ?? ExtractWebSearchQuery(action)
                ?? string.Empty;

            observations.Add(new WebSearchLifecycleObservation(callId!, query, action));
        }

        return observations;
    }

    public static object BuildWebSearchNotificationItem(WebSearchLifecycleObservation observation)
        => new
        {
            type = "webSearch",
            id = observation.CallId,
            query = observation.Query,
            action = observation.Action,
        };

    public static async Task<IReadOnlyList<ImageGenerationLifecycleObservation>> CaptureImageGenerationOutputItemsAsync(
        IEnumerable<JsonElement> outputItemsDone,
        string? cwd,
        CancellationToken cancellationToken)
    {
        var normalizedCwd = KernelToolJsonHelpers.Normalize(cwd);
        var observations = new List<ImageGenerationLifecycleObservation>();
        foreach (var item in outputItemsDone)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "type"));
            if (!string.Equals(type, "image_generation_call", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var callId = KernelToolJsonHelpers.Normalize(
                KernelToolJsonHelpers.ReadString(item, "id")
                ?? KernelToolJsonHelpers.ReadString(item, "call_id"));
            if (string.IsNullOrWhiteSpace(callId))
            {
                continue;
            }

            var status = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "status")) ?? string.Empty;
            var revisedPrompt = KernelToolJsonHelpers.Normalize(
                KernelToolJsonHelpers.ReadString(item, "revised_prompt")
                ?? KernelToolJsonHelpers.ReadString(item, "revisedPrompt"));
            var result = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "result")) ?? string.Empty;
            string? savedPath = null;

            if (!string.IsNullOrWhiteSpace(normalizedCwd) && !string.IsNullOrWhiteSpace(result))
            {
                try
                {
                    savedPath = await SaveImageGenerationResultToCwdAsync(
                        normalizedCwd!,
                        callId!,
                        result,
                        cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    savedPath = null;
                }
            }

            observations.Add(new ImageGenerationLifecycleObservation(
                callId!,
                status,
                revisedPrompt,
                result,
                savedPath,
                item.Clone()));
        }

        return observations;
    }

    public static async Task<string> SaveImageGenerationResultToCwdAsync(
        string cwd,
        string callId,
        string base64Result,
        CancellationToken cancellationToken)
    {
        var safeFileName = SanitizeGeneratedImageFileName(callId);
        Directory.CreateDirectory(cwd);

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64Result);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("image_generation result must be standard base64.", ex);
        }

        var path = Path.Combine(cwd, safeFileName + ".png");
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static string? ExtractWebSearchQuery(JsonElement? action)
    {
        if (!action.HasValue || action.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(action.Value, "query"))
            ?? KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(action.Value, "url"))
            ?? KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(action.Value, "pattern"));
    }

    public static object? ConvertJsonElementToObject(JsonElement? element)
    {
        if (element is not JsonElement value)
        {
            return null;
        }

        return value.Clone();
    }

    private static string SanitizeGeneratedImageFileName(string callId)
    {
        var builder = new StringBuilder(callId.Length);
        foreach (var ch in callId)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            {
                builder.Append(ch);
            }
        }

        var sanitized = builder.ToString().Trim('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "image_generation" : sanitized;
    }
}
