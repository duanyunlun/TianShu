using System.Text.Json;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Turn input resolution 运行时，负责生成本轮有效用户输入。
/// Runtime that resolves the effective user input for a turn.
/// </summary>
internal sealed class KernelTurnInputResolutionRuntime
{
    private readonly KernelTurnSteerInputRuntime steerInputRuntime;
    private readonly Func<string?, string?> normalize;
    private readonly Func<string, object, string, CancellationToken, TimeSpan?, Task<JsonElement>> sendServerRequestAsync;

    public KernelTurnInputResolutionRuntime(
        KernelTurnSteerInputRuntime steerInputRuntime,
        Func<string?, string?> normalize,
        Func<string, object, string, CancellationToken, TimeSpan?, Task<JsonElement>> sendServerRequestAsync)
    {
        this.steerInputRuntime = steerInputRuntime ?? throw new ArgumentNullException(nameof(steerInputRuntime));
        this.normalize = normalize ?? throw new ArgumentNullException(nameof(normalize));
        this.sendServerRequestAsync = sendServerRequestAsync ?? throw new ArgumentNullException(nameof(sendServerRequestAsync));
    }

    public async Task ResolveAsync(TurnOperationState state, CancellationToken cancellationToken)
    {
        state.EffectiveUserText = await steerInputRuntime.MergeSteerInputsAsync(
            state.TurnId,
            state.OriginalUserText,
            cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(normalize(state.EffectiveUserText)))
        {
            return;
        }

        var requestUserInputResult = await sendServerRequestAsync(
            "item/tool/requestUserInput",
            new
            {
                threadId = state.ThreadId,
                turnId = state.TurnId,
                itemId = $"request_user_input_{state.TurnId}",
                questions = new[]
                {
                    new
                    {
                        id = "supplement",
                        header = "补充信息",
                        question = "请输入补充信息以继续。",
                        isOther = true,
                        isSecret = false,
                        options = new[]
                        {
                            new { label = "继续（推荐）", description = "继续当前任务。" },
                            new { label = "取消", description = "取消本次请求。" },
                        },
                    },
                },
            },
            state.ThreadId,
            cancellationToken,
            TimeSpan.FromMinutes(2)).ConfigureAwait(false);

        state.EffectiveUserText = TryExtractRequestUserInputText(requestUserInputResult)
                                  ?? throw new InvalidOperationException("用户输入请求已返回，但未提供 answers。");
    }

    private string? TryExtractRequestUserInputText(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("answers", out var answersElement)
            || answersElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (answersElement.TryGetProperty("supplement", out var supplement)
            && supplement.ValueKind == JsonValueKind.Object
            && supplement.TryGetProperty("answers", out var supplementAnswers)
            && supplementAnswers.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in supplementAnswers.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var normalized = normalize(entry.GetString());
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    return normalized;
                }
            }
        }

        foreach (var property in answersElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object
                || !property.Value.TryGetProperty("answers", out var answerArray)
                || answerArray.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in answerArray.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var normalized = normalize(entry.GetString());
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    return normalized;
                }
            }
        }

        return null;
    }
}
