using System.Text.Json;
using System.Text.Json.Serialization;
using TianShu.Contracts.Tools;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// 内部同步测试工具的运行时支撑，不属于官方 builtin 工具包。
/// Runtime support for the internal synchronization test tool; it is not part of the official builtin tool package.
/// </summary>
internal static class KernelTestSyncRuntimeSupport
{
    private const ulong DefaultTimeoutMs = 1000;

    public const string ToolName = "test_sync_tool";
    public const string ToolDescription = "Internal synchronization helper used by TianShu integration tests.";

    private static readonly object barrierGate = new();
    private static readonly Dictionary<string, BarrierState> barriers = new(StringComparer.Ordinal);

    public static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            sleep_before_ms = new { type = "number" },
            sleep_after_ms = new { type = "number" },
            barrier = new
            {
                type = "object",
                required = new[] { "id", "participants" },
                properties = new
                {
                    id = new { type = "string" },
                    participants = new { type = "number" },
                    timeout_ms = new { type = "number" },
                },
                additionalProperties = false,
            },
            elicitation = new
            {
                type = "object",
                required = new[] { "server_name", "mode", "message" },
                properties = new
                {
                    server_name = new { type = "string" },
                    mode = new { type = "string", @enum = new[] { "form", "url" } },
                    message = new { type = "string" },
                    requested_schema = new { type = new[] { "object", "array", "string", "number", "boolean", "null" } },
                    url = new { type = "string" },
                    elicitation_id = new { type = "string" },
                },
                additionalProperties = false,
            },
        },
        additionalProperties = false,
    });

    public static async Task<KernelToolResult> ExecuteAsync(
        JsonElement arguments,
        KernelToolCallContext context,
        CancellationToken cancellationToken)
    {
        TestSyncArgs? args;
        try
        {
            args = JsonSerializer.Deserialize<TestSyncArgs>(arguments);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return Failure($"failed to parse function arguments: {ex.Message}");
        }

        if (args is null)
        {
            return Failure("failed to parse function arguments: invalid type: null");
        }

        if (args.SleepBeforeMs is { } sleepBeforeMs && sleepBeforeMs > 0)
        {
            await DelayMsAsync(sleepBeforeMs, cancellationToken).ConfigureAwait(false);
        }

        if (args.Barrier is not null)
        {
            var barrierResult = await WaitOnBarrierAsync(args.Barrier, cancellationToken).ConfigureAwait(false);
            if (!barrierResult.Success)
            {
                return barrierResult;
            }
        }

        if (args.SleepAfterMs is { } sleepAfterMs && sleepAfterMs > 0)
        {
            await DelayMsAsync(sleepAfterMs, cancellationToken).ConfigureAwait(false);
        }

        if (args.Elicitation is null)
        {
            return Success("ok");
        }

        if (context.McpServerElicitationRequester is null)
        {
            return Failure("mcpServer/elicitation/request is unavailable");
        }

        var elicitationResponse = await context.McpServerElicitationRequester(
            new McpServerElicitationRequest(
                ServerName: args.Elicitation.ServerName,
                Mode: args.Elicitation.Mode,
                Message: args.Elicitation.Message,
                RequestedSchema: args.Elicitation.RequestedSchema,
                Url: args.Elicitation.Url,
                ElicitationId: args.Elicitation.ElicitationId,
                Meta: args.Elicitation.Meta),
            cancellationToken).ConfigureAwait(false);

        return Success(JsonSerializer.Serialize(new
        {
            action = elicitationResponse.Action,
            content = elicitationResponse.Content,
        }));
    }

    private static async Task DelayMsAsync(ulong delayMs, CancellationToken cancellationToken)
    {
        while (delayMs > 0)
        {
            var chunk = delayMs > int.MaxValue ? int.MaxValue : (int)delayMs;
            await Task.Delay(chunk, cancellationToken).ConfigureAwait(false);
            delayMs -= (ulong)chunk;
        }
    }

    private static async Task<KernelToolResult> WaitOnBarrierAsync(BarrierArgs args, CancellationToken cancellationToken)
    {
        if (args.Participants == 0)
        {
            return Failure("barrier participants must be greater than zero");
        }

        if (args.TimeoutMs == 0)
        {
            return Failure("barrier timeout must be greater than zero");
        }

        var barrierId = args.Id;
        var participants = args.Participants;

        BarrierState state;
        lock (barrierGate)
        {
            if (barriers.TryGetValue(barrierId, out state!))
            {
                if (state.Participants != participants)
                {
                    return Failure($"barrier {barrierId} already registered with {state.Participants} participants");
                }
            }
            else
            {
                state = new BarrierState(participants);
                barriers[barrierId] = state;
            }
        }

        bool isLeader;
        try
        {
            isLeader = await state.Barrier.WaitAsync(
                TimeSpan.FromMilliseconds((double)args.TimeoutMs),
                cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return Failure("test_sync_tool barrier wait timed out");
        }

        if (isLeader)
        {
            lock (barrierGate)
            {
                if (barriers.TryGetValue(barrierId, out var existing) && ReferenceEquals(existing.Barrier, state.Barrier))
                {
                    barriers.Remove(barrierId);
                }
            }
        }

        return Success("ok");
    }

    private static KernelToolResult Success(string output)
    {
        return new KernelToolResult(true, output);
    }

    private static KernelToolResult Failure(string output)
    {
        return new KernelToolResult(false, output);
    }

    private sealed class BarrierState
    {
        public BarrierState(ulong participants)
        {
            Participants = participants;
            Barrier = new AsyncBarrier(participants);
        }

        public ulong Participants { get; }

        public AsyncBarrier Barrier { get; }
    }

    private sealed class AsyncBarrier
    {
        private readonly ulong participants;
        private readonly object gate = new();

        private ulong count;
        private TaskCompletionSource<object?> signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AsyncBarrier(ulong participants)
        {
            this.participants = participants;
        }

        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            TaskCompletionSource<object?> toAwait;
            lock (gate)
            {
                count++;
                if (count == participants)
                {
                    count = 0;
                    var current = signal;
                    signal = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    current.SetResult(null);
                    return true;
                }

                toAwait = signal;
            }

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            try
            {
                await toAwait.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                return false;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                lock (gate)
                {
                    if (ReferenceEquals(signal, toAwait) && count > 0)
                    {
                        count--;
                    }
                }

                throw new TimeoutException();
            }
            catch (OperationCanceledException)
            {
                lock (gate)
                {
                    if (ReferenceEquals(signal, toAwait) && count > 0)
                    {
                        count--;
                    }
                }

                throw;
            }
        }
    }

    private sealed record TestSyncArgs(
        [property: JsonPropertyName("sleep_before_ms")] ulong? SleepBeforeMs,
        [property: JsonPropertyName("sleep_after_ms")] ulong? SleepAfterMs,
        [property: JsonPropertyName("barrier")] BarrierArgs? Barrier,
        [property: JsonPropertyName("elicitation")] ElicitationArgs? Elicitation);

    private sealed record ElicitationArgs
    {
        [JsonPropertyName("server_name")]
        public required string ServerName { get; init; }

        [JsonPropertyName("mode")]
        public required string Mode { get; init; }

        [JsonPropertyName("message")]
        public required string Message { get; init; }

        [JsonPropertyName("_meta")]
        public JsonElement? Meta { get; init; }

        [JsonPropertyName("requested_schema")]
        public JsonElement? RequestedSchema { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("elicitation_id")]
        public string? ElicitationId { get; init; }
    }

    private sealed record BarrierArgs
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("participants")]
        public required ulong Participants { get; init; }

        [JsonPropertyName("timeout_ms")]
        public ulong TimeoutMs { get; init; } = DefaultTimeoutMs;
    }
}

/// <summary>
/// AppHost 内部注册用的测试工具端点，保留 MCP / Responses 同步测试入口但不暴露为 builtin Provider。
/// Internal AppHost endpoint for the test tool, preserving MCP / Responses synchronization tests without exposing a builtin provider.
/// </summary>
internal sealed class KernelTestSyncRuntimeEndpoint : IKernelToolHandler
{
    public string Name => KernelTestSyncRuntimeSupport.ToolName;

    public string Description => KernelTestSyncRuntimeSupport.ToolDescription;

    public bool IsMutating => false;

    public bool SupportsParallelToolCalls => true;

    public JsonElement InputSchema => KernelTestSyncRuntimeSupport.InputSchema.Clone();

    public JsonElement? OutputSchema => null;

    public ToolImplementationBinding ImplementationBinding { get; } = new(
        "tianshu.internal.test-sync",
        ToolImplementationKind.Managed);

    public ProviderResponsesToolDefinition BuildProviderToolDefinition()
        => new ProviderResponsesFunctionToolDefinition(
            Name,
            Description,
            InputSchema,
            OutputSchema,
            strict: false);

    public Task<KernelToolResult> ExecuteAsync(
        JsonElement arguments,
        KernelToolCallContext context,
        CancellationToken cancellationToken)
        => KernelTestSyncRuntimeSupport.ExecuteAsync(arguments, context, cancellationToken);

    public Task<KernelToolResult> ExecuteCustomAsync(
        string input,
        KernelToolCallContext context,
        CancellationToken cancellationToken)
    {
        _ = input;
        _ = context;
        _ = cancellationToken;
        return Task.FromResult(new KernelToolResult(false, $"工具 {Name} 不支持 freeform 输入。"));
    }
}
