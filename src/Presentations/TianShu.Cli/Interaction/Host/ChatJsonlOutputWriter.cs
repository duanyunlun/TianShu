using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.Cli.Interaction.Host;

/// <summary>
/// Writes JSONL chat output protocol frames.
/// 写入 chat JSONL 输出协议帧，集中维护 stdout/stderr 与 partial 字段形状。
/// </summary>
internal static class ChatJsonlOutputWriter
{
    public static void WriteStdout(string text, bool partial)
        => Write("stdout", text, partial);

    public static void WriteStderr(string text, bool partial)
        => Write("stderr", text, partial);

    public static void Write(string type, string text, bool partial)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Console.WriteLine(JsonSerializer.Serialize(new ChatJsonlOutputFrame(type, text, partial)));
    }

    private readonly record struct ChatJsonlOutputFrame(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("partial")] bool Partial);
}
