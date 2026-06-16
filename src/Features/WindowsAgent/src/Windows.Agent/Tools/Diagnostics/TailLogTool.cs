using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Windows.Agent.Tools.Diagnostics;

/// <summary>
/// 诊断工具：读取日志文件末尾 N 行（tail）。
/// </summary>
public sealed class TailLogTool
{
    private readonly ILogger<TailLogTool> _logger;

    public TailLogTool(ILogger<TailLogTool> logger)
    {
        _logger = logger;
    }

    [Description("Read the last N lines of a log file")]
    public async Task<string> TailAsync(
        [Description("Log file path")] string path,
        [Description("Number of lines to read from the end")] int lines = 200)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return JsonSerializer.Serialize(new { success = false, message = "path is required" }, new JsonSerializerOptions { WriteIndented = true });
            }

            if (lines <= 0)
            {
                return JsonSerializer.Serialize(new { success = false, message = "lines must be > 0" }, new JsonSerializerOptions { WriteIndented = true });
            }

            if (!File.Exists(path))
            {
                return JsonSerializer.Serialize(new { success = false, path, message = "log file not found" }, new JsonSerializerOptions { WriteIndented = true });
            }

            var buffer = new Queue<string>(capacity: Math.Min(lines, 1024));

            // 允许其它进程持续写日志：ReadWrite 共享模式。
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line == null)
                {
                    break;
                }

                buffer.Enqueue(line);
                while (buffer.Count > lines)
                {
                    buffer.Dequeue();
                }
            }

            var payload = new
            {
                success = true,
                path,
                linesRequested = lines,
                lineCount = buffer.Count,
                lines = buffer.ToArray()
            };

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to tail log: {Path}", path);
            var payload = new { success = false, path, message = ex.Message };
            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}

