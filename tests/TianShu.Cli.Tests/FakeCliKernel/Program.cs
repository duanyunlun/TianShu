using System.Text;
using System.Text.Json;

var scenarioPath = Environment.GetEnvironmentVariable("TIANSHU_FAKE_KERNEL_SCENARIO_PATH");
if (string.IsNullOrWhiteSpace(scenarioPath) || !File.Exists(scenarioPath))
{
    Console.Error.WriteLine("缺少 TIANSHU_FAKE_KERNEL_SCENARIO_PATH，或场景文件不存在。");
    return 1;
}

var requestLogPath = Environment.GetEnvironmentVariable("TIANSHU_FAKE_KERNEL_REQUEST_LOG_PATH");
var argsLogPath = Environment.GetEnvironmentVariable("TIANSHU_FAKE_KERNEL_ARGS_LOG_PATH");
if (!string.IsNullOrWhiteSpace(argsLogPath))
{
    File.WriteAllText(
        argsLogPath,
        JsonSerializer.Serialize(args, FakeKernelStatics.JsonOptions),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

FakeKernelScenario scenario;
await using (var scenarioStream = File.OpenRead(scenarioPath))
{
    scenario = await JsonSerializer.DeserializeAsync<FakeKernelScenario>(scenarioStream, FakeKernelStatics.JsonOptions).ConfigureAwait(false)
        ?? throw new InvalidOperationException("场景文件为空，无法启动 fake kernel。");
}

var handlers = scenario.Methods.ToDictionary(
    static pair => pair.Key,
    static pair => new Queue<FakeKernelMethodStep>(pair.Value),
    StringComparer.Ordinal);

while (true)
{
    var line = Console.In.ReadLine();
    if (line is null)
    {
        break;
    }

    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    if (!string.IsNullOrWhiteSpace(requestLogPath))
    {
        File.AppendAllText(
            requestLogPath,
            line + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    using var requestDocument = JsonDocument.Parse(line);
    var root = requestDocument.RootElement;
    if (!root.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
    {
        continue;
    }

    var method = methodElement.GetString();
    if (string.IsNullOrWhiteSpace(method))
    {
        continue;
    }

    if (!root.TryGetProperty("id", out var idElement))
    {
        // 客户端通知（例如 initialized）无需回包。
        continue;
    }

    if (!idElement.TryGetInt64(out var id))
    {
        await WriteErrorAsync(0, -32600, $"fake kernel 仅支持数值 id，当前 method={method}", null).ConfigureAwait(false);
        continue;
    }

    if (!handlers.TryGetValue(method, out var queue) || queue.Count == 0)
    {
        if (string.Equals(method, "initialize", StringComparison.Ordinal))
        {
            await WriteResultAsync(id, FakeKernelStatics.EmptyObject).ConfigureAwait(false);
            continue;
        }

        await WriteErrorAsync(id, -32601, $"fake kernel 未配置方法：{method}", null).ConfigureAwait(false);
        continue;
    }

    var step = queue.Dequeue();
    if (step.Error is not null)
    {
        await WriteErrorAsync(id, step.Error.Code, step.Error.Message, step.Error.Data).ConfigureAwait(false);
    }
    else
    {
        await WriteResultAsync(id, step.Result).ConfigureAwait(false);
    }

    foreach (var notification in step.Notifications)
    {
        if (notification.DelayMs > 0)
        {
            await Task.Delay(notification.DelayMs).ConfigureAwait(false);
        }

        await WriteNotificationAsync(notification.Method, notification.Params).ConfigureAwait(false);
    }
}

return 0;

static async Task WriteResultAsync(long id, JsonElement result)
{
    var payload = new Dictionary<string, object?>
    {
        ["id"] = id,
        ["result"] = result.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? FakeKernelStatics.EmptyObject : result,
    };
    await WriteLineAsync(payload).ConfigureAwait(false);
}

static async Task WriteErrorAsync(long id, int code, string message, JsonElement? data)
{
    var error = new Dictionary<string, object?>
    {
        ["code"] = code,
        ["message"] = message,
    };
    if (data.HasValue && data.Value.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
    {
        error["data"] = data.Value;
    }

    var payload = new Dictionary<string, object?>
    {
        ["id"] = id,
        ["error"] = error,
    };
    await WriteLineAsync(payload).ConfigureAwait(false);
}

static async Task WriteNotificationAsync(string method, JsonElement parameters)
{
    var payload = new Dictionary<string, object?>
    {
        ["method"] = method,
    };
    if (parameters.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
    {
        payload["params"] = parameters;
    }

    await WriteLineAsync(payload).ConfigureAwait(false);
}

static async Task WriteLineAsync(object payload)
{
    var json = JsonSerializer.Serialize(payload, FakeKernelStatics.JsonOptions);
    await Console.Out.WriteLineAsync(json).ConfigureAwait(false);
    await Console.Out.FlushAsync().ConfigureAwait(false);
}

internal sealed class FakeKernelScenario
{
    public Dictionary<string, List<FakeKernelMethodStep>> Methods { get; init; } = new(StringComparer.Ordinal);
}

internal sealed class FakeKernelMethodStep
{
    public JsonElement Result { get; init; }

    public FakeKernelError? Error { get; init; }

    public List<FakeKernelNotification> Notifications { get; init; } = [];
}

internal sealed class FakeKernelNotification
{
    public string Method { get; init; } = string.Empty;

    public int DelayMs { get; init; }

    public JsonElement Params { get; init; }
}

internal sealed class FakeKernelError
{
    public int Code { get; init; } = -32603;

    public string Message { get; init; } = "fake kernel error";

    public JsonElement? Data { get; init; }
}

internal static class FakeKernelStatics
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();
}
