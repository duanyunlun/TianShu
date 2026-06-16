using System.Text.Json;
using System.Threading.Channels;

namespace TianShu.AppHost.Tests;

internal static class KernelAppServerTestProtocol
{
    internal static string WithInitialize(string input, bool experimentalApi = true)
    {
        if (string.IsNullOrWhiteSpace(input)
            || input.Contains("\"method\":\"initialize\"", StringComparison.Ordinal))
        {
            return input;
        }

        return string.Join(Environment.NewLine, CreateInitializeRequest(experimentalApi), input);
    }

    internal static async Task InitializeAsync(
        ChannelWriter<string> inputWriter,
        ChannelReader<string> outputLines,
        TimeSpan timeout,
        bool experimentalApi = true)
    {
        if (!inputWriter.TryWrite(CreateInitializeRequest(experimentalApi)))
        {
            throw new InvalidOperationException("无法写入 initialize 请求。");
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        while (await outputLines.WaitToReadAsync(timeoutCts.Token).ConfigureAwait(false))
        {
            while (outputLines.TryRead(out var line))
            {
                JsonDocument? message = null;
                try
                {
                    message = JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                using (message)
                {
                    if (!IsResponseId(message.RootElement, 0))
                    {
                        continue;
                    }

                    if (message.RootElement.TryGetProperty("error", out var error))
                    {
                        throw new InvalidOperationException(
                            $"initialize 失败：{error.GetProperty("message").GetString()}");
                    }

                    return;
                }
            }
        }

        throw new TimeoutException("等待 initialize 响应超时。");
    }

    internal static string CreateInitializeRequest(bool experimentalApi)
        => $"{{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\",\"params\":{{\"capabilities\":{{\"experimentalApi\":{experimentalApi.ToString().ToLowerInvariant()}}}}}}}";

    private static bool IsResponseId(JsonElement json, long id)
        => json.TryGetProperty("id", out var idElement)
           && idElement.ValueKind == JsonValueKind.Number
           && idElement.TryGetInt64(out var numericId)
           && numericId == id;
}
