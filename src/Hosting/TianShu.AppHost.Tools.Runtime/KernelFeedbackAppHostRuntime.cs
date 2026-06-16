using System.Text.Json;
using TianShu.AppHost.State;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// feedback/upload 宿主运行时。
/// Host runtime for feedback/upload.
/// </summary>
internal sealed class KernelFeedbackAppHostRuntime
{
    private readonly KernelThreadStore threadStore;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private readonly Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync;

    public KernelFeedbackAppHostRuntime(
        KernelThreadStore threadStore,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync)
    {
        this.threadStore = threadStore;
        this.writeResultAsync = writeResultAsync;
        this.writeErrorAsync = writeErrorAsync;
    }

    public async Task HandleFeedbackUploadAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var classification = Normalize(ReadString(@params, "classification"));
        if (string.IsNullOrWhiteSpace(classification))
        {
            await writeErrorAsync(id, -32602, "classification 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var includeLogs = ReadBool(@params, "includeLogs");
        if (includeLogs is null)
        {
            await writeErrorAsync(id, -32602, "includeLogs 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var threadId = Normalize(ReadString(@params, "threadId"));
        if (!string.IsNullOrWhiteSpace(threadId))
        {
            var thread = await threadStore.GetThreadAsync(threadId!, cancellationToken).ConfigureAwait(false);
            if (thread is null)
            {
                await writeErrorAsync(id, -32600, $"invalid threadId: {threadId}", cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        var reason = Normalize(ReadString(@params, "reason"));
        var trackingThreadId = threadId ?? $"feedback_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}";
        var extraLogFiles = KernelToolJsonHelpers.ReadStringArray(@params, "extraLogFiles")
            .Select(static x => Normalize(x))
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => Path.GetFullPath(x!))
            .ToArray();

        var storage = KernelStoragePaths.ResolveDefault();
        var feedbackRoot = Path.Combine(storage.StateDirectory, "feedback");
        Directory.CreateDirectory(feedbackRoot);
        var reportFile = Path.Combine(
            feedbackRoot,
            $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{trackingThreadId}.json");
        var payload = new
        {
            classification,
            reason,
            threadId,
            includeLogs = includeLogs.Value,
            extraLogFiles,
            createdAt = DateTimeOffset.UtcNow.ToString("o"),
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        });
        await File.WriteAllTextAsync(reportFile, json, cancellationToken).ConfigureAwait(false);

        await writeResultAsync(id, new
        {
            threadId = trackingThreadId,
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string? ReadString(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    private static bool? ReadBool(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
