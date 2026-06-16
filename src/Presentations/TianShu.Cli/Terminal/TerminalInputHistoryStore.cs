using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TianShu.Configuration;

namespace TianShu.Cli.Terminal;

/// <summary>
/// Persists interactive terminal input history under TianShu home.
/// 将交互式终端输入历史持久化到 TianShu 用户目录。
/// </summary>
internal sealed class TerminalInputHistoryStore
{
    private const string DirectoryName = "input-history";
    private const int FileNameThreadIdPrefixLength = 64;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string directoryPath;

    public TerminalInputHistoryStore(string? directoryPath = null)
        => this.directoryPath = string.IsNullOrWhiteSpace(directoryPath)
            ? ResolveDefaultDirectoryPath()
            : Path.GetFullPath(directoryPath);

    public string DirectoryPath => directoryPath;

    public static string ResolveDefaultDirectoryPath()
        => TianShuHomePathUtilities.ResolveDataPathFromHome(
            TianShuHomePathUtilities.ResolveTianShuHomePath(),
            DirectoryName);

    public IReadOnlyList<string> Load(string? threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (normalizedThreadId is null)
        {
            return [];
        }

        var path = ResolveThreadPath(normalizedThreadId);
        if (!File.Exists(path))
        {
            return [];
        }

        var entries = new List<string>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var record = JsonSerializer.Deserialize<InputHistoryRecord>(line, JsonOptions);
                if (record is { Text: { } text }
                    && string.Equals(record.ThreadId, normalizedThreadId, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(text))
                {
                    entries.Add(text.Trim());
                }
            }
            catch (JsonException)
            {
                // 单条损坏记录不应破坏交互启动。
            }
        }

        return entries;
    }

    public void Append(string? threadId, string text, TerminalSubmitIntent intent)
    {
        var normalizedThreadId = Normalize(threadId);
        if (normalizedThreadId is null)
        {
            return;
        }

        var normalized = text.Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        Directory.CreateDirectory(directoryPath);
        var record = new InputHistoryRecord(
            DateTimeOffset.UtcNow,
            normalizedThreadId,
            normalized,
            intent.ToString());
        File.AppendAllText(ResolveThreadPath(normalizedThreadId), JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);
    }

    public void ClearThread(string? threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (normalizedThreadId is null)
        {
            return;
        }

        var path = ResolveThreadPath(normalizedThreadId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void ClearAll()
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directoryPath, "*.jsonl", SearchOption.TopDirectoryOnly))
        {
            File.Delete(path);
        }
    }

    internal string ResolveThreadPath(string threadId)
        => System.IO.Path.Combine(directoryPath, BuildThreadFileName(threadId));

    private static string BuildThreadFileName(string threadId)
    {
        var normalizedThreadId = Normalize(threadId)
            ?? throw new ArgumentException("Thread id is required.", nameof(threadId));
        var safePrefix = BuildSafeFileNamePrefix(normalizedThreadId);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedThreadId)))
            .ToLowerInvariant()[..16];
        return $"{safePrefix}.{hash}.jsonl";
    }

    private static string BuildSafeFileNamePrefix(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(Math.Min(value.Length, FileNameThreadIdPrefixLength));
        foreach (var character in value)
        {
            if (builder.Length >= FileNameThreadIdPrefixLength)
            {
                break;
            }

            builder.Append(invalid.Contains(character) || char.IsControl(character) ? '_' : character);
        }

        return builder.Length == 0 ? "thread" : builder.ToString();
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

    private sealed record InputHistoryRecord(
        [property: JsonPropertyName("timestampUtc")] DateTimeOffset TimestampUtc,
        [property: JsonPropertyName("threadId")] string ThreadId,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("intent")] string Intent);
}
